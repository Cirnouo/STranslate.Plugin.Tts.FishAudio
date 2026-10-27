#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ArgumentsPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$process = $null
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
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
    $start = [Diagnostics.ProcessStartInfo]::new($invocation.executable)
    $start.WorkingDirectory = $invocation.workingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    foreach ($argument in $invocation.arguments) { $start.ArgumentList.Add([string]$argument) }
    # Inherit the raw stdout/stderr handles. The shim never decodes or rewrites
    # target output; the parent drains each pipe independently into private files.
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (-not $process.Start()) { throw 'test-process-not-started' }
    [void]$process.WaitForExitAsync().GetAwaiter().GetResult()
    exit $process.ExitCode
} catch {
    [Console]::Error.WriteLine('test-process: failed (' + $_.Exception.GetType().Name + ').')
    exit 1
} finally {
    if ($null -ne $process) { $process.Dispose() }
}
