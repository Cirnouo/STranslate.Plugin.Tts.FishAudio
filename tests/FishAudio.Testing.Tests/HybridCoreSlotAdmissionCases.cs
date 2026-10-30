using System.Text.Json.Nodes;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

// Actual shared PowerShell slot consumer, under the existing process owner.
// The synthetic slot JSON is not raw product execution or source authentication.
public sealed partial class FrameworkCiEvidenceTests
{
    public static IEnumerable<TestCaseData> CoreSlotAdmissionRows()
    {
        foreach (var row in new[]
        {
            ("six-slots", true), ("five-slots", true), ("wrong-owner", false),
            ("empty", false), ("missing", false), ("selected-drift", false), ("overlap", false),
        })
            yield return new TestCaseData(row.Item1, row.Item2).SetName("Hybrid_core_framework_slot_" + row.Item1)
                .SetProperty("FishAudioCaseId", "infra.hybrid-slots/core-consumer-" + row.Item1);
    }

    [TestCaseSource(nameof(CoreSlotAdmissionRows))]
    public async Task SharedSlotConsumerRequiresTheExactNonemptyCurrentCoreFrameworkOwner(string fault, bool accepts)
    {
        var expected = HybridSlots();
        expected[0]!["owner"] = "framework";
        if (fault == "five-slots") expected.RemoveAt(5);
        var actual = expected.DeepClone().AsArray();
        if (fault == "wrong-owner") actual[0]!["owner"] = "legacy";
        else if (fault == "empty") actual[0]!["selectedIds"] = new JsonArray();
        else if (fault == "missing") actual.RemoveAt(0);
        else if (fault == "selected-drift") actual[0]!["selectedIds"] = new JsonArray("fixture/wrong-core-owner");
        else if (fault == "overlap")
        {
            actual[0]!["selectedIds"] = actual[1]!["selectedIds"]!.DeepClone();
            // Even matching per-slot fixture expectations cannot admit one ID twice.
            // Real expectations are independently derived by the conservation planner.
            expected[0]!["selectedIds"] = actual[0]!["selectedIds"]!.DeepClone();
        }
        WriteJson(Path.Combine(_root, "hybrid-input.json"), new JsonObject { ["expected"] = expected, ["actual"] = actual });

        var result = await RunHybridBoundaryAsync("slots");

        Assert.That(result.ExitCode, Is.EqualTo(accepts ? 0 : 1), result.Error);
        if (!accepts)
            Assert.That(result.Error, Does.Contain(fault switch
            {
                "wrong-owner" => "hybrid-slot-owner-or-order", "empty" => "hybrid-case-set-shape",
                "missing" => "hybrid-slot-count", "overlap" => "hybrid-owner-overlap",
                _ => "hybrid-case-set-mismatch",
            }));
    }
}
