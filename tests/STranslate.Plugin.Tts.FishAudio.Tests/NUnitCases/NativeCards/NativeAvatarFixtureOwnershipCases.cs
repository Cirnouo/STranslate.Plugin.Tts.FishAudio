using System.Reflection;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using NUnit.Framework;

namespace NUnitCases.NativeCards;

// Additional failure controls use their own fresh desktop host, outside the profile.
[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class NativeAvatarFixtureOwnershipCases
{
    [TestCase("held-preview", TestName = "Avatar_owner_failure_while_original_audio_reply_is_held")]
    [Property("FishAudioCaseId", "infra.native-cards/avatar-owner/held-preview-failure-drains-before-temp-cleanup")]
    public Task HeldPreviewFailureRetiresOriginalWork(string point) =>
        StaTestHost.RunOnStaThreadAsync(() => CheckFailureAsync(point));

    [TestCase("queued-uia-preview", TestName = "Avatar_owner_failure_after_actual_uia_post_before_execution")]
    [Property("FishAudioCaseId", "infra.native-cards/avatar-owner/queued-uia-failure-owns-callback-and-command-tail")]
    public Task QueuedAutomationFailureOwnsTheRealCallback(string point) =>
        StaTestHost.RunOnStaThreadAsync(() => CheckFailureAsync(point));

    private static async Task CheckFailureAsync(string point)
    {
        var primary = new InvalidOperationException("avatar-original-boundary-failure");
        NativeAvatarFixtureTrace? trace = null;
        var injected = false;
        IAsyncRelayCommand? automationCommand = null;
        PropertyChangedEventHandler? independentCommandObserver = null;
        Task? actualAutomationCommand = null;
        var automationCommandWasPending = false;
        Exception? observed = null;
        Dispatcher? owningDispatcher = null;
        try
        {
            try
            {
                await AvatarPreviewSuite.ObserveFailureBoundaryAsync(point, (stage, actual) =>
                {
                    owningDispatcher ??= Dispatcher.FromThread(Thread.CurrentThread);
                    if (stage == "before-uia-preview" && point == "queued-uia-preview")
                    {
                        trace = actual;
                        automationCommand = actual.ViewModel!.ToggleDisplayPreviewCommand;
                        independentCommandObserver = (_, args) =>
                        {
                            if (actualAutomationCommand is null && args.PropertyName == nameof(IAsyncRelayCommand.ExecutionTask)
                                && automationCommand.ExecutionTask is { } original)
                            {
                                actualAutomationCommand = original;
                                automationCommandWasPending = !original.IsCompleted;
                            }
                        };
                        automationCommand.PropertyChanged += independentCommandObserver;
                    }
                    if (stage != point) return;
                    trace = actual;
                    Assert.That(actual.ViewModel, Is.Not.Null);
                    Assert.That(Directory.Exists(actual.Directory), Is.True);
                    if (point == "held-preview")
                    {
                        Assert.That(actual.Responses, Has.Count.EqualTo(1));
                        Assert.That(actual.ObservedReplies, Has.Count.EqualTo(1));
                        Assert.That(ReferenceEquals(actual.Responses[0].Task, actual.ObservedReplies[0]), Is.True);
                        Assert.That(actual.Responses[0].Task.IsCompleted, Is.False);
                        Assert.That(actual.Commands.Any(task => !task.IsCompleted), Is.True);
                    }
                    else
                    {
                        Assert.That(actual.Automation, Has.Count.EqualTo(1));
                        Assert.That(actual.Automation[0].Status, Is.EqualTo(DispatcherOperationStatus.Pending));
                        Assert.That(actual.Automation[0].Task.IsCompleted, Is.False);
                        Assert.That(actual.AutomationTasks, Has.Count.EqualTo(1));
                        Assert.That(ReferenceEquals(actual.Automation[0].Task, actual.AutomationTasks[0]), Is.True);
                    }
                    injected = true;
                    throw primary;
                });
            }
            catch (Exception error) { observed = error; }

            Assert.That(owningDispatcher, Is.Not.Null);
            Assert.That(owningDispatcher!.CheckAccess(), Is.True,
                "Post-owner assertions and rescue must remain on the original scene dispatcher.");
            Assert.That(injected, Is.True, "The real pending ownership boundary must be reached before injection.");
            Assert.That(observed, Is.SameAs(primary));
            Assert.That(trace, Is.Not.Null);
            Assert.That(trace!.Commands, Is.Not.Empty);
            Assert.That(trace.Commands.All(task => task.IsCompletedSuccessfully), Is.True);
            Assert.That(trace.Responses.All(response => response.Task.IsCompleted), Is.True);
            Assert.That(trace.Automation.All(operation => operation.Task.IsCompletedSuccessfully), Is.True);
            if (point == "queued-uia-preview")
            {
                Assert.That(automationCommandWasPending, Is.True, "The real callback must start a genuinely held command tail.");
                Assert.That(actualAutomationCommand, Is.Not.Null);
                Assert.That(trace.Commands.Any(task => ReferenceEquals(task, actualAutomationCommand)), Is.True,
                    "The owner must retain the independently observed original UIA command Task.");
                Assert.That(actualAutomationCommand!.IsCompletedSuccessfully, Is.True);
                Assert.That(trace.AutomationReply, Is.Not.Null);
                Assert.That(trace.ObservedReplies.Any(task => ReferenceEquals(task, trace.AutomationReply!.Task)), Is.True);
                Assert.That(trace.AutomationReply!.Task.IsCanceled, Is.True);
            }
            Assert.That((bool)typeof(STranslate.Plugin.Tts.FishAudio.ViewModel.SettingsViewModel)
                .GetField("_isDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(trace.ViewModel)!, Is.True);
            Assert.That(trace.ViewModel!.IsDisplayVoicePreviewActive, Is.False);
            Assert.That(Directory.Exists(trace.Directory), Is.False);
            Assert.That(primary.Data[RegressionFixtureOwner.CleanupTypesKey], Is.Null,
                "No hidden callback, command, raw reply, state or Temp cleanup failure may accompany the marker.");
        }
        finally
        {
            if (automationCommand is not null && independentCommandObserver is not null)
                automationCommand.PropertyChanged -= independentCommandObserver;
            // Rescue runs only after the authoritative ownership oracles. It uses
            // original objects directly and never calls the candidate cleanup.
            if (trace is not null)
            {
                foreach (var response in trace.Responses) response.TrySetCanceled();
                foreach (var operation in trace.Automation)
                    try { await operation.Task; } catch { }
                var actualTasks = trace.Commands.ToList();
                if (trace.ViewModel?.ToggleDisplayPreviewCommand.ExecutionTask is { } display) actualTasks.Add(display);
                if (trace.ViewModel?.ToggleSearchItemPreviewCommand.ExecutionTask is { } browse) actualTasks.Add(browse);
                try { trace.ViewModel?.Dispose(); } catch { }
                foreach (var task in actualTasks)
                    try { await task; } catch { }
                foreach (var resource in trace.Resources.AsEnumerable().Reverse())
                    try { resource.Dispose(); } catch { }
                var directory = System.IO.Path.GetFullPath(trace.Directory);
                var parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FishAudioTests"));
                if (Directory.Exists(directory) &&
                    string.Equals(System.IO.Path.GetDirectoryName(directory), parent, StringComparison.OrdinalIgnoreCase) &&
                    Guid.TryParseExact(System.IO.Path.GetFileName(directory), "N", out _) &&
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                    try { Directory.Delete(directory, recursive: true); } catch { }
            }
        }
    }
}
