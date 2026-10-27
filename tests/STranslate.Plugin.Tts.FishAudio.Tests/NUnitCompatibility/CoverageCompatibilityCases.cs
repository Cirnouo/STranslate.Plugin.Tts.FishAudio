using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using NUnitAssert = NUnit.Framework.Assert;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class CoverageCompatibilityCases
{
    private const string CaseA = "compat.same-host/a-draft-boolean-rounding";
    private const string CaseB = "compat.same-host/b-loudness-integer-top-p";
    private JsonDocument? _sharedDocument;
    private string _caseId = "";
    private int _processId;

    [SetUp]
    public void ObserveSharedProductSetup()
    {
        _caseId = TestContext.CurrentContext.Test.Properties.Get("FishAudioCaseId") as string
            ?? throw new InvalidOperationException("compat-case-id-missing");
        _processId = Environment.ProcessId;
        _sharedDocument = JsonDocument.Parse("{\"fraction\":0.82,\"optional\":null}");
        NUnitAssert.That(SettingsValidation.TryGetDouble(_sharedDocument.RootElement.GetProperty("fraction"), out var fraction), Is.True);
        NUnitAssert.That(fraction, Is.EqualTo(0.82));
        NUnitAssert.That(SettingsValidation.TryGetDouble(_sharedDocument.RootElement.GetProperty("optional"), out var rejected), Is.False);
        NUnitAssert.That(rejected, Is.EqualTo(0d));
        RecordPhase("setup");
    }

    [TearDown]
    public void ObserveSharedProductTeardown()
    {
        try
        {
            NUnitAssert.That(_sharedDocument, Is.Not.Null);
            NUnitAssert.That(SettingsValidation.TryGetNullableString(_sharedDocument!.RootElement.GetProperty("optional"), out var optional), Is.True);
            NUnitAssert.That(optional, Is.Null);
            NUnitAssert.That(SettingsValidation.TryGetNullableString(_sharedDocument.RootElement.GetProperty("fraction"), out var rejected), Is.False);
            NUnitAssert.That(rejected, Is.Null);
            RecordPhase("teardown");
        }
        finally
        {
            _sharedDocument?.Dispose();
            _sharedDocument = null;
        }
    }

    [Test]
    [Property("FishAudioCaseId", CaseA)]
    public async Task CaseAHasExclusiveProductCallsAcrossExecutionContexts()
    {
        NUnitAssert.That(SettingsValidation.IsValidVoiceIdDraft(""), Is.True);
        NUnitAssert.That(SettingsValidation.IsValidVoiceIdDraft("a0f"), Is.True);
        NUnitAssert.That(SettingsValidation.IsValidVoiceIdDraft("A0f"), Is.False);
        NUnitAssert.That(SettingsValidation.IsValidVoiceIdDraft(new string('a', 33)), Is.False);
        RecordPhase("sync");

        using var document = JsonDocument.Parse("{\"valid\":true,\"invalid\":\"true\"}");
        await Task.Yield();
        NUnitAssert.That(SettingsValidation.TryGetBoolean(document.RootElement.GetProperty("valid"), out var value), Is.True);
        NUnitAssert.That(value, Is.True);
        NUnitAssert.That(SettingsValidation.TryGetBoolean(document.RootElement.GetProperty("invalid"), out var rejected), Is.False);
        NUnitAssert.That(rejected, Is.False);
        RecordPhase("after-await");

        await StaTestHost.RunOnStaThreadAsync(() =>
        {
            AssertSharedStaContext();
            NUnitAssert.That(SettingsNormalizer.RoundToPrecision(0.825m, 2), Is.EqualTo(0.83));
            NUnitAssert.That(SettingsNormalizer.RoundToPrecision(-0.001m, 2), Is.EqualTo(0d));
            NUnitAssert.That(double.IsNegative(SettingsNormalizer.RoundToPrecision(-0.001m, 2)), Is.False);
            RecordPhase("owned-sta");
        });
    }

    [Test]
    [Property("FishAudioCaseId", CaseB)]
    public async Task CaseBHasExclusiveProductCallsAcrossExecutionContexts()
    {
        NUnitAssert.That(FishAudioModelPolicy.SupportsNormalizeLoudness("s2-pro"), Is.True);
        NUnitAssert.That(FishAudioModelPolicy.SupportsNormalizeLoudness("drama-3"), Is.False);
        RecordPhase("sync");

        using var document = JsonDocument.Parse("{\"valid\":42,\"invalid\":42.5}");
        await Task.Yield();
        NUnitAssert.That(SettingsValidation.TryGetInt32(document.RootElement.GetProperty("valid"), out var value), Is.True);
        NUnitAssert.That(value, Is.EqualTo(42));
        NUnitAssert.That(SettingsValidation.TryGetInt32(document.RootElement.GetProperty("invalid"), out var rejected), Is.False);
        NUnitAssert.That(rejected, Is.EqualTo(0));
        RecordPhase("after-await");

        await StaTestHost.RunOnStaThreadAsync(() =>
        {
            AssertSharedStaContext();
            NUnitAssert.That(SettingsNormalizer.NormalizeTopP(0), Is.EqualTo(0.05));
            NUnitAssert.That(SettingsNormalizer.NormalizeTopP(0.82), Is.EqualTo(0.82));
            NUnitAssert.That(SettingsNormalizer.NormalizeTopP(double.NaN), Is.EqualTo(0.7));
            RecordPhase("owned-sta");
        });
    }

    private static void AssertSharedStaContext()
    {
        // These direct calls observe shared-STA attribution only. They do not install
        // BeginCase or an external process watchdog and do not prove native lifecycle migration.
        NUnitAssert.That(Thread.CurrentThread.GetApartmentState(), Is.EqualTo(ApartmentState.STA));
        NUnitAssert.That(SynchronizationContext.Current, Is.InstanceOf<DispatcherSynchronizationContext>());
        NUnitAssert.That(Dispatcher.CurrentDispatcher.CheckAccess(), Is.True);
    }

    private void RecordPhase(string phase)
    {
        NUnitAssert.That(Environment.ProcessId, Is.EqualTo(_processId));
        TestContext.Progress.WriteLine($"compatibility-phase case={_caseId} phase={phase} process={_processId} thread={Environment.CurrentManagedThreadId} apartment={Thread.CurrentThread.GetApartmentState()} timestamp={Stopwatch.GetTimestamp()} frequency={Stopwatch.Frequency}");
    }
}
