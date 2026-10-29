using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

// Uses captured read-only outer metadata and the existing process fixture owner
// for the one real legacy-list subprocess, drains, assertions and cleanup. Discovery itself
// constructs no fixture and never executes these methods.
public sealed partial class FrameworkCiEvidenceTests
{
    [TestCaseSource(nameof(MetadataContextFaults))]
    public void CapturedMetadataRejectsMissingTamperedStaleAndWrongAssemblyInputs(string fault, string code)
    {
        RunnerMetadataInputs.Rejected? rejected;
        if (fault == "missing")
            rejected = Assert.Throws<RunnerMetadataInputs.Rejected>(() =>
                RunnerMetadataInputs.ReadActual(_repositoryRoot, _ => null));
        else if (fault == "tamper")
        {
            var original = Environment.GetEnvironmentVariable(RunnerMetadataInputs.ContextVariable)!;
            var changed = JsonNode.Parse(original)!;
            changed["kind"] = "runner-readonly-metadata-tampered";
            var altered = changed.ToJsonString();
            Assert.That(altered, Is.Not.EqualTo(original));
            rejected = Assert.Throws<RunnerMetadataInputs.Rejected>(() =>
                RunnerMetadataInputs.ReadActual(_repositoryRoot, name => name == RunnerMetadataInputs.ContextVariable
                    ? altered : Environment.GetEnvironmentVariable(name)));
        }
        else
        {
            // This is an actual authenticated outer request/control binding.
            // Only its untrusted input value is changed for these rejection controls.
            var binding = RunnerMetadataInputs.ReadBinding(_repositoryRoot);
            var changed = fault == "stale-source"
                ? binding.Context with { SourceFingerprint = new string('0', 64) }
                : binding.Context with { Assemblies = new[]
                {
                    binding.Context.Assemblies[0] with
                    {
                        Assembly = binding.Context.Assemblies[1].Assembly,
                        AssemblySha256 = binding.Context.Assemblies[1].AssemblySha256,
                        Pdb = binding.Context.Assemblies[1].Pdb,
                        PdbSha256 = binding.Context.Assemblies[1].PdbSha256,
                    },
                    binding.Context.Assemblies[1],
                } };
            rejected = Assert.Throws<RunnerMetadataInputs.Rejected>(() =>
                RunnerMetadataInputs.ValidateInputs(changed, binding.Request, _repositoryRoot));
        }
        Assert.That(rejected!.Message, Is.EqualTo("runner-metadata-" + code));
    }

    private static IEnumerable<TestCaseData> MetadataContextFaults()
    {
        foreach (var row in new[]
        {
            ("missing", "missing-context"), ("tamper", "request-context-binding"),
            ("stale-source", "source-binding"), ("wrong-assembly", "assembly-binding"),
        })
            yield return new TestCaseData(row.Item1, row.Item2).SetName("RunnerMetadataContext_" + row.Item1)
                .SetProperty("FishAudioCaseId", "infra.runner/catalog/metadata-context-rejects-" + row.Item1);
    }

