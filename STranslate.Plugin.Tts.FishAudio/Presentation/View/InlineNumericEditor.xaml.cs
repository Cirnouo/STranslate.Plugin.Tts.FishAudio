using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace STranslate.Plugin.Tts.FishAudio.View;

/// <summary>A numeric display that holds an isolated text draft until commit.</summary>
public partial class InlineNumericEditor : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(InlineNumericEditor),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(InlineNumericEditor), new PropertyMetadata(0d, OnPresentationChanged));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(InlineNumericEditor), new PropertyMetadata(1d, OnPresentationChanged));
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
    private readonly NumericWheelAdjustment _wheel = new();
    internal TimeProvider WheelTimeProvider { set => _wheel.TimeProvider = value; }
    private bool _observingDisplayTemplate;
    private readonly TranslateTransform _unitTranslation = new();
    private AlignmentContext? _alignmentContext;
    private DispatcherOperation? _alignmentOperation;
    private bool _alignmentAttached;
    private int _alignmentGeneration;
    private sealed record TextContext(string Text, FontFamily Family, double Size, FontStyle Style,
        FontWeight Weight, FontStretch Stretch, string Language, FlowDirection Flow,
        TextFormattingMode Formatting, double PixelsPerDip, Size Bounds, Point Origin, ControlTemplate? Template = null);
    private sealed record AlignmentContext(TextContext Number, TextContext Unit, bool Editing);
    private readonly System.ComponentModel.DependencyPropertyDescriptor _displayTemplate =
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Control.TemplateProperty, typeof(Button));

    public InlineNumericEditor()
    {
        InitializeComponent();
        UnitText.RenderTransform = _unitTranslation;
        AttachAlignment();
        Loaded += (_, _) => AttachAlignment();
        DataObject.AddPastingHandler(EditorBox, EditorBox_Pasting);
        DisplayButton.Loaded += (_, _) => { ObserveDisplayTemplate(); RestartDisplayHover(); };
        DisplayButton.Unloaded += (_, _) =>
        {
            _wheel.EndHover();
            if (!_observingDisplayTemplate) return;
            _displayTemplate.RemoveValueChanged(DisplayButton, DisplayTemplateChanged);
            _observingDisplayTemplate = false;
        };
        RefreshDisplay();
        Unloaded += (_, _) => { DetachAlignment(); EndEdit(commit: false); _wheel.EndHover(); };
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
        EditorBox.MaxLength = IntegerDigits + (DecimalPlaces > 0 ? DecimalPlaces + 1 : 0) + (Minimum < 0 ? 1 : 0);
        DisplayButton.Content = Value.ToString($"F{DecimalPlaces}", CultureInfo.CurrentCulture);
        UnitText.Text = Unit;
        UnitText.Visibility = string.IsNullOrEmpty(Unit) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(DisplayButton, $"{Label} {DisplayButton.Content} {Unit}".Trim());
        AutomationProperties.SetName(EditorBox, Label);
    }

    private void AttachAlignment()
    {
        if (_alignmentAttached) return;
        _alignmentAttached = true;
        _alignmentContext = null;
        LayoutUpdated += AlignmentLayoutUpdated;
    }

    private void DetachAlignment()
    {
        _alignmentAttached = false;
        LayoutUpdated -= AlignmentLayoutUpdated;
        _alignmentGeneration++;
        _alignmentOperation?.Abort();
        _alignmentOperation = null;
        _alignmentContext = null;
    }

    private AlignmentContext CaptureAlignment()
    {
        Control number = IsEditing ? EditorBox : DisplayButton;
        var text = IsEditing ? EditorBox.Text : DisplayButton.Content?.ToString() ?? "";
        var numeric = new TextContext(text, number.FontFamily, number.FontSize, number.FontStyle,
            number.FontWeight, number.FontStretch, number.Language.IetfLanguageTag, number.FlowDirection,
            TextOptions.GetTextFormattingMode(number), VisualTreeHelper.GetDpi(number).PixelsPerDip,
            number.RenderSize, number.TranslatePoint(new Point(), this), number.Template);
        var origin = UnitText.TranslatePoint(new Point(), this);
        origin.Y -= _unitTranslation.Y;
        var unit = new TextContext(UnitText.Text, UnitText.FontFamily, UnitText.FontSize, UnitText.FontStyle,
            UnitText.FontWeight, UnitText.FontStretch, UnitText.Language.IetfLanguageTag, UnitText.FlowDirection,
            TextOptions.GetTextFormattingMode(UnitText), VisualTreeHelper.GetDpi(UnitText).PixelsPerDip,
            UnitText.RenderSize, origin);
        return new(numeric, unit, IsEditing);
    }

    private void AlignmentLayoutUpdated(object? sender, EventArgs args)
    {
        if (UnitText.Visibility != Visibility.Visible || _alignmentOperation is not null
            || !IsAncestorOf(DisplayButton) || !IsAncestorOf(EditorBox) || !IsAncestorOf(UnitText)) return;
        var context = CaptureAlignment();
        if (_alignmentContext == context) return;
        _alignmentContext = context;
        var generation = _alignmentGeneration;
        // Loaded priority runs after pending Render work. One bounded callback
        // per changed context; missing/stale drawings never trigger a retry loop.
        _alignmentOperation = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _alignmentOperation = null;
            if (!_alignmentAttached || generation != _alignmentGeneration) return;
            var current = CaptureAlignment();
            _alignmentContext = current;
            var hasDigits = current.Number.Text.Any(character => character is >= '0' and <= '9');
            Visual number = IsEditing && hasDigits ? EditorBox : DisplayButton;
            var text = IsEditing && hasDigits ? current.Number.Text : DisplayButton.Content?.ToString() ?? "";
            if (!NativeTextOutline.TryBottom(number, this, text, out var numberBottom)
                || !NativeTextOutline.TryBottom(UnitText, this, current.Unit.Text, out var unitBottom)
                || current != CaptureAlignment()) return;
            var shift = _unitTranslation.Y + numberBottom - unitBottom;
            if (Math.Abs(shift - _unitTranslation.Y) > .000001) _unitTranslation.Y = shift;
        }));
    }

    private void DisplayButton_Click(object sender, RoutedEventArgs args)
    {
        if (IsEditing) return;
        _wheel.EndHover();
        EditorBox.Text = DisplayButton.Content?.ToString() ?? "";
        IsEditing = true;
        DisplayButton.Visibility = Visibility.Collapsed;
        EditorBox.Visibility = Visibility.Visible;
        // Focus immediately: no queued focus callback can steal focus back
        // after the user has clicked another control.
        EditorBox.Focus();
        EditorBox.SelectAll();
    }

    private void DisplayButton_PreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (IsEditing || args.Delta == 0 || !_wheel.IsHoverReady) return;
        args.Handled = true;
        SetCurrentValue(ValueProperty, _wheel.Adjust(Value, Minimum, Maximum, DecimalPlaces, args.Delta));
    }

    private void ObserveDisplayTemplate()
    {
        if (_observingDisplayTemplate) return;
        _displayTemplate.AddValueChanged(DisplayButton, DisplayTemplateChanged);
        _observingDisplayTemplate = true;
    }

    private void DisplayTemplateChanged(object? sender, EventArgs args) => RestartDisplayHover();

    private void RestartDisplayHover() =>
        _wheel.RestartHover(DisplayButton is { IsMouseOver: true, IsVisible: true } && !IsEditing);

    private void DisplayButton_MouseEnter(object sender, MouseEventArgs args) => _wheel.BeginHover();
    private void DisplayButton_MouseLeave(object sender, MouseEventArgs args) => _wheel.EndHover();

    public override void OnApplyTemplate()
    {
        _wheel.EndHover();
        base.OnApplyTemplate();
        RestartDisplayHover();
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
        RestartDisplayHover();
    }

    private bool TryParseDraft(string text, out double value)
    {
        value = 0;
        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (!IsValidDraft(text)
            || (!double.TryParse(text, style, CultureInfo.CurrentCulture, out value)
                && !double.TryParse(text, style, CultureInfo.InvariantCulture, out value))
            || !double.IsFinite(value) || value < Minimum || value > Maximum)
            return false;
        value = Configuration.SettingsNormalizer.RoundToPrecision((decimal)value, DecimalPlaces);
        return true;
    }

    private int IntegerDigits => Math.Max(1, (int)Math.Floor(Math.Log10(Math.Max(1, Math.Max(Math.Abs(Minimum), Math.Abs(Maximum))))) + 1);

    private bool IsValidDraft(string text)
    {
        if (text.Length > EditorBox.MaxLength) return false;
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        var hasDecimal = false;
        var integerDigits = 0;
        var fractionDigits = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '-' && index == 0 && Minimum < 0) continue;
            if (character == '.' || (separator.Length == 1 && character == separator[0]))
            {
                if (hasDecimal || DecimalPlaces == 0) return false;
                hasDecimal = true;
                continue;
            }
            if (character is < '0' or > '9') return false;
            if (hasDecimal) fractionDigits++;
            else integerDigits++;
        }
        return integerDigits <= IntegerDigits && fractionDigits <= DecimalPlaces;
    }

    private string ProposedText(string replacement) =>
        EditorBox.Text.Remove(EditorBox.SelectionStart, EditorBox.SelectionLength).Insert(EditorBox.SelectionStart, replacement);

    private void EditorBox_PreviewTextInput(object sender, TextCompositionEventArgs args) =>
        args.Handled = !IsValidDraft(ProposedText(args.Text));

    private void EditorBox_Pasting(object sender, DataObjectPastingEventArgs args)
    {
        var text = args.DataObject.GetData(DataFormats.UnicodeText) as string
            ?? args.DataObject.GetData(DataFormats.Text) as string;
        if (text is null || !IsValidDraft(ProposedText(text)))
            args.CancelCommand();
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
