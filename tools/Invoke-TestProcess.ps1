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
    $start = [Diagnostics.ProcessStartInfo]::new($invocation.executable)
    $start.WorkingDirectory = $invocation.workingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in $invocation.arguments) { $start.ArgumentList.Add([string]$argument) }
    # CreateNoWindow targets must receive explicit pipes. Forward both streams
    # concurrently as bytes; neither PowerShell nor StreamReader decodes output.
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (-not $process.Start()) { throw 'test-process-not-started' }
    $started = $true
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
        [void]$pending.Remove($completed)
    }
    exit $process.ExitCode
} catch {
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
    if ($null -ne $process) { $process.Dispose() }
}
