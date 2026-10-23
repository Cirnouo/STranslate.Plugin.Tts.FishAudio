using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows;

namespace STranslate.Plugin.Tts.FishAudio.View;

public sealed class LanguageFlagImage : Image
{
    protected override AutomationPeer OnCreateAutomationPeer() => null!;
}

public sealed class LanguageMoreButton : Button
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(nameof(IsExpanded),
        typeof(bool), typeof(LanguageMoreButton), new PropertyMetadata(false, (sender, args) =>
            ((LanguageMoreButton)sender).NotifyExpanded((bool)args.OldValue, (bool)args.NewValue)));
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }
    internal VoiceLanguageTags? TagsOwner { get; set; }
    protected override AutomationPeer OnCreateAutomationPeer() => new MorePeer(this);

    internal void NotifyExpanded(bool oldValue, bool newValue)
    {
        if (UIElementAutomationPeer.FromElement(this) is MorePeer peer)
            peer.RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                oldValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed,
                newValue ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
    }

    private sealed class MorePeer(LanguageMoreButton owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);
        public ExpandCollapseState ExpandCollapseState => owner.TagsOwner?.IsPopupOpen == true
            ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public void Expand() => owner.TagsOwner?.OpenPopup();
        public void Collapse() => owner.TagsOwner?.ClosePopup();
    }
}
