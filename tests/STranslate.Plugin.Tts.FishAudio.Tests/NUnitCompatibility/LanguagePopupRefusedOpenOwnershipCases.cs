using System.Reflection;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.View;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class LanguagePopupRefusedOpenOwnershipCases
{
    [Test]
    [Property("FishAudioCaseId", "infra.native-popups/language/refused-open-has-no-native-lifetime")]
    public Task RefusedOpenRetiresItsObserverWithoutInventingAClosedEvent() =>
        StaTestHost.RunOnStaThreadAsync((Func<Task>)(async () =>
        {
            var tags = new VoiceLanguageTags { Languages = ["en", "ja", "de"] };
            var opened = 0;
            var owner = new LanguagePopupFixtureOwner(new LanguagePopupFixtureHooks
            {
                PopupOpened = _ => opened++
            });
            owner.Tags = tags;
            owner.RequestClose = () => tags.ClosePopup();
            await owner.RunAsync(async () =>
            {
                // This real control is unmeasured and detached. Its ordinary
                // refusal guards must not become a fictitious native lifetime.
                Assert.That(tags.IsLoaded, Is.False);
                Assert.That(typeof(VoiceLanguageTags).GetField("_inlineCount",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tags), Is.EqualTo(-1));
                owner.Open(tags, tags.OpenPopup);
                Assert.That(tags.IsPopupOpen, Is.False);
                Assert.That(opened, Is.Zero);
                Assert.That(owner.Lifetimes.Count, Is.EqualTo(1));
                var lifetime = owner.Lifetimes[0];
                Assert.That(lifetime.Source, Is.Null);
                Assert.That(lifetime.OpenedEvents, Is.Zero);
                Assert.That(lifetime.ClosedEvents, Is.Zero);
                Assert.That(lifetime.ClosedTask.IsCompleted, Is.False);
                await owner.JoinCurrentAsync();
                Assert.That(lifetime.HandlersAttached, Is.False);
                Assert.That(lifetime.ClosedTask.IsCompleted, Is.False);
                Assert.That(lifetime.ClosureVerified, Is.False);
            });
            Assert.That(owner.Lifetimes[0].HandlersAttached, Is.False);
            Assert.That(opened, Is.Zero);
        }));
}
