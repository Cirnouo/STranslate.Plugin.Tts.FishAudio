#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ArgumentsPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$process = $null
$started = $false
$stdout = $null
$stderr = $null
$exit = $null
$driverPipe = $null
$driverWriter = $null
$driverContext = $null
$primaryFailed = $false
function Publish-DriverEvent([string]$Kind) {
    if ($null -eq $driverWriter) { return }
    $record = [ordered]@{
        schemaVersion = 1; role = 'execution-launcher'; nonce = $driverContext.nonce
        runId = $driverContext.runId; sourceTree = $driverContext.sourceTree
        pid = $PID; kind = $Kind; targetPid = $process.Id
        timestamp = [Diagnostics.Stopwatch]::GetTimestamp(); frequency = [Diagnostics.Stopwatch]::Frequency
        exitCode = $(if ($Kind -ceq 'target-exited') { $process.ExitCode } else { $null })
    }
    $driverWriter.WriteLine(($record | ConvertTo-Json -Compress -Depth 4))
}
try {
    # The parent assigns this waiting shim to its Windows Job before releasing
    # GO. No real target, and therefore no target descendant, starts beforehand.
    if ([Console]::ReadLine() -cne 'GO') { throw 'test-process-missing-owner' }
    $invocation = [IO.File]::ReadAllText($ArgumentsPath) | ConvertFrom-Json
    if ($invocation.schemaVersion -ne 1 -or
        -not [IO.Path]::IsPathFullyQualified($invocation.executable) -or
        -not [IO.Path]::IsPathFullyQualified($invocation.workingDirectory)) {
        throw 'test-process-invalid-invocation'
    }
    if ($null -ne $invocation.PSObject.Properties['driverControl']) {
        $driverContext = $invocation.driverControl
        if ($driverContext.pipeName -cnotmatch '^fa-fw-[0-9a-f]{32}-launcher$' -or
            $driverContext.nonce -cnotmatch '^[0-9a-f]{64}$' -or
            $driverContext.runId -cnotmatch '^[0-9a-f]{32}$' -or
            ($null -ne $driverContext.sourceTree -and ($driverContext.sourceTree -isnot [string] -or
                $driverContext.sourceTree -cnotmatch '^([0-9a-f]{40}|[0-9a-f]{64})$'))) {
            throw 'test-process-invalid-driver-context'
        }
        $driverPipe = [IO.Pipes.NamedPipeClientStream]::new('.', $driverContext.pipeName,
            [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
        $driverPipe.ConnectAsync(120000).GetAwaiter().GetResult()
        $acknowledgement = [IO.StreamReader]::new($driverPipe, [Text.UTF8Encoding]::new($false, $true), $false, 256, $true)
        try {
            if ($acknowledgement.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(120)).GetAwaiter().GetResult() -cne
                ('framework-owner-ready:' + $driverContext.nonce)) { throw 'test-process-owner-not-ready' }
        } finally { $acknowledgement.Dispose() }
        $driverWriter = [IO.StreamWriter]::new($driverPipe, [Text.UTF8Encoding]::new($false, $true), 4096, $true)
        $driverWriter.AutoFlush = $true
    }
    $start = [Diagnostics.ProcessStartInfo]::new($invocation.executable)
    $start.WorkingDirectory = $invocation.workingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $invocation.arguments) { $start.ArgumentList.Add([string]$argument) }
    if ($null -ne $invocation.PSObject.Properties['environment']) {
        foreach ($entry in $invocation.environment.PSObject.Properties) {
            $start.Environment[$entry.Name] = [string]$entry.Value
        }
    }
    # CreateNoWindow targets must receive explicit pipes. Forward both streams
    # concurrently as bytes; neither PowerShell nor StreamReader decodes output.
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (-not $process.Start()) { throw 'test-process-not-started' }
    $started = $true
    Publish-DriverEvent 'target-started'
    $stdout = $process.StandardOutput.BaseStream.CopyToAsync([Console]::OpenStandardOutput())
    $stderr = $process.StandardError.BaseStream.CopyToAsync([Console]::OpenStandardError())
    $exit = $process.WaitForExitAsync()
    $pending = [Collections.Generic.List[Threading.Tasks.Task]]::new()
    foreach ($task in @($exit, $stdout, $stderr)) { $pending.Add($task) }
    while ($pending.Count -gt 0) {
        # WhenAll delays an early forwarding failure until the target exits.
        # That target can be blocked writing the pipe whose forwarder failed.
        $completed = [Threading.Tasks.Task]::WhenAny($pending.ToArray()).GetAwaiter().GetResult()
        [void]$completed.GetAwaiter().GetResult()
        if ([object]::ReferenceEquals($completed, $exit)) {
            # Publish real target termination before waiting for pipes held by
            # descendants. The external owner gives exit and both drains one
            # shared allowance after authoritative result verification.
            Publish-DriverEvent 'target-exited'
        }
        [void]$pending.Remove($completed)
    }
    exit $process.ExitCode
} catch {
    $primaryFailed = $true
    if ($started) {
        # This is failure cleanup, not proof of Job or descendant completion.
        # The parent still owns its Job; the original exit and forwarding tasks
        # share one cleanup allowance, including target-tree termination.
        $cleanup = [Diagnostics.Stopwatch]::StartNew()
        try { if (-not $process.HasExited) { $process.Kill($true) } } catch { }
        try {
            $remaining = [TimeSpan]::FromSeconds(10) - $cleanup.Elapsed
            if ($remaining -gt [TimeSpan]::Zero) {
                $tasks = [Threading.Tasks.Task[]]@(@($exit, $stdout, $stderr) | Where-Object { $null -ne $_ })
                [void][Threading.Tasks.Task]::WhenAll($tasks).WaitAsync($remaining).GetAwaiter().GetResult()
            }
        } catch { } # Preserve the forwarding/start failure as a nonzero exit.
    }
    # A forwarding failure must not attempt another potentially blocked write
    # to the same output pipes. The parent observes this shim's nonzero exit.
    if (-not $started) { try { [Console]::Error.WriteLine('test-process-failed') } catch { } }
    exit 1
} finally {
    # Close the pipe first: a prior failed publication must never flush into the
    # same blocked transport again. Every secondary release is independent.
    $releaseFailed = $false
    if ($primaryFailed) {
        try { if ($null -ne $driverPipe) { $driverPipe.Dispose() } } catch { $releaseFailed = $true }
    }
    try { if ($null -ne $driverWriter) { $driverWriter.Dispose() } } catch { $releaseFailed = $true }
    try { if ($null -ne $driverPipe) { $driverPipe.Dispose() } } catch { $releaseFailed = $true }
    try { if ($null -ne $process) { $process.Dispose() } } catch { $releaseFailed = $true }
    if ($releaseFailed -and -not $primaryFailed) { exit 1 }
}
