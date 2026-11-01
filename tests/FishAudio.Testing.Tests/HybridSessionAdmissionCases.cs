using System.Diagnostics;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

// Reuses the existing raw NUnit XML/TRX/request/supervisor fixture and actual
// metadata CLI. Only metadata inputs are synthetic; no validator is replaced.
public sealed partial class FrameworkCiEvidenceTests
{
    [Test]
    [Property("FishAudioCaseId", "infra.hybrid/admit-focused-owner-through-raw-readers")]
    public async Task HybridFocusedOwnerReparsesCurrentMetadataAndRawResults()
    {
        PrepareHybridFramework();
        var result = await RunHybridBoundaryAsync("framework");
        Assert.That(result.ExitCode, Is.Zero, result.Error);
        Assert.That(File.Exists(Path.Combine(_root, "hybrid-verification/plugin-current-plan.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(_root, "hybrid-verification/framework-terminal-plugin-phase-0001.json")), Is.True);
    }

    [TestCase("duplicate", TestName = "Hybrid_owner_union_rejects_duplicate_owner")]
    [TestCase("missing", TestName = "Hybrid_owner_union_rejects_missing_owner")]
    [TestCase("owner", TestName = "Hybrid_owner_union_rejects_wrong_owner_kind")]
    [TestCase("order", TestName = "Hybrid_owner_union_rejects_changed_slot_order")]
    public async Task HybridSlotUnionRejectsInconsistentOwner(string variable)
    {
        var expected = HybridSlots();
        var actual = expected.DeepClone().AsArray();
        if (variable == "duplicate") actual[5]!["selectedIds"] = actual[1]!["selectedIds"]!.DeepClone();
        if (variable == "missing") actual.RemoveAt(1);
        if (variable == "owner") actual[1]!["owner"] = "legacy";
        if (variable == "order") (actual[0], actual[1]) = (actual[1]!.DeepClone(), actual[0]!.DeepClone());
        WriteJson(Path.Combine(_root, "hybrid-input.json"), new JsonObject { ["expected"] = expected, ["actual"] = actual });
        var result = await RunHybridBoundaryAsync("slots");
        Assert.That(result.ExitCode, Is.Not.Zero, "The real union guard must reject this owner set.");
    }

    [TestCase("omitted", TestName = "Hybrid_latest_owner_rejects_omitted_new_attempt")]
    [TestCase("failed", TestName = "Hybrid_latest_owner_rejects_newer_failed_attempt")]
    [TestCase("hidden-omitted", TestName = "Hybrid_Common_rejects_Windows_hidden_unlisted_failed_attempt")]
    public async Task HybridLatestAttemptCannotBorrowOlderPass(string variable)
    {
        var directory = Path.Combine(_root, "hybrid-attempts");
        var records = new JsonArray();
        for (var ordinal = 1; ordinal <= 2; ordinal++)
        {
            var path = Path.Combine(directory, $"attempt-{ordinal:D4}", "attempt.json");
            WriteJson(path, new JsonObject { ["ordinal"] = ordinal, ["complete"] = ordinal == 1, ["exitCode"] = ordinal == 1 ? 0 : 1 });
            if (ordinal == 1 || variable == "failed") records.Add(new JsonObject { ["ordinal"] = ordinal, ["sha256"] = Hash(path) });
        }
        if (variable == "hidden-omitted")
        {
            var original = Path.Combine(directory, "attempt-0001", "attempt.json");
            var originalHash = records[0]!["sha256"]!.GetValue<string>();
            var hidden = Path.Combine(directory, "attempt-0002");
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
            Assert.That(File.GetAttributes(hidden).HasFlag(FileAttributes.Hidden), Is.True,
                "The real later failed attempt directory must have the Windows Hidden attribute before admission.");
            Assert.That(Hash(original), Is.EqualTo(originalHash), "Earlier pinned passing bytes stay unchanged.");
            Assert.That(records.Count, Is.EqualTo(1), "The later physical attempt remains unlisted in the old passing manifest.");
        }
        WriteJson(Path.Combine(_root, "hybrid-input.json"), new JsonObject { ["directory"] = directory, ["records"] = records });
        var result = await RunHybridBoundaryAsync("latest");
        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Error, Does.Contain(variable == "failed" ? "hybrid-latest-attempt-failed-or-incomplete" : "hybrid-attempt-set"));
    }

    [TestCase("missing-request", TestName = "Hybrid_framework_rejects_missing_actual_request")]
    [TestCase("changed-request", TestName = "Hybrid_framework_rejects_changed_request_bytes")]
    [TestCase("wrong-fullname", TestName = "Hybrid_framework_rejects_coherent_wrong_request_fullname")]
    [TestCase("phase-policy", TestName = "Hybrid_framework_rejects_changed_phase_policy")]
    [TestCase("stale-source", TestName = "Hybrid_framework_rejects_stale_source_owner")]
    public async Task HybridFrameworkRejectsWrongCurrentEvidence(string variable)
    {
        PrepareHybridFramework();
        var phase = FirstExecutionPhase;
        var requestPath = Artifact(phase["request"]!.GetValue<string>());
        if (variable == "missing-request") File.Delete(requestPath);
        if (variable == "changed-request") File.AppendAllText(requestPath, "\n ");
        if (variable == "wrong-fullname")
        {
            var request = JsonNode.Parse(File.ReadAllText(requestPath))!;
            request["cases"]![0]!["fullName"] = "Foreign.Fixture.Other";
            WriteJson(requestPath, request);
            phase["requestSha256"] = Hash(requestPath);
        }
        if (variable == "phase-policy")
        {
            phase["maximumConcurrency"] = 1;
            var planPath = Artifact(_manifest["assemblies"]![0]!["executionPlan"]!.GetValue<string>());
            var plan = JsonNode.Parse(File.ReadAllText(planPath))!;
            plan["plan"]!["phases"]![0]!["maximumConcurrency"] = 1;
            WriteJson(planPath, plan);
            _manifest["assemblies"]![0]!["executionPlanSha256"] = Hash(planPath);
        }
        if (variable == "stale-source") _manifest["source"]!["sourceCommit"] = ForeignCommit;
        WriteJson(Path.Combine(_bundle, "manifest.json"), _manifest);
        var result = await RunHybridBoundaryAsync("framework");
        Assert.That(result.ExitCode, Is.Not.Zero);
        var expected = variable switch
        {
            "missing-request" => "hybrid-file-not-regular",
            "changed-request" => "hybrid-artifact-digest",
            "wrong-fullname" => "hybrid-framework-phase-request-binding",
            "phase-policy" => "framework-execution-plan-rejected",
            "stale-source" => "hybrid-source-identity-mismatch",
            _ => throw new ArgumentOutOfRangeException(nameof(variable)),
        };
        Assert.That(result.Error, Does.Contain(expected));
    }

    private void PrepareHybridFramework()
    {
        var freshPath = Path.Combine(_root, "repository/discovery-template/manifest.json");
        var fresh = JsonNode.Parse(File.ReadAllText(freshPath))!;
        var relative = new[]
        {
            "tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests.dll",
            "tests/FishAudio.Testing.Tests/bin/Release/net10.0-windows/FishAudio.Testing.Tests.dll",
        };
        var ids = new JsonArray();
        for (var index = 0; index < 2; index++)
        {
            var assembly = _manifest["assemblies"]![index]!;
            var old = Path.Combine(_root, "repository", assembly["assembly"]!.GetValue<string>());
            var target = Path.Combine(_root, "repository", relative[index]);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(old, target, overwrite: false);
            assembly["assembly"] = relative[index];
            fresh["assemblies"]![index]!["assembly"] = relative[index];
            ids.Add(assembly["selectedIds"]![0]!.DeepClone());
            foreach (var phase in assembly["executionPhases"]!.AsArray())
            {
                var requestPath = Artifact(phase!["request"]!.GetValue<string>());
                var request = JsonNode.Parse(File.ReadAllText(requestPath))!;
                request["testAssembly"] = target;
                WriteJson(requestPath, request);
                phase["requestSha256"] = Hash(requestPath);
                var supervisorPath = Artifact(phase["supervisor"]!.GetValue<string>());
                var supervisor = JsonNode.Parse(File.ReadAllText(supervisorPath))!;
                supervisor["requestSha256"] = Hash(requestPath);
                WriteJson(supervisorPath, supervisor);
                phase["supervisorSha256"] = Hash(supervisorPath);
            }
        }
        // The hybrid producer invokes Full -NoBuild after its shared build.
        // Keep this metadata fixture faithful to that exact phase contract.
        var phases = _manifest["phases"]!.AsArray();
        foreach (var phase in phases.Where(item => item!["name"]!.GetValue<string>()
                     .StartsWith("build-", StringComparison.Ordinal)).ToArray()) phases.Remove(phase);
        var inputPath = Artifact("selection-input.json");
        WriteJson(inputPath, new JsonObject { ["schemaVersion"] = 1, ["ids"] = ids.DeepClone() });
        _manifest["selection"] = "focused";
        _manifest["selectionInput"] = new JsonObject { ["path"] = "selection-input.json", ["sha256"] = Hash(inputPath) };
        WriteJson(Path.Combine(_root, "hybrid-expected-source.json"), fresh["source"]!.DeepClone());
        WriteJson(Path.Combine(_root, "hybrid-expected-ids.json"), new JsonObject { ["ids"] = ids });
        WriteJson(freshPath, fresh);
        WriteJson(Path.Combine(_bundle, "manifest.json"), _manifest);
    }

    private static JsonArray HybridSlots()
    {
        var result = new JsonArray();
        var partitions = new string?[] { "core", "native-settings", "native-input", "native-popups", "native-cards", null };
        for (var index = 0; index < partitions.Length; index++)
            result.Add(new JsonObject
            {
                ["ordinal"] = index + 1, ["id"] = $"slot-{index + 1:D4}",
                ["owner"] = index is 1 or 5 ? "framework" : "legacy", ["partition"] = partitions[index],
                ["selectedIds"] = new JsonArray("fixture/slot-" + index),
            });
        return result;
    }

    private async Task<ProcessResult> RunHybridBoundaryAsync(string mode)
    {
        var script = Path.Combine(_root, "hybrid-boundary.ps1");
        Write(script, HybridBoundary);
        var start = new ProcessStartInfo(FindExecutable("pwsh.exe"))
        {
            WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, "-Mode", mode,
            "-Common", Path.Combine(_repositoryRoot, "tools/HybridSession.Common.ps1"),
            "-Root", Path.Combine(_root, "repository"), "-Owner", _bundle,
            "-Fresh", Path.Combine(_root, "repository/discovery-template"), "-Output", Path.Combine(_root, "hybrid-verification"),
            "-Source", Path.Combine(_root, "hybrid-expected-source.json"), "-Ids", Path.Combine(_root, "hybrid-expected-ids.json"),
            "-InputPath", Path.Combine(_root, "hybrid-input.json"), "-Tool", _actualTool,
        }) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase) ||
                     key.StartsWith("FISHAUDIO_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        return await RunOwnedProcessAsync(start);
    }

    private const string HybridBoundary = """
        param([string]$Mode,[string]$Common,[string]$Root,[string]$Owner,[string]$Fresh,[string]$Output,
            [string]$Source,[string]$Ids,[string]$InputPath,[string]$Tool)
        . $Common
        $root = $Root
        try {
            if ($Mode -ceq 'framework') {
                $expectedSource = Read-HybridJson $Source
                $expectedIds = [string[]](Read-HybridJson $Ids).ids
                Assert-HybridFrameworkOwner $Owner $Fresh $expectedSource $expectedIds $Output $Tool $Root
            } elseif ($Mode -ceq 'slots') {
                $inputValue = Read-HybridJson $InputPath
                Assert-HybridSlotIndex $inputValue.actual $inputValue.expected
            } elseif ($Mode -ceq 'latest') {
                $inputValue = Read-HybridJson $InputPath
                [void](Read-HybridLatestAttempt $inputValue.directory $inputValue.records)
            } elseif ($Mode -cin @('build-pins','changed-build-pin')) {
                $pins = @(Get-HybridBuildPins $Root)
                New-HybridDirectory $Output $Root
                Write-HybridJson (Join-Path $Output 'build-pins.json') @{ pins = $pins }
                if ($Mode -ceq 'changed-build-pin') {
                    Close-HybridLocks
                    [IO.File]::AppendAllText((Join-Path $Root $pins[2].path),'changed-after-first-pin')
                    Assert-HybridBuildPins (Get-HybridBuildPins $Root) $pins
                }
            } else { throw 'hybrid-fixture-mode' }
        } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
        finally { Close-HybridLocks }
        exit 0
        """;
}