    private void RetainSdkDependencyEvidence(RunnerMetadataInputs.SdkDependencyEvidence dependency)
    {
        var binding = RunnerMetadataInputs.ReadBinding(_repositoryRoot);
        var runtime = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        var expected = Path.GetFullPath(Path.Combine(binding.Request.OutputDirectory, "../runtime-evidence"));
        Assert.That(runtime, Is.Not.Null.And.Not.Empty);
        Assert.That(Path.IsPathFullyQualified(runtime!), Is.True);
        Assert.That(Path.GetFullPath(runtime!), Is.EqualTo(expected).IgnoreCase);
        Assert.That(binding.Request.Environment!.TryGetValue("FISHAUDIO_COMPAT_EVIDENCE_ROOT", out var pinned) &&
            string.Equals(pinned, runtime, StringComparison.Ordinal), Is.True);
        Assert.That(Directory.Exists(runtime), Is.True);
        RequireNoReparseAncestors(runtime!);
        var text = JsonSerializer.Serialize(dependency, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        });
        WriteNewEvidence("runner-metadata-sdk-dependency.json", text);
        using var output = new FileStream(Path.Combine(runtime!, "runner-metadata-sdk-dependency.json"),
            FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        output.Write(bytes);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.runner/catalog/current-native-settings-ownership")]
    [Property("FishAudioFreshHost", "true")]
    [Property("FishAudioExclusiveResource", "framework-discovery")]
    public async Task CurrentRawMetadataRetainsEveryNativeSettingsOwnerAndColdAnchor()
    {
        var dependencies = new List<RunnerMetadataInputs.SdkDependencyEvidence>();
        var captured = RunnerMetadataInputs.ReadActual(_repositoryRoot, verifiedDependency: dependencies.Add);
        Assert.That(dependencies, Has.Count.EqualTo(1));
        RetainSdkDependencyEvidence(dependencies[0]);
        var paths = captured.Assemblies.Select(item => item.Discovery).ToList();
        var plugin = captured.Assemblies[0].Assembly;
        var list = new ProcessStartInfo(FindExecutable("dotnet.exe"))
        {
            WorkingDirectory = _repositoryRoot, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var value in new[] { plugin, "--legacy", "--list" }) list.ArgumentList.Add(value);
        var listed = await RunOwnedProcessAsync(list);
        WriteNewEvidence("current-legacy.stdout.log", listed.Output);
        WriteNewEvidence("current-legacy.stderr.log", listed.Error);
        Assert.That(listed.ExitCode, Is.Zero, "The current real runner must provide its metadata-only legacy catalog.");
        Assert.That(listed.Error, Is.Empty);
        var lines = listed.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines[^1].StartsWith("Selected ", StringComparison.Ordinal), Is.True);
        var legacy = lines[..^1].Select(line =>
        {
            var fields = line.Split('\t');
            Assert.That(fields, Has.Length.EqualTo(2));
            return new MigrationLegacyCase(fields[0], fields[1]);
        }).ToArray();
        Assert.That(lines[^1], Is.EqualTo($"Selected {legacy.Length} case(s)."));
        Assert.That(Hash(plugin), Is.EqualTo(captured.Assemblies[0].AssemblySha256));

        var reference = PinnedLegacyReference.ReadValidated(Path.Combine(_repositoryRoot, "tests/Migration/legacy-catalog.json"));
        var mapPath = Path.Combine(_repositoryRoot, "tests/Migration/migrated-cases.json");
        using var map = JsonDocument.Parse(File.ReadAllBytes(mapPath));
        Assert.That(map.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var migrations = map.RootElement.GetProperty("migrations").Deserialize<MigrationRecord[]>(json)!;
        var current = MigrationConservation.ReadValidated(reference, legacy, paths, migrations);
        var slots = HybridExecutionSlotPlan.ReadValidated(reference, legacy, paths, migrations);
        var originalNative = reference.Cases.Where(item => item.Partition == "native-settings").ToArray();
        Assert.That(originalNative, Has.Length.EqualTo(94), "The immutable reference owns all original native-settings responsibilities.");
        Assert.That(legacy.Any(item => item.Partition == "native-settings"), Is.False,
            "A retired legacy partition must not overlap its complete framework owner.");
        var checkedNative = current.CheckedOwners.Where(item => item.Partition == "native-settings").ToArray();
        Assert.That(checkedNative.Select(item => item.OriginalId), Is.EqualTo(originalNative.Select(item => item.Id)));
        Assert.That(checkedNative.All(item => item.Owner == MigrationOwnerKind.NUnit), Is.True);
        var expected = originalNative.SelectMany(original => migrations.Single(item => item.OriginalId == original.Id).SuccessorIds).ToArray();
        Assert.That(expected, Has.Length.EqualTo(173));
        Assert.That(expected, Is.Unique, "Each original's complete successors have independent framework identities.");

        var metadata = paths.Select(path => (Path: path, Members: NUnitExecutionMetadata.ReadValidated(path))).ToArray();
        var native = metadata.SelectMany(owner => owner.Members.Where(item => item.Profile == "native-settings")
            .Select(item => (owner.Path, Policy: item))).OrderBy(item => item.Policy.ProfileOrdinal).ToArray();
        Assert.That(native.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase), Has.Exactly(1).Items);
        Assert.That(native[0].Path, Is.EqualTo(paths[0]), "Native settings belongs to the actual plugin assembly.");
        Assert.That(native.Select(item => item.Policy.Case.CanonicalId), Is.EqualTo(expected),
            "Compare the complete raw profile against every original owner's mapped successors, in original order.");
        Assert.That(native.Select(item => item.Policy.ProfileOrdinal), Is.EqualTo(Enumerable.Range(0, 173).Select(value => (int?)value)));
        Assert.That(native.All(item => item.Policy.ExclusiveResources.Contains("desktop", StringComparer.Ordinal)), Is.True);
        var profile = NUnitExecutionPlan.ReadValidated(paths[0], [expected[0]], maximumConcurrency: 1);
        Assert.That(profile.ExpandedIds, Is.EquivalentTo(expected), "Expansion membership retains all native leaves; phase cases below own execution order.");
        Assert.That(profile.Phases, Has.Count.EqualTo(1));
        var phasePlan = profile.Phases[0];
        Assert.That(phasePlan.Kind, Is.EqualTo("ordered-profile"));
        Assert.That(phasePlan.Profile, Is.EqualTo("native-settings"));
        Assert.That(phasePlan.RequireOrder, Is.True);
        Assert.That(phasePlan.MaximumConcurrency, Is.EqualTo(1));
        Assert.That(phasePlan.Cases.Select(item => item.CanonicalId), Is.EqualTo(expected));
        Assert.That(slots.Slots.Take(5).Select(item => item.Partition),
            Is.EqualTo(new[] { "core", "native-settings", "native-input", "native-popups", "native-cards" }));
        Assert.That(slots.Slots.Take(5).Select(item => item.Owner),
            Is.EqualTo(new[] { "legacy", "framework", "legacy", "legacy", "legacy" }));
        Assert.That(slots.Slots[1].SelectedIds, Is.EqualTo(expected));
        foreach (var slot in slots.Slots.Where(item => item.Owner == "legacy"))
            Assert.That(slot.SelectedIds, Is.EqualTo(legacy.Where(item => item.Partition == slot.Partition).Select(item => item.Id)),
                "Every remaining legacy slot retains complete current catalog order.");
        var framework = slots.Slots.Where(item => item.Owner == "framework").SelectMany(item => item.SelectedIds).ToArray();
        Assert.That(framework, Is.Unique);
        Assert.That(framework, Is.EquivalentTo(metadata.SelectMany(item => item.Members).Select(item => item.Case.CanonicalId)),
            "Native slot and ordinary tail must cover the entire pair of actual raw catalogs exactly once.");
        Assert.That(expected.Take(4), Is.EqualTo(new[]
        {
            "lifecycle.activity-dispatcher/playback-timeout-retires-bound-commands-on-ui-thread",
            "lifecycle.activity-dispatcher/playback-cancellation-retires-bound-commands-on-ui-thread",
            "lifecycle.activity-dispatcher/queued-retirement-cannot-unlock-new-ui-operation",
            "settings.binding/api-key-password-input-persists-before-focus-leaves",
        }), "Cold listeners and password construction/binding keep their original first-use prefix.");
        var previous = -1;
        foreach (var anchor in new (string Prefix, int Owners, int Leaves)[]
        {
            ("cards.ink/", 25, 25), ("numeric.glyph/", 1, 1),
            ("numeric.geometry/compact/", 48, 48), ("numeric.bottom/", 12, 12),
            ("numeric.lifecycle/", 1, 1), ("input.clear/run", 1, 34),
        })
        {
            var originals = originalNative.Where(item => item.Id.StartsWith(anchor.Prefix, StringComparison.Ordinal)).ToArray();
            Assert.That(originals, Has.Length.EqualTo(anchor.Owners));
            var successors = originals.SelectMany(original => migrations.Single(item => item.OriginalId == original.Id).SuccessorIds).ToArray();
            Assert.That(successors, Has.Length.EqualTo(anchor.Leaves));
            var indices = successors.Select(id => Array.IndexOf(expected, id)).ToArray();
            Assert.That(indices, Is.EqualTo(Enumerable.Range(previous < 0 ? 52 : previous + 1, successors.Length)),
                "Ink, glyph, compact, bottom, continuous lifecycle and InputClear retain every ordered cold anchor.");
            previous = indices[^1];
        }
        Assert.That(previous, Is.EqualTo(expected.Length - 1));
        _ = RemainingRoot();
    }
}
