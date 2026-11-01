using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace NUnitCompatibility;

// Pure request-binding controls. These never invoke the returned delegate,
// capture a live source, start a child, create files or construct product readers.
[TestFixture]
[NonParallelizable]
public sealed class ChildTargetBindingCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.child-binding/exact-historical-target-is-deferred-metadata")]
    public void ExactHistoricalRequestCreatesOneDeferredMetadataDescriptor()
    {
        var request = Request();
        RegressionProcessProtocol.ValidateRequest(request);
        var original = JsonSerializer.Serialize(request, RegressionProcessProtocol.Options);
        var actions = new List<RegressionActionEvent>();
        using var scope = RegressionActionScope.BeginCase("runner.probe/child-binding-metadata", actions.Add);
        var selected = StreamingTextChildCompatibilityTarget.BindExact(request);
        Assert.That(selected, Is.Not.Null);
        Assert.That(selected!.Id, Is.EqualTo("streaming.text/first-window-is-lazy-and-cuts-ordinary-long-sentences"));
        Assert.That(selected.Partition, Is.EqualTo("core"));
        Assert.That(selected.Run, Is.Not.Null);
        Assert.That(JsonSerializer.Serialize(request, RegressionProcessProtocol.Options) == original, Is.True,
            "Binding must preserve the original request bytes without publishing private paths.");
        Assert.That(actions, Is.Empty, "Binding metadata must not queue or run a regression action.");
    }

    public static IEnumerable<TestCaseData> RejectionRows()
    {
        yield return Row("mixed", true);
        yield return Row("extra-duplicate", false);
        yield return Row("wrong-partition", true);
        yield return Row("unknown", true);
        yield return Row("legacy-arguments", false);
        yield return Row("probe", true);
        yield return Row("empty", false);
    }

    [TestCaseSource(nameof(RejectionRows))]
    public void InexactOrAlternateOperationCannotBindTheHistoricalChildTarget(string mutation, bool protocolShapeValid)
    {
        var request = Request();
        switch (mutation)
        {
            case "mixed": request.CaseIds = [request.CaseIds[0], "runner.probe/another-case"]; break;
            case "extra-duplicate": request.CaseIds = [request.CaseIds[0], request.CaseIds[0]]; break;
            case "wrong-partition": request.Partition = "native-input"; break;
            case "unknown": request.CaseIds = [request.CaseIds[0] + "/unknown"]; break;
            case "legacy-arguments": request.LegacyArguments = []; break;
            case "probe": request.Probe = "pipes"; break;
            case "empty": request.CaseIds = []; break;
            default: throw new InvalidOperationException("child-binding-control-invalid");
        }
        // Valid generic protocol shapes still cannot select this specific operation.
        // Invalid generic shapes already reject before the child resolver is reached.
        if (protocolShapeValid) RegressionProcessProtocol.ValidateRequest(request);
        else Assert.Throws<InvalidDataException>(() => RegressionProcessProtocol.ValidateRequest(request));
        var original = JsonSerializer.Serialize(request, RegressionProcessProtocol.Options);
        var actions = new List<RegressionActionEvent>();
        using var scope = RegressionActionScope.BeginCase("runner.probe/child-binding-rejection", actions.Add);
        Assert.That(StreamingTextChildCompatibilityTarget.BindExact(request), Is.Null);
        Assert.That(JsonSerializer.Serialize(request, RegressionProcessProtocol.Options) == original, Is.True,
            "Binding must preserve the original request bytes without publishing private paths.");
        Assert.That(actions, Is.Empty);
    }

    private static TestCaseData Row(string mutation, bool protocolShapeValid) =>
        new TestCaseData(mutation, protocolShapeValid)
            .SetName("ChildTargetBinding_reject-" + mutation)
            .SetProperty("FishAudioCaseId", "infra.child-binding/reject-" + mutation);

    private static RegressionChildRequest Request()
    {
        var commit = new string('a', 40);
        var tree = new string('b', 40);
        return new RegressionChildRequest
        {
            RequestId = "11111111111111111111111111111111",
            RunId = "22222222222222222222222222222222",
            Partition = "core",
            CaseIds = [StreamingTextChildCompatibilityTarget.HistoricalScenarioId],
            Source = new RegressionSourceIdentity
            {
                Available = true, Snapshot = true, Commit = commit, Tree = tree,
                Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                    "snapshot-v1\n" + commit + "\n" + tree + "\n"))),
            },
            Configuration = RegressionReport.BuildMetadata("RegressionBuildConfiguration"),
            Sdk = RegressionReport.BuildMetadata("RegressionBuildSdk"),
            ManifestPath = Path.Combine(Path.GetTempPath(), "FishAudioChildBindingMetadata", "request.child.json"),
        };
    }
}
