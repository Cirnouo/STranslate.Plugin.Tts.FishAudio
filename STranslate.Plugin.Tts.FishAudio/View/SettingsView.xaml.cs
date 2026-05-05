using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace STranslate.Plugin.Tts.FishAudio.View;

public partial class SettingsView : UserControl
{
    private static readonly Regex DigitsOnly = new(@"^\d+$", RegexOptions.Compiled);

    public SettingsView()
    {
        InitializeComponent();
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBox) return;
        if (e.OriginalSource is not DependencyObject source) return;
        if (HasInteractiveAncestor(source)) return;
        Keyboard.ClearFocus();
    }

    private static bool HasInteractiveAncestor(DependencyObject obj)
    {
        var current = obj;
        while (current is not null)
        {
            if (current is TextBox or PasswordBox or ButtonBase or ComboBox or Slider or Thumb)
                return true;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void PageInputBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnly.IsMatch(e.Text);
    }

    private void PageInputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModel.SettingsViewModel vm && vm.CommitPageInputCommand.CanExecute(null))
            vm.CommitPageInputCommand.Execute(null);
    }
}
