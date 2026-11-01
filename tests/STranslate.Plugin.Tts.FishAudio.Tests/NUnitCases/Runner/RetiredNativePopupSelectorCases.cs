using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using NUnit.Framework;
using static TestAssertions;

namespace NUnitCases.Runner;

[TestFixture]
[NonParallelizable]
public sealed class RetiredNativePopupSelectorCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.runner/retired-native-popups/all-original-owner-identities")]
    public void EveryOriginalPopupOwnerRejectsWithoutExecutingOrWarmingSta()
    {
        var dispatcher = SharedDispatcher();
        var wasCreated = dispatcher.IsValueCreated;
        var events = new List<RegressionActionEvent>();
        using var owner = RegressionActionScope.BeginCase("runner.probe/retired-popup-metadata", events.Add);
        var current = RegressionCatalog.Create();
        Assert.That(current, Is.Empty);
        using var reference = JsonDocument.Parse(File.ReadAllBytes(FindRepoFile("tests/Migration/legacy-catalog.json")));
        var originals = reference.RootElement.GetProperty("cases").EnumerateArray()
            .Where(item => item.GetProperty("partition").GetString() == "native-popups")
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        Assert.That(originals, Has.Length.EqualTo(147));
        foreach (var id in originals.Concat(new[] { "popup.language-tags/run-all", "popup.search-adjustment/run-all" }))
        {
            foreach (var selector in new[] { "--case", "--group" })
                Assert.That(Assert.Throws<RegressionSelectionException>(() =>
                    RegressionSelection.Parse([selector, id]).ExplicitIds(current))!.Message,
                    Is.EqualTo("retired-case"));
            var explicitDescriptor = new RegressionCase(id, "native-popups",
                () => throw new InvalidOperationException("historical-popup-descriptor-must-not-run"));
            Assert.That(RegressionCaseAliases.Resolve(id, [explicitDescriptor]), Is.EqualTo(new[] { id }),
                "An explicit historical exact descriptor still wins without invoking its callback.");
        }
        Assert.That(events, Is.Empty);
        Assert.That(dispatcher.IsValueCreated, Is.EqualTo(wasCreated));
    }

    [Test]
    [Property("FishAudioCaseId", "infra.runner/retired-native-popups/direct-selectors-reject-before-source-or-child")]
    public async Task PopupDirectSelectorsRejectBeforeSourceCaptureOrChildLaunch()
    {
        var source = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        var stderr = Console.Error;
        using var errors = new StringWriter();
        var launches = 0;
        using var process = new RegressionProcessHost(_ =>
        {
            launches++;
            throw new InvalidOperationException("retired-popup-selector-launched-child");
        });
        var shapes = new string[][]
        {
            ["--native-key-state-tests"], ["--popup-direction-tests"], ["--popup-heading-tests"],
            ["--popup-proof-tests", "private-proof-directory"], ["--popup-lifetime-tests"],
            ["--voice-language-tags-tests"], ["--popup-direction-tests", "private-proof-directory"],
            ["--voice-language-tags-tests", "private-proof-directory"],
            ["--favorite-order-tests", "--popup-heading-tests"],
            ["--case", "popup.keyboard-state/run", "--popup-lifetime-tests"],
        };
        try
        {
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT",
                Path.Combine(Path.GetTempPath(), "absent-popup-context-" + Guid.NewGuid().ToString("N") + ".json"));
            Console.SetError(errors);
            foreach (var shape in shapes)
            {
                Assert.That(LegacyRegressionSelectors.AreArgumentsValid(shape), Is.False);
                Assert.That(await RegressionProcessHost.TryRunLegacyAsync(shape, process), Is.EqualTo(2));
            }
            Assert.That(launches, Is.Zero);
            Assert.That(errors.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
                Is.EqualTo(shapes.Select(_ => "Retired legacy regression selector; use NUnit case selection.")));
        }
        finally
        {
            Console.SetError(stderr);
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", source);
        }
    }

    [Test]
    [Property("FishAudioCaseId", "infra.runner/retired-native-popups/empty-catalog-is-metadata-only")]
    public void ProductionEmptyCatalogCanOnlyBeListedAndNeverAuthorizesExecution()
    {
        var source = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        var stdout = Console.Out;
        var stderr = Console.Error;
        var dispatcher = SharedDispatcher();
        var wasCreated = dispatcher.IsValueCreated;
        var events = new List<RegressionActionEvent>();
        using var owner = RegressionActionScope.BeginCase("runner.probe/zero-legacy-metadata", events.Add);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        try
        {
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT",
                Path.Combine(Path.GetTempPath(), "absent-zero-legacy-context-" + Guid.NewGuid().ToString("N") + ".json"));
            Console.SetOut(output);
            Console.SetError(errors);
            Assert.That(RegressionLegacyEntryPoint.Run(["--list"]), Is.Zero);
            Assert.That(output.ToString(), Is.EqualTo("Selected 0 case(s)." + Environment.NewLine));
            Assert.That(errors.ToString(), Is.Empty);
            output.GetStringBuilder().Clear();
            foreach (var arguments in new string[][]
            {
                [], ["--all"], ["--unknown"], ["--list", "--all"], ["--list", "--list"],
                ["--case", "popup.keyboard-state/run"], ["--list", "--partition", "native-popups"],
                ["--rerun-failed", "missing-report.json"], ["--numeric-typography-proof-tests"],
            })
            {
                errors.GetStringBuilder().Clear();
                Assert.That(RegressionLegacyEntryPoint.Run(arguments), Is.EqualTo(2));
                Assert.That(errors.ToString(), Is.EqualTo(RegressionLegacyEntryPoint.RetiredDiagnostic + Environment.NewLine));
                Assert.That(output.ToString(), Is.Empty, "An execution request cannot acquire an empty successful result.");
            }
            Assert.That(events, Is.Empty);
            Assert.That(dispatcher.IsValueCreated, Is.EqualTo(wasCreated));
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", source);
        }
    }

    [Test]
    [Property("FishAudioCaseId", "infra.runner/retired-native-popups/empty-execution-report-cannot-pass-verifier")]
    public void EmptyProductionCatalogCannotAuthorizeSnapshotExecutionEvidence()
    {
        var missing = Path.Combine(Path.GetTempPath(), "absent-legacy-report-" + Guid.NewGuid().ToString("N") + ".json");
        var full = Assert.Throws<InvalidDataException>(() => RegressionSnapshotVerifier.Run(
            ["--verify-snapshot-report", missing, new string('a', 40)]));
        Assert.That(full!.Message, Is.EqualTo("Retired legacy execution has no snapshot catalog evidence."));
        var partition = Assert.Throws<InvalidDataException>(() => RegressionSnapshotVerifier.Run(
            ["--verify-snapshot-partition-report", missing, new string('a', 40), "--partition", "native-popups"]));
        Assert.That(partition!.Message, Is.EqualTo("Retired legacy execution has no snapshot partition evidence."));
        Assert.That(File.Exists(missing), Is.False);
    }

    private static Lazy<Task<Dispatcher>> SharedDispatcher() =>
        (Lazy<Task<Dispatcher>>)typeof(StaTestHost).GetField("SharedDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
}
