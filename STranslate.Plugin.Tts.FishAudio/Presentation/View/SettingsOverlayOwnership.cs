using System.Windows;
using System.Windows.Media;

namespace STranslate.Plugin.Tts.FishAudio.View;

internal static class SettingsOverlayOwnership
{
    private sealed class Ownership(object token, Action close)
    {
        internal object Token { get; } = token;
        internal Action Close { get; } = close;
    }
    private static readonly DependencyProperty OwnerProperty = DependencyProperty.RegisterAttached(
        "Owner", typeof(Ownership), typeof(SettingsOverlayOwnership));

    internal static void Acquire(FrameworkElement owner, object token, Action close)
    {
        if (owner.GetValue(OwnerProperty) is Ownership previous && !ReferenceEquals(previous.Token, token))
            previous.Close();
        owner.SetValue(OwnerProperty, new Ownership(token, close));
    }

    internal static void Release(FrameworkElement owner, object token)
    {
        if (owner.GetValue(OwnerProperty) is Ownership current && ReferenceEquals(current.Token, token))
            owner.ClearValue(OwnerProperty);
    }

    internal static FrameworkElement FindOwner(FrameworkElement element) =>
        Ancestors(element).OfType<SettingsView>().FirstOrDefault()
        ?? (FrameworkElement?)Window.GetWindow(element)
        ?? Ancestors(element).OfType<FrameworkElement>().LastOrDefault() ?? element;

    internal static bool IsWithin(DependencyObject child, DependencyObject owner) =>
        ReferenceEquals(child, owner) || Ancestors(child).Contains(owner);

    internal static IEnumerable<DependencyObject> Ancestors(DependencyObject child)
    {
        for (var parent = ParentOf(child); parent is not null; parent = ParentOf(parent)) yield return parent;
    }
    private static DependencyObject? ParentOf(DependencyObject child) => child is Visual
        ? VisualTreeHelper.GetParent(child) ?? LogicalTreeHelper.GetParent(child)
        : LogicalTreeHelper.GetParent(child);
}
