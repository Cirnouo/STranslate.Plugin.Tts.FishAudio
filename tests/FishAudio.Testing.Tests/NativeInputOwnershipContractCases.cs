using System.Diagnostics;
using System.Text.Json;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

// The body consumes the authenticated outer raw discovery and the actual current
// metadata-only legacy catalog through the existing owned process boundary.
// Framework discovery only inspects attributes; it executes no reader or process.
public sealed partial class FrameworkCiEvidenceTests
{
    [Test]
    [Property("FishAudioCaseId", "infra.runner/catalog/current-native-input-ownership")]
    [Property("FishAudioFreshHost", "true")]
    [Property("FishAudioExclusiveResource", "framework-discovery")]
    public async Task CurrentRawMetadataRetainsEveryNativeInputOwnerAndColdAnchor()
    {
        var dependencies = new List<RunnerMetadataInputs.SdkDependencyEvidence>();
        var captured = RunnerMetadataInputs.ReadActual(_repositoryRoot, verifiedDependency: dependencies.Add);
        Assert.That(dependencies, Has.Count.EqualTo(1));
        var paths = captured.Assemblies.Select(item => item.Discovery).ToList();
        var plugin = captured.Assemblies[0].Assembly;
        var list = new ProcessStartInfo(FindExecutable("dotnet.exe"))
        {
            WorkingDirectory = _repositoryRoot, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var value in new[] { plugin, "--legacy", "--list" }) list.ArgumentList.Add(value);
        var listed = await RunOwnedProcessAsync(list);
        WriteNewEvidence("native-input-current-legacy.stdout.log", listed.Output);
        WriteNewEvidence("native-input-current-legacy.stderr.log", listed.Error);
        Assert.That(listed.ExitCode, Is.Zero, "The current real runner must provide its metadata-only legacy catalog.");
        Assert.That(listed.Error, Is.Empty);
        var lines = listed.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Is.Not.Empty);
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
        var originalNative = reference.Cases.Where(item => item.Partition == "native-input").ToArray();
        Assert.That(originalNative, Has.Length.EqualTo(175), "The immutable reference owns all original native-input responsibilities.");
        Assert.That(legacy.Any(item => item.Partition == "native-input"), Is.False,
            "A retired legacy partition must not overlap its complete framework owner.");
        var checkedNative = current.CheckedOwners.Where(item => item.Partition == "native-input").ToArray();
        Assert.That(checkedNative.Select(item => item.OriginalId), Is.EqualTo(originalNative.Select(item => item.Id)));
        Assert.That(checkedNative.All(item => item.Owner == MigrationOwnerKind.NUnit), Is.True);
        var expected = originalNative.SelectMany(original => migrations.Single(item => item.OriginalId == original.Id).SuccessorIds).ToArray();
        Assert.That(expected, Has.Length.EqualTo(185));
        Assert.That(expected, Is.Unique, "Each original's complete successors have independent framework identities.");

