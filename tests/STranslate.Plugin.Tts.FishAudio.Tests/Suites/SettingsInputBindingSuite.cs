using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class SettingsInputBindingSuite
{
    internal static Task ApiKeyPasswordInputPersistsBeforeFocusLeavesAsync() =>
        RunOnStaThreadAsync(() =>
        {
            var settings = new Settings { ApiKey = AppliedKey };
            var context = CreateContext(settings: settings);
            var storage = (ContextProxy)(object)context;
            string? savedApiKey = null;
            storage.SaveSettings = value => savedApiKey = value.ApiKey;
            using var viewModel = new SettingsViewModel(context, settings);
            var view = new SettingsView { DataContext = viewModel };
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
