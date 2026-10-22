using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace STranslate.Plugin.Tts.FishAudio.View;

public partial class SettingsView : UserControl
{
    private static readonly Regex DigitsOnly = new(@"^\d+$", RegexOptions.Compiled);

    public SettingsView()
    {
        InitializeComponent();
        PreviewMouseDown += OnPreviewMouseDown;
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject clicked)
        {
            foreach (var editor in FindNumericEditors(this))
                if (editor.IsEditing && !HasAncestor(clicked, editor))
                    editor.CommitEdit();
        }
        if (Keyboard.FocusedElement is not TextBox) return;
        if (e.OriginalSource is not DependencyObject source) return;
        if (HasInteractiveAncestor(source)) return;
        Keyboard.ClearFocus();
    }

    private static bool HasAncestor(DependencyObject source, DependencyObject ancestor)
    {
        for (DependencyObject? current = source; current is not null; current = ParentOf(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject source) => source switch
    {
        ContentElement content => ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent,
        Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(source),
        _ => LogicalTreeHelper.GetParent(source),
    };

    private static IEnumerable<InlineNumericEditor> FindNumericEditors(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is InlineNumericEditor editor) yield return editor;
            else foreach (var descendant in FindNumericEditors(child)) yield return descendant;
        }
    }

    private static bool HasInteractiveAncestor(DependencyObject obj)
    {
        var current = obj;
        while (current is not null)
        {
            if (current is TextBox or PasswordBox or ButtonBase or ComboBox or Slider or Thumb)
                return true;
            current = ParentOf(current);
        }
        return false;
    }

    private void VoiceIdInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox input)
            e.Handled = !Configuration.SettingsValidation.IsValidVoiceIdDraft(ReplaceSelection(input, e.Text));
    }

    private void VoiceIdInput_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox input || !e.DataObject.GetDataPresent(DataFormats.UnicodeText)
            || e.DataObject.GetData(DataFormats.UnicodeText) is not string text
            || !Configuration.SettingsValidation.IsValidVoiceIdPaste(text)
            || !Configuration.SettingsValidation.IsValidVoiceIdDraft(ReplaceSelection(input, text)))
            e.CancelCommand();
    }

    private static string ReplaceSelection(TextBox input, string replacement) =>
        input.Text.Remove(input.SelectionStart, input.SelectionLength).Insert(input.SelectionStart, replacement);

    private void PageInputBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnly.IsMatch(e.Text);
    }

    private void PageInputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModel.SettingsViewModel vm && vm.CommitPageInputCommand.CanExecute(null))
            vm.CommitPageInputCommand.Execute(null);
    }

    private void FavoritePageInputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModel.SettingsViewModel vm && vm.CommitFavoritePageInputCommand.CanExecute(null))
            vm.CommitFavoritePageInputCommand.Execute(null);
    }

    private void S21ProFreePromoCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && HasInteractiveAncestor(source))
            return;

        if (DataContext is not ViewModel.SettingsViewModel vm || !vm.UseS21ProFreePromoCommand.CanExecute(null))
            return;

        vm.UseS21ProFreePromoCommand.Execute(null);
        SynthesisModelCard.BringIntoView();
        FlashSynthesisModelCard();
    }

    private void FlashSynthesisModelCard()
    {
        var originalBrush = SynthesisModelCard.Background as SolidColorBrush;
        var flashBrush = new SolidColorBrush(Color.FromArgb(0x45, 0x4C, 0xAF, 0x50));
        SynthesisModelCard.Background = flashBrush;

        var animation = new ColorAnimation
        {
            From = flashBrush.Color,
            To = originalBrush?.Color ?? Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(900),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };

        animation.Completed += (_, _) => SynthesisModelCard.Background = originalBrush;
        flashBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }
}
