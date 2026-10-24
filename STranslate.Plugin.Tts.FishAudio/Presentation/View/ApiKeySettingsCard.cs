using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using iNKORE.UI.WPF.Modern.Controls;

namespace STranslate.Plugin.Tts.FishAudio.View;

/// <summary>Reserves the native header before allocating API Key input space.</summary>
public sealed class ApiKeySettingsCard : SettingsCard
{
    private Grid? _templateRoot;
    private VisualStateGroup? _contentAlignmentStates;

    public ApiKeySettingsCard()
    {
        SetResourceReference(StyleProperty, typeof(SettingsCard));
    }

    public override void OnApplyTemplate()
    {
        var previousState = _contentAlignmentStates?.CurrentState?.Name;
        var root = GetTemplateChild("PART_RootGrid") as Grid;
        if (!ReferenceEquals(root, _templateRoot))
        {
            _templateRoot = root;
            _contentAlignmentStates = GetTemplateChild("PART_RootBorder") is FrameworkElement border
                ? VisualStateManager.GetVisualStateGroups(border).OfType<VisualStateGroup>()
                    .FirstOrDefault(group => group.Name == "ContentAlignmentStates")
                : null;
            var right = _contentAlignmentStates?.States.OfType<VisualState>().FirstOrDefault(state => state.Name == "Right");
            if (right is not null && root is { ColumnDefinitions.Count: >= 3 })
            {
                // Configure this template instance before the native initial
                // state entry. Other states release these animation clocks and
                // recover the native column widths without breakpoint changes.
                var storyboard = right.Storyboard?.Clone() ?? new Storyboard();
                AddColumnWidth(storyboard, root.ColumnDefinitions[1], GridLength.Auto);
                AddColumnWidth(storyboard, root.ColumnDefinitions[2], new GridLength(1, GridUnitType.Star));
                right.Storyboard = storyboard;
            }
        }

        base.OnApplyTemplate();
        // Native code can retain its cached alignment across template changes.
        // Replay only the prior native state if the new group was not entered.
        if (_contentAlignmentStates?.CurrentState is null && previousState is not null)
            VisualStateManager.GoToState(this, previousState, false);
    }

    private static void AddColumnWidth(Storyboard storyboard, ColumnDefinition column, GridLength width)
    {
        var animation = new ObjectAnimationUsingKeyFrames { Duration = TimeSpan.Zero };
        animation.KeyFrames.Add(new DiscreteObjectKeyFrame(width, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        Storyboard.SetTarget(animation, column);
        Storyboard.SetTargetProperty(animation, new PropertyPath(ColumnDefinition.WidthProperty));
        storyboard.Children.Add(animation);
    }
}
