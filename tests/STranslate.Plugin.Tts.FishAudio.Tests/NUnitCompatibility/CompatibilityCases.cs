using System.Text.Json;
using System.Windows.Threading;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using NUnitAssert = NUnit.Framework.Assert;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
public sealed class CompatibilityCases
{
    public CompatibilityCases()
    {
        // An execution-only positive control: discovery must leave this absent.
        if (Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT") is { Length: > 0 } root)
        {
            if (!Path.IsPathFullyQualified(root)) throw new InvalidDataException("compat-evidence-root-must-be-absolute");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, $"fixture-created-{Environment.ProcessId}.txt"), "constructed");
        }
    }

    [Test]
    [Order(10)]
    [Property("FishAudioCaseId", "compat.sync/api-key-format")]
    public void SynchronousProductCall()
    {
        NUnitAssert.That(SettingsValidation.IsValidApiKeyFormat("0123456789abcdef0123456789abcdef"), Is.True);
    }

    [Test]
    [Order(20)]
    [Property("FishAudioCaseId", "compat.async/json-string-after-yield")]
    public async Task ProductCallAfterAwait()
    {
        using var document = JsonDocument.Parse("\"compatibility-after-await\"");
        await Task.Yield();
        NUnitAssert.That(SettingsValidation.TryGetString(document.RootElement, out var value), Is.True);
        NUnitAssert.That(value, Is.EqualTo("compatibility-after-await"));
    }

    public static IEnumerable<TestCaseData> VoiceIdRows()
    {
        yield return new TestCaseData("0123456789abcdef0123456789abcdef", true)
            .SetName("voice-id/lowercase-32 \"quoted\" \\ <xml>& 中文")
            .SetProperty("FishAudioCaseId", "compat.source/voice-id/lowercase-32")
            .SetProperty("Order", 30);
        yield return new TestCaseData("0123456789ABCDEF0123456789ABCDEF", false)
            .SetName("voice-id/uppercase-32 \"quoted\" \\ <xml>& 日本語")
            .SetProperty("FishAudioCaseId", "compat.source/voice-id/uppercase-32")
            .SetProperty("Order", 31);
    }

    [TestCaseSource(nameof(VoiceIdRows))]
    [Order(30)]
    public void SourceRowsHaveDistinctProductResults(string voiceId, bool expected)
    {
        NUnitAssert.That(SettingsValidation.IsValidVoiceIdFormat(voiceId), Is.EqualTo(expected));
    }

    [Test]
    [Order(40)]
    [Property("FishAudioCaseId", "compat.native/settings-view-construction")]
    public async Task SettingsViewConstructionOnTheSharedSta()
    {
        // This direct compatibility probe does not install BeginCase or an external
        // process watchdog; it does not establish migrated native lifecycle ownership.
        await StaTestHost.RunOnStaThreadAsync(() =>
        {
            STranslate.Plugin.Tts.FishAudio.View.SettingsView? view = null;
            Exception? failure = null;
            try
            {
                NUnitAssert.That(Thread.CurrentThread.GetApartmentState(), Is.EqualTo(ApartmentState.STA));
                NUnitAssert.That(SynchronizationContext.Current, Is.InstanceOf<DispatcherSynchronizationContext>());
                var context = SynchronizationContext.Current;
                var dispatcher = Dispatcher.CurrentDispatcher;
                NUnitAssert.That(dispatcher.CheckAccess(), Is.True);
                view = new STranslate.Plugin.Tts.FishAudio.View.SettingsView();
                NUnitAssert.That(view.Dispatcher, Is.SameAs(dispatcher));
                NUnitAssert.That(view.Dispatcher.Thread, Is.SameAs(Thread.CurrentThread));
                NUnitAssert.That(SynchronizationContext.Current, Is.SameAs(context));
                NUnitAssert.That(view.Content, Is.Not.Null);
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                try
                {
                    if (view is not null) { view.DataContext = null; view.Content = null; }
                }
                catch when (failure is not null) { /* Preserve the construction/assertion failure. */ }
            }
        });
    }
}
