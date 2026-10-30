#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$RepositoryRoot,[string]$CheckNonce,[int]$CheckExitCode,[switch]$FinalizeCommit,[string]$MessagePath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$encoding = [Text.UTF8Encoding]::new($false)
$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RepositoryRoot))
function Invoke-GateGit([string[]]$Arguments) {
    $output = & git -C $root @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'gate-git-failed' }
    return ($output -join "`n").Trim()
}
function Get-GateDigest([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$state = Invoke-GateGit @('rev-parse','--path-format=absolute','--git-path','fish-audio-testing')
$installedRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$installationPath = Join-Path $installedRoot 'installation.json'
$installation = [IO.File]::ReadAllText($installationPath) | ConvertFrom-Json
function Assert-GateInstallation {
    $effective = [IO.Path]::GetFullPath((Invoke-GateGit @('rev-parse','--path-format=absolute','--git-path','hooks')))
    $expected = [IO.Path]::GetFullPath((Join-Path $state ('hooks/bundles/'+$installation.bundleId)))
    if ($installation.schemaVersion -ne 2 -or $installation.gateProtocolVersion -ne 3 -or
        $installation.bundleId -cnotmatch '^[a-f0-9]{32}$' -or
        -not $root.Equals($installation.repositoryRoot,[StringComparison]::OrdinalIgnoreCase) -or
        -not $effective.Equals($installedRoot,[StringComparison]::OrdinalIgnoreCase) -or
        -not $expected.Equals($installedRoot,[StringComparison]::OrdinalIgnoreCase) -or
        -not $installedRoot.Equals($installation.installedRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'gate-installation-owner-mismatch' }
}
Assert-GateInstallation
$installationGuards = [Collections.Generic.List[IO.FileStream]]::new()
try {
    # Pin the installed code and its exact installation descriptor before the
    # final guard loads any consumer. No snapshot/build fallback is possible.
    $installationGuards.Add([IO.File]::Open($installationPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read))
    foreach($name in @('pre-commit','commit-msg','TestGate.ps1','HybridReceipt.ps1')) {
        $path=Join-Path $installedRoot $name
        $installationGuards.Add([IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read))
        if ($installation.digests.$name -cnotmatch '^[a-f0-9]{64}$' -or (Get-GateDigest $path) -cne $installation.digests.$name) { throw 'gate-installed-consumer-digest' }
    }
    if ($CheckNonce) {
        $challengePath=Join-Path $installedRoot 'check.json'
        $challenge=[IO.File]::ReadAllText($challengePath)|ConvertFrom-Json
        $age=([DateTimeOffset]::UtcNow-[DateTimeOffset]::Parse($challenge.createdUtc)).TotalSeconds
        if ($CheckNonce -cnotmatch '^[a-f0-9]{32}$' -or $challenge.nonce -cne $CheckNonce -or $challenge.exitCode -ne $CheckExitCode -or $age -lt 0 -or $age -gt 60) { throw 'gate-invalid-installation-challenge' }
        [IO.File]::Delete($challengePath)
        [IO.File]::WriteAllText((Join-Path $installedRoot 'check-ack'),$CheckNonce,$encoding)
        exit $CheckExitCode
    }
    $gateLock=[IO.File]::Open((Join-Path $state 'gate.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
        Assert-GateInstallation
        $receiptPath=Join-Path $state 'commit-receipt.json'
        if ($FinalizeCommit) {
            if ($installation.previousHooks -and [IO.File]::Exists((Join-Path $installation.previousHooks 'commit-msg'))) {
                & git -C $root -c ('core.hooksPath='+$installation.previousHooks) hook run commit-msg -- $MessagePath
                if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            }
            . (Join-Path $installedRoot 'HybridReceipt.ps1')
            try {
                [void](Lock-HybridFile $receiptPath)
                $receipt=Read-HybridJson $receiptPath
                if ($receipt.runId -cnotmatch '^[a-f0-9]{32}$' -or $receipt.gateProtocolVersion -ne 3) { throw 'gate-invalid-commit-receipt' }
                $evidenceRoot=Join-Path $state ('runs/'+$receipt.runId)
                $completedGatePath=Join-Path $evidenceRoot 'gate.json'
                [void](Lock-HybridFile $completedGatePath ([string]$receipt.gateSha256))
                $completedGate=Read-HybridJson $completedGatePath
                if ($completedGate.installationSha256 -cne (Get-GateDigest $installationPath) -or
                    $completedGate.receiptConsumerSha256 -cne $installation.digests.'HybridReceipt.ps1') { throw 'gate-final-consumer-changed' }
                Assert-GateHybridReceipt $evidenceRoot $completedGate
                $finalTree=Invoke-GateGit @('write-tree')
                $finalIndex=Invoke-GateGit @('rev-parse','--path-format=absolute','--git-path','index')
                if ($env:GIT_INDEX_FILE) { $finalIndex=[IO.Path]::GetFullPath($env:GIT_INDEX_FILE,$root) }
                [void](Lock-HybridFile $finalIndex)
                if ($completedGate.snapshotTree -cne $receipt.tree -or $finalTree -cne $receipt.tree -or
                    -not $finalIndex.Equals($receipt.indexPath,[StringComparison]::OrdinalIgnoreCase) -or
                    (Get-GateDigest $finalIndex) -cne $receipt.indexHash) { throw 'gate-index-changed-after-pre-commit' }
                # Repeat exact directory inventories immediately before consume;
                # new unlisted physical attempts/generations cannot borrow pass.
                Assert-GateHybridReceipt $evidenceRoot $completedGate
                # Release only the consumed receipt path for its own rename;
                # index, manifest, consumer and raw evidence remain held.
                for($i=$script:HybridLocks.Count-1;$i -ge 0;$i--) {
                    if(Test-HybridSamePath $script:HybridLocks[$i].Name $receiptPath) {
                        $script:HybridLocks[$i].Dispose();$script:HybridLocks.RemoveAt($i)
                    }
                }
                [IO.File]::Move($receiptPath,(Join-Path $evidenceRoot 'commit-final.json'))
                exit 0
            } finally { Close-HybridLocks }
        }
        $exitCode=1;$manifest=$null;$runRoot=$null;$snapshotRoot=$null;$snapshotOwned=$false;$runId=$null
        $snapshotParent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $stage='prior-hook';$primary=$null
        function Remove-GateSnapshot {
            if (-not $snapshotOwned -or -not $snapshotRoot -or -not [IO.Directory]::Exists($snapshotRoot)) { return }
            $resolved=(Resolve-Path -LiteralPath $snapshotRoot).ProviderPath
            if ($runId -cnotmatch '^[a-f0-9]{32}$' -or
                ((Get-Item -LiteralPath $resolved -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                [IO.Path]::GetFileName($resolved) -cne ('fa-s-'+$runId) -or
                -not [IO.Path]::GetDirectoryName($resolved).Equals([IO.Path]::TrimEndingDirectorySeparator($snapshotParent),[StringComparison]::OrdinalIgnoreCase)) { throw 'gate-unsafe-snapshot-cleanup' }
            Remove-Item -LiteralPath $resolved -Recurse -Force
            $script:snapshotOwned=$false
        }
        function Invoke-GatePhase([string]$Name,[string]$Executable,[string[]]$Arguments,[int]$MetadataTimeoutMilliseconds=0) {
            $savedContext=$env:FISHAUDIO_SNAPSHOT_CONTEXT
            try {
                $env:FISHAUDIO_SNAPSHOT_CONTEXT=$contextPath
                $code=Invoke-HybridProcess $Executable $Arguments (Join-Path $runRoot $Name) $snapshotRoot $MetadataTimeoutMilliseconds
                if ($code -ne 0) { throw 'gate-validation-command-failed' }
            } finally {
                $env:FISHAUDIO_SNAPSHOT_CONTEXT=$savedContext
                if($Name -ceq 'build-package'){
                    foreach($pair in @(@('stdout.bin','build.stdout.log'),@('stderr.bin','build.stderr.log'))){
                        $raw=Join-Path (Join-Path $runRoot $Name) $pair[0]
                        if([IO.File]::Exists($raw)){[IO.File]::Copy($raw,(Join-Path $runRoot $pair[1]),$false)}
                    }
                }
            }
        }
        try {
            [IO.File]::Delete($receiptPath)
            if ($installation.previousHooks -and [IO.File]::Exists((Join-Path $installation.previousHooks 'pre-commit'))) {
                & git -C $root -c ('core.hooksPath='+$installation.previousHooks) hook run pre-commit
                if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            }
            $stage='freeze-index'
            if (Invoke-GateGit @('ls-files','--unmerged')) { throw 'gate-unmerged-index' }
            $tree=Invoke-GateGit @('write-tree')
            if ($tree -cnotmatch '^[a-f0-9]{40}([a-f0-9]{24})?$') { throw 'gate-invalid-tree' }
            if ($env:FISHAUDIO_EXPECTED_TREE -and $env:FISHAUDIO_EXPECTED_TREE -cne $tree) { throw 'gate-unexpected-index-tree' }
            $indexPath=Invoke-GateGit @('rev-parse','--path-format=absolute','--git-path','index')
            if ($env:GIT_INDEX_FILE) { $indexPath=[IO.Path]::GetFullPath($env:GIT_INDEX_FILE,$root) }
            $indexHash=Get-GateDigest $indexPath
            $head=& git -C $root rev-parse --verify HEAD 2>$null
            $sourceCommit=if($LASTEXITCODE -eq 0){($head -join '').Trim()}else{$null}
            $runId=[Guid]::NewGuid().ToString('N');$runRoot=Join-Path $state ('runs/'+$runId)
            [IO.Directory]::CreateDirectory($runRoot)|Out-Null
            $snapshotRoot=Join-Path $snapshotParent ('fa-s-'+$runId)
            if ([IO.Directory]::Exists($snapshotRoot)) { throw 'gate-snapshot-already-exists' }
            if ((Join-Path $snapshotRoot 'tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests.exe').Length -ge 260) { throw 'gate-snapshot-path-too-long' }
            [IO.Directory]::CreateDirectory($snapshotRoot)|Out-Null;$snapshotOwned=$true
            $manifest=[ordered]@{schemaVersion=1;gateProtocolVersion=3;mode='bootstrap/full/hybrid';runId=$runId
                snapshotTree=$tree;sourceCommit=$sourceCommit;complete=$false;exitCode=1;startedUtc=[DateTimeOffset]::UtcNow.ToString('O');finishedUtc=$null
                installationSha256=(Get-GateDigest $installationPath);receiptConsumerSha256=$installation.digests.'HybridReceipt.ps1'
                validation=@{package='build-package/process.json';session='hybrid/manifest.json';admission='hybrid-admission/admission.json'}
                hybridVerified=$false;hybridArtifacts=@();hybrid=$null;failure=$null}
            [IO.File]::WriteAllText((Join-Path $runRoot 'gate.json'),($manifest|ConvertTo-Json -Depth 12),$encoding)
            Write-Host ('[gate] bootstrap/full/hybrid protocol3; S='+$tree+'; run='+$runId)

    $stage = 'materialize-snapshot'
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
public static class FishAudioGitSnapshot
{
    static byte[] Git(string root, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new IOException("snapshot-git-start");
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        try
        {
            if (!process.WaitForExit(120000) || !Task.WhenAll(copy, error).Wait(10000)) throw new IOException("snapshot-git-timeout");
            if (process.ExitCode != 0) throw new IOException("snapshot-git-failed");
            return output.ToArray();
        }
        finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(10000); } }
    }
    public static List<Dictionary<string,string>> Create(string root, string tree, string destination)
    {
        var files = new List<Dictionary<string,string>>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;
        foreach (var record in new UTF8Encoding(false, true).GetString(Git(root,"ls-tree","-rz","--full-tree",tree)).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab < 0) throw new IOException("snapshot-invalid-tree-record");
            var header = record.Substring(0,tab).Split(' ');
            var name = record.Substring(tab+1);
            if (header.Length != 3 || header[1] != "blob" || (header[0] != "100644" && header[0] != "100755"))
                throw new IOException("snapshot-unsupported-file-mode");
            if (name.Contains('\\') || Path.IsPathRooted(name) || !names.Add(name)) throw new IOException("snapshot-invalid-path");
            foreach (var part in name.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    part.EndsWith(".",StringComparison.Ordinal) || part.EndsWith(" ",StringComparison.Ordinal)) throw new IOException("snapshot-invalid-path");
            var target = Path.GetFullPath(Path.Combine(destination,name));
            if (!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new IOException("snapshot-path-escape");
            var bytes = Git(root,"cat-file","blob",header[2]);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllBytes(target,bytes);
            files.Add(new Dictionary<string,string> { ["path"]=name, ["blob"]=header[2],
                ["sha256"]=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), ["mode"]=header[0] });
        }
        if (files.Count == 0) throw new IOException("snapshot-empty-tree");
        return files;
    }
}
'@
    $files = [FishAudioGitSnapshot]::Create($root, $tree, $snapshotRoot)
    if (-not [IO.File]::Exists((Join-Path $snapshotRoot 'build.ps1'))) { throw 'gate-snapshot-missing-build' }
    $contextPath = Join-Path $runRoot 'snapshot.json'
    $context = @{ schemaVersion = 1; root = $snapshotRoot; tree = $tree; sourceCommit = $sourceCommit; files = $files.ToArray() }
    [IO.File]::WriteAllText($contextPath, ($context | ConvertTo-Json -Depth 12), $encoding)

            foreach($relative in @('tools/TestHybrid.ps1','tools/Assert-HybridSession.ps1','tools/HybridSession.Common.ps1','tools/TestProcessOwner.ps1','tools/Invoke-TestProcess.ps1')) {
                if(-not [IO.File]::Exists((Join-Path $snapshotRoot $relative))){throw 'gate-snapshot-missing-hybrid-input'}
            }
            . (Join-Path $snapshotRoot 'tools/HybridSession.Common.ps1')
            $shell=(Get-Process -Id $PID).Path
            $dotnet=(Get-Command dotnet -CommandType Application -ErrorAction Stop|Select-Object -First 1).Source
            $stage='build-release-package'
            Invoke-GatePhase 'build-package' $shell @('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $snapshotRoot 'build.ps1'),'-Release')
            # Build the same two Release projects with the standard driver flags;
            # this preparation neither discovers nor executes a scenario.
            foreach($definition in @(
                @('plugin','tests/STranslate.Plugin.Tts.FishAudio.Tests/STranslate.Plugin.Tts.FishAudio.Tests.csproj'),
                @('testing-tool','tests/FishAudio.Testing.Tests/FishAudio.Testing.Tests.csproj'))) {
                $stage='build-'+$definition[0]
                Invoke-GatePhase ('build-'+$definition[0]) $dotnet @('build',(Join-Path $snapshotRoot $definition[1]),'--configuration','Release',
                    '--disable-build-servers','-p:RestoreLockedMode=true','-p:FishAudioCoverage=false','-p:UseSharedCompilation=false','-nodeReuse:false')
            }
            $stage='hybrid-full'
            $hybridRoot=Join-Path $runRoot 'hybrid'
            Invoke-GatePhase 'hybrid-command' $shell @('-NoLogo','-NoProfile','-NonInteractive','-File',
                (Join-Path $snapshotRoot 'tools/TestHybrid.ps1'),'-RepositoryRoot',$snapshotRoot,'-ResultsDirectory',$hybridRoot,'-NoBuild','-Workers','4')
            $stage='hybrid-independent-admission'
            $admissionRoot=Join-Path $runRoot 'hybrid-admission'
            Invoke-GatePhase 'hybrid-admission-command' $shell @('-NoLogo','-NoProfile','-NonInteractive','-File',
                (Join-Path $snapshotRoot 'tools/Assert-HybridSession.ps1'),'-RepositoryRoot',$snapshotRoot,'-SessionDirectory',$hybridRoot,'-OutputDirectory',$admissionRoot)
            # Use the installed, independently digest-bound minimal consumer for
            # receipt sealing and final commit-msg. No build DLL is imported.
            . (Join-Path $installedRoot 'HybridReceipt.ps1')
            $admissionPath=Join-Path $admissionRoot 'admission.json'
            $admission=Read-HybridJson $admissionPath
            $artifacts=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
            function Add-GateArtifact([string]$Path,[string]$Digest) {
                if (-not (Test-HybridContained $Path $runRoot)) { throw 'gate-artifact-outside-retained-run' }
                $relative=[IO.Path]::GetRelativePath($runRoot,$Path).Replace('\','/')
                [void](Assert-GateReceiptPath $runRoot $relative)
                [void](Lock-HybridFile $Path $Digest -Retained)
                if($artifacts.ContainsKey($relative)) {
                    if($artifacts[$relative] -cne $Digest){throw 'gate-artifact-digest-conflict'}
                } else { $artifacts.Add($relative,$Digest) }
            }
            foreach($artifact in $admission.receiptArtifacts) { Add-GateArtifact ([string]$artifact.path) ([string]$artifact.sha256) }
            Add-GateArtifact $admissionPath (Get-GateDigest $admissionPath)
            Add-GateArtifact $contextPath (Get-GateDigest $contextPath)
            foreach($name in @('build.stdout.log','build.stderr.log')){$path=Join-Path $runRoot $name;Add-GateArtifact $path (Get-GateDigest $path)}
            foreach($directory in @('build-package','build-plugin','build-testing-tool','hybrid-command','hybrid-admission-command')) {
                foreach($file in Get-ChildItem -LiteralPath (Join-Path $runRoot $directory) -Recurse -File -Force) { Add-GateArtifact $file.FullName (Get-GateDigest $file.FullName) }
            }
            $package=Join-Path $snapshotRoot 'STranslate.Plugin.Tts.FishAudio.spkg'
            if(-not [IO.File]::Exists($package)){throw 'gate-current-package-missing'}
            [IO.File]::Copy($package,(Join-Path $runRoot 'package.spkg'),$false)
            $manifest['packageSha256']=Get-GateDigest (Join-Path $runRoot 'package.spkg')
            Add-GateArtifact (Join-Path $runRoot 'package.spkg') $manifest.packageSha256
            $manifest.hybrid=@{session='hybrid';admission='hybrid-admission/admission.json';admissionSha256=(Get-GateDigest $admissionPath)}
            $manifest.hybridArtifacts=@($artifacts.GetEnumerator()|Sort-Object Key|ForEach-Object{@{path=$_.Key;sha256=$_.Value}})
            $manifest.hybridVerified=$true
            $session=Read-HybridJson (Join-Path $hybridRoot 'manifest.json')
            $manifest['legacyCaseCount']=[int](($session.slots|Where-Object owner -CEQ 'legacy'|ForEach-Object{@($_.selectedIds).Count}|Measure-Object -Sum).Sum)
            $manifest['frameworkCaseCount']=[int](($session.slots|Where-Object owner -CEQ 'framework'|ForEach-Object{@($_.selectedIds).Count}|Measure-Object -Sum).Sum)
            $stage='verify-final-index'
            if ((Invoke-GateGit @('write-tree')) -cne $tree -or (Get-GateDigest $indexPath) -cne $indexHash) { throw 'gate-index-changed-during-validation' }
            [void](Lock-HybridFile $indexPath $indexHash)
            $manifest.complete=$true;$manifest.exitCode=0
            Assert-GateHybridReceipt $runRoot ([pscustomobject]$manifest)
            $stage='snapshot-cleanup'
            Remove-GateSnapshot
            Assert-GateHybridReceipt $runRoot ([pscustomobject]$manifest)
            if((Get-GateDigest $indexPath) -cne $indexHash){throw 'gate-index-changed-during-validation'}
            $exitCode=0
        } catch {
            $primary=$_
            if($manifest){$manifest.complete=$false;$manifest.failure=@{stage=$stage;exceptionType=$_.Exception.GetType().FullName;scriptLine=$_.InvocationInfo.ScriptLineNumber
                reason=$(if($_.Exception.Message -cmatch '^gate-[a-z-]+$'){$_.Exception.Message}else{'gate-operation-failed'})}}
            Write-Host ('[gate] rejected at '+$stage+' ('+$_.Exception.GetType().Name+'); inspect private gate evidence.')
        } finally {
            $cleanupFailure=$null
            try {
                if($manifest -and $snapshotRoot -and [IO.File]::Exists((Join-Path $snapshotRoot 'STranslate.Plugin.Tts.FishAudio.spkg')) -and -not [IO.File]::Exists((Join-Path $runRoot 'package.spkg'))) {
                    [IO.File]::Copy((Join-Path $snapshotRoot 'STranslate.Plugin.Tts.FishAudio.spkg'),(Join-Path $runRoot 'package.spkg'),$false)
                    $manifest['packageSha256']=Get-GateDigest (Join-Path $runRoot 'package.spkg')
                }
            } catch {$cleanupFailure=$_}
            try {Remove-GateSnapshot} catch {if($null -eq $cleanupFailure){$cleanupFailure=$_}}
            if($null -ne $cleanupFailure){if($manifest){$manifest.complete=$false;$manifest['cleanupFailure']=$cleanupFailure.Exception.GetType().FullName};if($exitCode -eq 0){$exitCode=1}}
            if($manifest){
                $manifest.exitCode=$exitCode;$manifest.finishedUtc=[DateTimeOffset]::UtcNow.ToString('O')
                $completedGatePath=Join-Path $runRoot 'gate.json'
                if($exitCode -eq 0 -and $manifest.complete){
                    # The consumer and the artifact/directory inventories remain
                    # held while publishing the separate immutable list digest.
                    try {Assert-GateHybridReceipt $runRoot ([pscustomobject]$manifest)}
                    catch {$exitCode=1;$manifest.exitCode=1;$manifest.complete=$false;$manifest.failure=@{stage='final-receipt';exceptionType=$_.Exception.GetType().FullName;reason='gate-final-receipt-rejected'}}
                }
                [IO.File]::WriteAllText($completedGatePath,($manifest|ConvertTo-Json -Depth 12),$encoding)
                if($exitCode -eq 0 -and $manifest.complete){
                    $receipt=@{gateProtocolVersion=3;runId=$runId;tree=$tree;indexPath=$indexPath;indexHash=$indexHash;gateSha256=(Get-GateDigest $completedGatePath)}
                    [IO.File]::WriteAllText($receiptPath,($receipt|ConvertTo-Json),$encoding)
                    Write-Host ('[gate] passed legacy='+$manifest.legacyCaseCount+'; NUnit='+$manifest.frameworkCaseCount+'; S='+$tree+'; run='+$runId)
                }
            }
            if(Get-Command Close-HybridLocks -CommandType Function -ErrorAction SilentlyContinue){Close-HybridLocks}
        }
        exit $exitCode
    } finally {$gateLock.Dispose()}
} finally {foreach($guard in $installationGuards){$guard.Dispose()}}
