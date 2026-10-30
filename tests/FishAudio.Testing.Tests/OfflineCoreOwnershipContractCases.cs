using System.Diagnostics;
using System.Text.Json;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class FrameworkCiEvidenceTests
{
    // Retains the existing canonical runner-contract responsibility. Actual
    // source/build-bound raw catalogs replace assumptions about legacy owners.
    [Test]
    [Property("FishAudioCaseId", "infra.runner/catalog/offline-dispatcher-and-input-requests-remain-core")]
    [Property("FishAudioFreshHost", "true")]
    [Property("FishAudioExclusiveResource", "framework-discovery")]
    public async Task OfflineDispatcherAndInputRequestsHaveCompleteCurrentFrameworkOwners()
    {
        var captured = RunnerMetadataInputs.ReadActual(_repositoryRoot);
        var paths = captured.Assemblies.Select(item => item.Discovery).ToArray();
        var plugin = captured.Assemblies[0].Assembly;
        var list = new ProcessStartInfo(FindExecutable("dotnet.exe"))
        {
            WorkingDirectory = _repositoryRoot, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { plugin, "--legacy", "--list" }) list.ArgumentList.Add(argument);
        var listed = await RunOwnedProcessAsync(list);
        WriteNewEvidence("offline-current-legacy.stdout.log", listed.Output);
        WriteNewEvidence("offline-current-legacy.stderr.log", listed.Error);
        Assert.That(listed.ExitCode, Is.Zero);
        Assert.That(listed.Error, Is.Empty);
        var lines = listed.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Is.Not.Empty);
        var legacy = lines[..^1].Select(line =>
        {
            var fields = line.Split('\t');
            Assert.That(fields, Has.Length.EqualTo(2));
            return new MigrationLegacyCase(fields[0], fields[1]);
        }).ToArray();
        Assert.That(lines[^1], Is.EqualTo($"Selected {legacy.Length} case(s)."));
        Assert.That(Hash(plugin), Is.EqualTo(captured.Assemblies[0].AssemblySha256));

        var reference = PinnedLegacyReference.ReadValidated(Path.Combine(_repositoryRoot, "tests/Migration/legacy-catalog.json"));
        using var map = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(_repositoryRoot, "tests/Migration/migrated-cases.json")));
        Assert.That(map.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var migrations = map.RootElement.GetProperty("migrations").Deserialize<MigrationRecord[]>(json)!;
        var admitted = MigrationConservation.ReadValidated(reference, legacy, paths, migrations);
        var currentCore = admitted.CheckedOwners.Where(item => item.Partition == "core").ToArray();
        Assert.That(currentCore, Has.Length.EqualTo(309), "The pinned original reference retains all offline responsibilities.");
        const string childTarget = "streaming.text/first-window-is-lazy-and-cuts-ordinary-long-sentences";
        Assert.That(legacy.Any(item => item.Partition == "core"), Is.False,
            "All original core responsibilities have framework owners; the separate compatibility child is not default catalog work.");
        var migrated = currentCore;
        Assert.That(migrated, Has.Length.EqualTo(309));
        Assert.That(migrated.All(item => item.Owner == MigrationOwnerKind.NUnit), Is.True);
        const string childSuccessor = "streaming.text/lazy-unpunctuated-first-window-does-not-read-tail";
        Assert.That(currentCore.Single(item => item.OriginalId == childTarget).SuccessorIds,
            Is.EqualTo(new[] { childSuccessor }));
        var policies = paths.SelectMany(NUnitExecutionMetadata.ReadValidated).ToArray();
        foreach (var owner in migrated)
        foreach (var id in owner.SuccessorIds)
        {
            var policy = policies.Single(item => item.Case.CanonicalId == id);
            Assert.That(policy.Profile, Is.Null,
                "An offline successor cannot acquire a desktop native-profile slot: " + id);
        }
        foreach (var (id, leaves) in new[]
        {
            ("lifecycle.activity-dispatcher/deadline-diagnostics-preserve-original-failure-without-retry", 1),
            ("input.clear/run-requests", 7), ("voice.search-adjustment/run-requests", 13),
        })
        {
            Assert.That(legacy.Any(item => item.Id == id), Is.False);
            var owner = currentCore.Single(item => item.OriginalId == id);
            Assert.That(owner.Owner, Is.EqualTo(MigrationOwnerKind.NUnit));
            Assert.That(owner.SuccessorIds, Has.Count.EqualTo(leaves),
                "The complete original request/diagnostic responsibility moves as one verified owner transfer.");
        }
        Assert.That(policies.Single(item => item.Case.CanonicalId == childSuccessor).FreshHost, Is.True,
            "The standard lazy-text owner retains its original cold first-use process boundary.");
        var slots = HybridExecutionSlotPlan.ReadValidated(reference, legacy, paths, migrations);
        Assert.That(slots.Slots[0].Partition, Is.EqualTo("core"));
        Assert.That(slots.Slots[0].Owner, Is.EqualTo("framework"));
        var coreSuccessors = currentCore.SelectMany(item => item.SuccessorIds).ToHashSet(StringComparer.Ordinal);
        Assert.That(coreSuccessors, Is.Not.Empty);
        Assert.That(slots.Slots[0].SelectedIds,
            Is.EqualTo(policies.Where(item => coreSuccessors.Contains(item.Case.CanonicalId)).Select(item => item.Case.CanonicalId)),
            "The core slot owns every mapped current raw identity across both assemblies in discovery order.");
        Assert.That(slots.Slots.Skip(1).SelectMany(item => item.SelectedIds).Intersect(coreSuccessors), Is.Empty,
            "A core successor must not execute again in a native slot or the ordinary tail.");
        Assert.That(slots.Slots.All(item => item.SelectedIds.Count > 0), Is.True);
        var framework = slots.Slots.Where(item => item.Owner == "framework").SelectMany(item => item.SelectedIds).ToArray();
        Assert.That(framework, Is.Unique);
        Assert.That(framework, Is.EquivalentTo(policies.Select(item => item.Case.CanonicalId)),
            "Native slots and the ordinary tail cover both actual framework catalogs once.");
        _ = RemainingRoot();
    }
}
