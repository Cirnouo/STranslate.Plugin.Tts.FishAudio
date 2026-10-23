using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class SettingsInputBindingSuite
{
    internal static async Task ApiKeyPasswordInputPersistsBeforeFocusLeavesAsync()
    {
        SettingsView view = null!;
        var constructionTimeout = TimeSpan.FromSeconds(15);
        var phase = Stopwatch.StartNew();
        var constructionFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.WriteLine($"[settings-input] View construction queued with {constructionTimeout.TotalSeconds:0}s execution budget.");
        try
        {
            await RunOnStaThreadAsync(() =>
            {
                Console.WriteLine($"[settings-input] View construction entered after {phase.ElapsedMilliseconds}ms on thread {Environment.CurrentManagedThreadId}.");
                try
                {
                    view = new SettingsView();
                    Console.WriteLine($"[settings-input] Constructor returned after {phase.ElapsedMilliseconds}ms.");
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"[settings-input] Constructor faulted with {exception.GetType().Name} after {phase.ElapsedMilliseconds}ms.");
                    throw;
                }
                finally
                {
                    constructionFinished.TrySetResult();
                }
            }, constructionTimeout);
        }
        catch (TimeoutException)
        {
            Console.WriteLine($"[settings-input] Original construction deadline failed after {phase.ElapsedMilliseconds}ms; observing the same action without retrying it.");
            try
            {
                await constructionFinished.Task.WaitAsync(TimeSpan.FromSeconds(20));
                Console.WriteLine($"[settings-input] Timed-out action exited after {phase.ElapsedMilliseconds}ms; preserving the original failed deadline.");
            }
            catch (TimeoutException)
            {
                Console.WriteLine($"[settings-input] Timed-out action remains incomplete after {phase.ElapsedMilliseconds}ms.");
            }
            throw;
        }
        Console.WriteLine($"[settings-input] View construction completed after {phase.ElapsedMilliseconds}ms.");

        phase.Restart();
        Console.WriteLine("[settings-input] Binding assertions queued.");
        await RunOnStaThreadAsync(() =>
        {
            Console.WriteLine($"[settings-input] Binding assertions entered after {phase.ElapsedMilliseconds}ms.");
            var settings = new Settings { ApiKey = AppliedKey };
            var context = CreateContext(settings: settings);
            var storage = (ContextProxy)(object)context;
            string? savedApiKey = null;
            storage.SaveSettings = value => savedApiKey = value.ApiKey;
            using var viewModel = new SettingsViewModel(context, settings);
            view.DataContext = viewModel;
            DrainBindings();

            var passwordBox = Descendants(view).OfType<PasswordBox>().Single(box =>
                BindingOperations.GetBinding(box, PasswordBoxAssistant.PasswordProperty)?.Path.Path
                    == nameof(SettingsViewModel.ApiKey));
            AssertEqual(AppliedKey, passwordBox.Password, "The actual settings input must display the stored API Key");
            var lostFocusCount = 0;
            passwordBox.LostFocus += (_, _) => lostFocusCount++;

            // SDK 1.0.15 already defaults to TwoWay, but its LostFocus update
            // trigger must not defer the plugin's immediate-save contract.
            foreach (var editedKey in new[] { DraftKey, "", AppliedKey })
            {
                var savesBeforeEdit = storage.SaveCount;
                passwordBox.Password = editedKey;
                DrainBindings();

                AssertEqual(0, lostFocusCount, "This regression must not commit the edit by changing focus");
                AssertEqual(editedKey, viewModel.ApiKey, "Password input must update the ViewModel before losing focus");
                AssertEqual(editedKey, settings.ApiKey, "Password input must update runtime settings before losing focus");
                AssertEqual(editedKey, savedApiKey, "Password input must reach host storage before losing focus");
                AssertEqual(savesBeforeEdit + 1, storage.SaveCount, "Each password edit must save once without a focus change");
            }
        });
        Console.WriteLine($"[settings-input] Binding assertions completed after {phase.ElapsedMilliseconds}ms.");
    }

    private static void DrainBindings() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
