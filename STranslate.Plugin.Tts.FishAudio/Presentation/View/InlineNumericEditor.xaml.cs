using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace STranslate.Plugin.Tts.FishAudio.View;

/// <summary>A numeric display that holds an isolated text draft until commit.</summary>
public partial class InlineNumericEditor : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(InlineNumericEditor),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(InlineNumericEditor), new PropertyMetadata(0d));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(InlineNumericEditor), new PropertyMetadata(1d));
    public static readonly DependencyProperty DecimalPlacesProperty = DependencyProperty.Register(nameof(DecimalPlaces), typeof(int), typeof(InlineNumericEditor), new PropertyMetadata(2, OnPresentationChanged));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(InlineNumericEditor), new PropertyMetadata("", OnPresentationChanged));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(InlineNumericEditor), new PropertyMetadata("", OnPresentationChanged));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int DecimalPlaces { get => (int)GetValue(DecimalPlacesProperty); set => SetValue(DecimalPlacesProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    internal bool IsEditing { get; private set; }

    public InlineNumericEditor()
    {
        InitializeComponent();
        RefreshDisplay();
        Unloaded += (_, _) => EndEdit(commit: false);
    }

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var editor = (InlineNumericEditor)sender;
        // A slider or a replacement VM owns a newer value. A stale draft may
        // never overwrite it when focus subsequently leaves the input.
        editor.EndEdit(commit: false);
        editor.RefreshDisplay();
    }

    private static void OnPresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((InlineNumericEditor)sender).RefreshDisplay();

    private void RefreshDisplay()
    {
        if (DisplayButton is null) return;
        DisplayButton.Content = Value.ToString($"F{DecimalPlaces}", CultureInfo.CurrentCulture);
        UnitText.Text = Unit;
        AutomationProperties.SetName(DisplayButton, $"{Label} {DisplayButton.Content} {Unit}".Trim());
        AutomationProperties.SetName(EditorBox, Label);
    }

    private void DisplayButton_Click(object sender, RoutedEventArgs args)
    {
        if (IsEditing) return;
        EditorBox.Text = DisplayButton.Content?.ToString() ?? "";
        IsEditing = true;
        DisplayButton.Visibility = Visibility.Collapsed;
        EditorBox.Visibility = Visibility.Visible;
        // Focus immediately: no queued focus callback can steal focus back
        // after the user has clicked another control.
        EditorBox.Focus();
        EditorBox.SelectAll();
    }

    internal void CommitEdit() => EndEdit(commit: true);

    private void EndEdit(bool commit)
    {
        if (!IsEditing) return;
        IsEditing = false;
        if (commit && TryParseDraft(EditorBox.Text, out var value))
            SetCurrentValue(ValueProperty, value);
        EditorBox.Visibility = Visibility.Collapsed;
        DisplayButton.Visibility = Visibility.Visible;
        RefreshDisplay();
    }

    private bool TryParseDraft(string text, out double value)
    {
        const NumberStyles style = NumberStyles.Float;
        if ((!double.TryParse(text, style, CultureInfo.CurrentCulture, out value)
                && !double.TryParse(text, style, CultureInfo.InvariantCulture, out value))
            || !double.IsFinite(value) || value < Minimum || value > Maximum)
            return false;
        value = (double)Math.Round((decimal)value, DecimalPlaces, MidpointRounding.AwayFromZero);
        return true;
    }

    private void EditorBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args) => CommitEdit();

    private void EditorBox_KeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Enter or Key.Escape)) return;
        EndEdit(commit: args.Key == Key.Enter);
        DisplayButton.Focus();
        args.Handled = true;
    }
}
