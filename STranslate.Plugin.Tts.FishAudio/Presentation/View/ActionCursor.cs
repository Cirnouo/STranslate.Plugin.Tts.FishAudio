using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using iNKORE.UI.WPF.Modern.Controls;

namespace STranslate.Plugin.Tts.FishAudio.View;

/// <summary>Answers pointer queries only on this plugin's owned presentation surfaces.</summary>
public static class ActionCursor
{
    public static readonly DependencyProperty IsScopeProperty = DependencyProperty.RegisterAttached(
        "IsScope", typeof(bool), typeof(ActionCursor), new PropertyMetadata(false, ScopeChanged));
    public static readonly DependencyProperty IsActionProperty = DependencyProperty.RegisterAttached(
        "IsAction", typeof(bool), typeof(ActionCursor), new PropertyMetadata(false));
    private static readonly DependencyProperty ScopeStateProperty = DependencyProperty.RegisterAttached(
        "ScopeState", typeof(ScopeState), typeof(ActionCursor));

    public static bool GetIsScope(DependencyObject element) => (bool)element.GetValue(IsScopeProperty);
    public static void SetIsScope(DependencyObject element, bool value) => element.SetValue(IsScopeProperty, value);
    public static bool GetIsAction(DependencyObject element) => (bool)element.GetValue(IsActionProperty);
    public static void SetIsAction(DependencyObject element, bool value) => element.SetValue(IsActionProperty, value);

    private static void ScopeChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        if (element.GetValue(ScopeStateProperty) is ScopeState previous) previous.Dispose();
        element.ClearValue(ScopeStateProperty);
        if ((bool)args.NewValue) element.SetValue(ScopeStateProperty, new ScopeState(element));
    }

    private static void Query(object sender, QueryCursorEventArgs args)
    {
        if (sender is not DependencyObject scope || args.OriginalSource is not DependencyObject source) return;
        var ancestors = new List<DependencyObject>();
        for (var current = source; current is not null; current = Parent(current))
        {
            ancestors.Add(current);
            if (ReferenceEquals(current, scope)) break;
        }
        if (!ancestors.Contains(scope) || ancestors.OfType<UIElement>().Any(element =>
                !element.IsEnabled || !element.IsVisible || !element.IsHitTestVisible)) return;
        foreach (var current in ancestors)
        {
            // Nearest ownership wins: embedded buttons remain actions, editor/link text remains native.
            if (current is TextBoxBase or PasswordBox or Hyperlink or HyperlinkButton) return;
            if (current is SettingsCard card && !card.IsClickEnabled) return;
            if (current is ComboBox { IsEditable: true } combo && !IsDropDownButton(source, combo)) return;
            if (current is ButtonBase or ComboBoxItem or ComboBox or ToggleSwitch or Slider or ScrollBar || GetIsAction(current))
            {
                args.Cursor = Cursors.Hand;
                args.Handled = true;
                return;
            }
        }
    }

    private static bool IsDropDownButton(DependencyObject source, ComboBox combo)
    {
        for (var current = source; current is not null && !ReferenceEquals(current, combo); current = Parent(current))
            if (current is ButtonBase) return true;
        return false;
    }

    private static DependencyObject? Parent(DependencyObject element) => element switch
    {
        ContentElement content => ContentOperations.GetParent(content) ?? (content as FrameworkContentElement)?.Parent,
        Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(element) ?? (element as FrameworkElement)?.Parent,
        _ => LogicalTreeHelper.GetParent(element),
    };

    private sealed class ScopeState : IDisposable
    {
        private readonly FrameworkElement _owner;
        private readonly Dictionary<ComboBox, FrameworkElement?> _combos = [];
        private readonly List<ReloadNotification> _pendingReloads = [];

        internal ScopeState(FrameworkElement owner)
        {
            _owner = owner;
            owner.AddHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler(Query), true);
            owner.Loaded += Loaded;
            owner.Unloaded += Unloaded;
            if (owner.IsLoaded) HookDescendants(owner);
        }

        private void Loaded(object sender, RoutedEventArgs args)
        {
            HookDescendants(_owner);
        }

        private void HookDescendants(DependencyObject root)
        {
            if (root is ComboBox combo) Hook(combo);
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                HookDescendants(VisualTreeHelper.GetChild(root, index));
        }

        private void Hook(ComboBox combo)
        {
            if (!_combos.TryAdd(combo, null)) return;
            combo.DropDownOpened += Opened;
            combo.DropDownClosed += Closed;
            combo.Unloaded += ComboUnloaded;
        }

        private void Opened(object? sender, EventArgs args)
        {
            if (sender is not ComboBox combo) return;
            DetachPopup(combo);
            if (combo.Template.FindName("PART_Popup", combo) is Popup { Child: FrameworkElement surface })
            {
                surface.AddHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler(Query), true);
                _combos[combo] = surface;
            }
        }

        private void Closed(object? sender, EventArgs args) { if (sender is ComboBox combo) DetachPopup(combo); }
        private void ComboUnloaded(object sender, RoutedEventArgs args)
        {
            if (sender is not ComboBox combo || !_combos.ContainsKey(combo)) return;
            Unhook(combo);
            if (!_owner.IsLoaded) return;
            // Native ancestors can reload content without reloading this scope. Only a weak
            // Loaded notification remains until local return or scope retirement.
            _pendingReloads.Add(new ReloadNotification(this, combo));
        }

        private sealed class ReloadNotification : IDisposable
        {
            private readonly WeakReference<ScopeState> _scope;
            private readonly WeakReference<ComboBox> _combo;
            private bool _disposed;

            internal ReloadNotification(ScopeState scope, ComboBox combo)
            {
                _scope = new(scope);
                _combo = new(combo);
                combo.Loaded += Loaded;
            }

            private void Loaded(object sender, RoutedEventArgs args)
            {
                if (_disposed) return;
                if (!_scope.TryGetTarget(out var scope)) { Dispose(); return; }
                if (!scope._owner.IsLoaded || !ReferenceEquals(scope._owner.GetValue(ScopeStateProperty), scope))
                {
                    Dispose();
                    scope._pendingReloads.Remove(this);
                    return;
                }
                for (DependencyObject? current = (ComboBox)sender; current is not null; current = Parent(current))
                    if (ReferenceEquals(current, scope._owner))
                    {
                        Dispose();
                        scope._pendingReloads.Remove(this);
                        scope.Hook((ComboBox)sender);
                        break;
                    }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (_combo.TryGetTarget(out var combo)) combo.Loaded -= Loaded;
            }
        }
        private void DetachPopup(ComboBox combo)
        {
            if (_combos.TryGetValue(combo, out var surface) && surface is not null)
                surface.RemoveHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler(Query));
            if (_combos.ContainsKey(combo)) _combos[combo] = null;
        }

        private void Unhook(ComboBox combo)
        {
            DetachPopup(combo);
            combo.DropDownOpened -= Opened;
            combo.DropDownClosed -= Closed;
            combo.Unloaded -= ComboUnloaded;
            _combos.Remove(combo);
        }

        private void Unloaded(object sender, RoutedEventArgs args)
        {
            foreach (var combo in _combos.Keys.ToArray()) Unhook(combo);
            foreach (var notification in _pendingReloads) notification.Dispose();
            _pendingReloads.Clear();
        }

        public void Dispose()
        {
            Unloaded(_owner, new RoutedEventArgs());
            _owner.RemoveHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler(Query));
            _owner.Loaded -= Loaded;
            _owner.Unloaded -= Unloaded;
        }
    }
}
