using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using STranslate.Plugin.Tts.FishAudio.Presentation;

namespace STranslate.Plugin.Tts.FishAudio.View;

public static class PageInputBehavior
{
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.RegisterAttached(
        "Maximum", typeof(int), typeof(PageInputBehavior), new PropertyMetadata(0, OnMaximumChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(InputState), typeof(PageInputBehavior));

    public static int GetMaximum(DependencyObject target) => (int)target.GetValue(MaximumProperty);
    public static void SetMaximum(DependencyObject target, int value) => target.SetValue(MaximumProperty, value);

    private static void OnMaximumChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not TextBox input) return;
        if (input.GetValue(StateProperty) is not InputState)
        {
            input.SetValue(StateProperty, new InputState());
            input.PreviewTextInput += OnPreviewTextInput;
            input.PreviewKeyDown += OnPreviewKeyDown;
            input.TextChanged += OnTextChanged;
            DataObject.AddPastingHandler(input, OnPasting);
        }
        Normalize(input);
    }

    private static string ReplaceSelection(TextBox input, string replacement) =>
        input.Text.Remove(input.SelectionStart, input.SelectionLength).Insert(input.SelectionStart, replacement);

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs args) =>
        args.Handled = !PageInputDraft.IsDigits(ReplaceSelection((TextBox)sender, args.Text));

    private static void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Space) args.Handled = true;
    }

    private static void OnPasting(object sender, DataObjectPastingEventArgs args)
    {
        if (!args.DataObject.GetDataPresent(DataFormats.UnicodeText)
            || args.DataObject.GetData(DataFormats.UnicodeText) is not string text
            || !PageInputDraft.IsDigits(text)
            || !PageInputDraft.IsDigits(ReplaceSelection((TextBox)sender, text)))
            args.CancelCommand();
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs args) => Normalize((TextBox)sender);

    private static void Normalize(TextBox input)
    {
        var state = (InputState)input.GetValue(StateProperty);
        if (state.IsNormalizing) return;
        var normalized = PageInputDraft.Clamp(input.Text, GetMaximum(input));
        if (normalized == input.Text) return;
        state.IsNormalizing = true;
        try
        {
            var start = input.SelectionStart;
            var length = input.SelectionLength;
            input.SetCurrentValue(TextBox.TextProperty, normalized);
            input.Select(Math.Min(start, normalized.Length), Math.Min(length, Math.Max(0, normalized.Length - start)));
            input.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
        finally
        {
            state.IsNormalizing = false;
        }
    }

    private sealed class InputState
    {
        internal bool IsNormalizing { get; set; }
    }
}