        var metadata = paths.Select(path => (Path: path, Members: NUnitExecutionMetadata.ReadValidated(path))).ToArray();
        var native = metadata.SelectMany(owner => owner.Members.Where(item => item.Profile == "native-input")
            .Select(item => (owner.Path, Policy: item))).OrderBy(item => item.Policy.ProfileOrdinal).ToArray();
        Assert.That(native.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase), Has.Exactly(1).Items);
        Assert.That(native[0].Path, Is.EqualTo(paths[0]), "Native input belongs to the actual plugin assembly.");
        Assert.That(native.Select(item => item.Policy.Case.CanonicalId), Is.EqualTo(expected),
            "Compare the complete raw profile against every original owner's mapped successors, in original order.");
        Assert.That(native.Select(item => item.Policy.ProfileOrdinal), Is.EqualTo(Enumerable.Range(0, 185).Select(value => (int?)value)));
        Assert.That(native.All(item => item.Policy.ExclusiveResources.Contains("desktop", StringComparer.Ordinal)), Is.True);
        var profile = NUnitExecutionPlan.ReadValidated(paths[0], [expected[0]], maximumConcurrency: 1);
        Assert.That(profile.ExpandedIds, Is.EquivalentTo(expected), "Expansion membership retains all native leaves; phase cases below own execution order.");
        Assert.That(profile.Phases, Has.Count.EqualTo(1));
        var phasePlan = profile.Phases[0];
        Assert.That(phasePlan.Kind, Is.EqualTo("ordered-profile"));
        Assert.That(phasePlan.Profile, Is.EqualTo("native-input"));
        Assert.That(phasePlan.RequireOrder, Is.True);
        Assert.That(phasePlan.MaximumConcurrency, Is.EqualTo(1));
        Assert.That(phasePlan.Cases.Select(item => item.CanonicalId), Is.EqualTo(expected));
        Assert.That(slots.Slots.Take(5).Select(item => item.Partition),
            Is.EqualTo(new[] { "core", "native-settings", "native-input", "native-popups", "native-cards" }));
        Assert.That(slots.Slots.Take(5).Select(item => item.Owner),
            Is.EqualTo(new[] { "framework", "framework", "framework", "legacy", "legacy" }));
        Assert.That(slots.Slots[2].SelectedIds, Is.EqualTo(expected));
        foreach (var slot in slots.Slots.Where(item => item.Owner == "legacy"))
            Assert.That(slot.SelectedIds, Is.EqualTo(legacy.Where(item => item.Partition == slot.Partition).Select(item => item.Id)),
                "Every remaining legacy slot retains complete current catalog order.");
        var framework = slots.Slots.Where(item => item.Owner == "framework").SelectMany(item => item.SelectedIds).ToArray();
        Assert.That(framework, Is.Unique);
        Assert.That(framework, Is.EquivalentTo(metadata.SelectMany(item => item.Members).Select(item => item.Case.CanonicalId)),
            "Native slot and ordinary tail must cover the entire pair of actual raw catalogs exactly once.");
        Assert.That(expected.Take(4), Is.EqualTo(new[]
        {
            "input.settings/native/track-point/speed", "input.settings/native/track-point/volume",
            "input.settings/native/track-point/temperature", "input.settings/native/track-point/topp",
        }), "The original four track-point actions retain native-input first use.");
        var previous = -1;
        foreach (var anchor in new (string Prefix, int Owners, int Leaves, int First)[]
        {
            ("input.settings/native/", 142, 142, 0),
            ("repository.link/run-all", 1, 7, 142),
            ("input.page-entry/run-all", 1, 5, 149),
            ("input.cursor/", 31, 31, 154),
        })
        {
            var originals = originalNative.Where(item => item.Id.StartsWith(anchor.Prefix, StringComparison.Ordinal)).ToArray();
            Assert.That(originals, Has.Length.EqualTo(anchor.Owners));
            var successors = originals.SelectMany(original => migrations.Single(item => item.OriginalId == original.Id).SuccessorIds).ToArray();
            Assert.That(successors, Has.Length.EqualTo(anchor.Leaves));
            Assert.That(anchor.First, Is.EqualTo(previous + 1));
            var indices = successors.Select(id => Array.IndexOf(expected, id)).ToArray();
            Assert.That(indices, Is.EqualTo(Enumerable.Range(anchor.First, anchor.Leaves)),
                "All settings actions, seven repository locales, five PageEntry workflows and every cursor owner retain original order.");
            previous = indices[^1];
        }
        Assert.That(previous, Is.EqualTo(expected.Length - 1));
        Assert.That(expected.Skip(142).Take(7), Is.EqualTo(new[]
        {
            "repository.link/compiled/en", "repository.link/compiled/zh-cn", "repository.link/compiled/zh-tw",
            "repository.link/compiled/ja", "repository.link/compiled/ko", "repository.link/compiled/ru", "repository.link/compiled/tr",
        }));
        Assert.That(expected.Skip(149).Take(5), Is.EqualTo(new[]
        {
            "input.page-entry/fields/search", "input.page-entry/fields/favorites",
            "input.page-entry/search-maximum-and-commit-transitions", "input.page-entry/favorite-commits-preserve-pending-owner",
            "input.page-entry/extreme-maximum-overflow-safe",
        }));
        Assert.That(expected[154], Is.EqualTo("input.cursor/construction"));
        Assert.That(expected[^2..], Is.EqualTo(new[] { "input.cursor/native/light/lifecycle", "input.cursor/native/dark/lifecycle" }));
        _ = RemainingRoot();
    }
}
