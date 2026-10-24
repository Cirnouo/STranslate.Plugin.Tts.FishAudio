using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using iNKORE.UI.WPF.Modern;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.View;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static ContextProxy;
using static StaTestHost;
using static TestAssertions;

internal static class VoiceLikeSuite
{
    internal static async Task RunAllAsync()
    {
        await SharedCardTemplateIsCompiledAsync();
        await SettingsLayoutSuite.LikePresentationAsync();
        foreach (var locale in UiLocales.All)
        foreach (var dark in new[] { false, true })
        foreach (var width in new[] { 320d, 640d })
            await RunOnStaThreadAsync(() => InspectLikeState(locale, dark, width));
        Console.WriteLine("Like state, seven-locale accessibility, narrow tabs and legacy ID compatibility passed.");
    }

    internal static Task SharedCardTemplateIsCompiledAsync() => RunOnStaThreadAsync(() =>
    {
        using var vm = new SettingsViewModel(CreateContext(), new Settings());
        var item = new VoiceSearchItem { Id = "0123456789abcdef0123456789abcdef", Title = "Compiled card" };
        vm.SearchResults = [item]; vm.ByIdResults = [item]; vm.FavoriteResults = [item]; vm.HasSearched = true;
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        SettingsLayoutSuite.Layout(view);
        var template = (DataTemplate)view.FindResource("VoiceCardTemplate");
        var paths = new[] { nameof(vm.SearchResults), nameof(vm.ByIdResults), nameof(vm.FavoriteResults) };
        var lists = Visuals(view).OfType<ItemsControl>().Where(control => paths.Contains(
            BindingOperations.GetBinding(control, ItemsControl.ItemsSourceProperty)?.Path.Path)).ToArray();
        AssertEqual(3, lists.Length, "The compiled view contains all three browsing lists");
        foreach (var list in lists)
            AssertEqual(true, ReferenceEquals(template, list.ItemTemplate), "Every browsing location resolves the same compiled card template");
        var like = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleFavoriteCommand) && IsShown(button));
        AssertEqual(true, like.Content is System.Windows.Shapes.Path path && ReferenceEquals(view.FindResource("VoiceLikeHeartGeometry"), path.Data),
            "The compiled shared template renders its Like vector from the common heart resource");
    });

    private static void InspectLikeState(string locale, bool dark, double width)
    {
        const string id = "0123456789abcdef0123456789abcdef";
        const string other = "1123456789abcdef0123456789abcdef";
        var directory = CreateTempDirectory();
        var file = Path.Combine(directory, "favorite-voices.json");
        // An existing ordered file from the Favorite presentation is already the Like list.
        var original = JsonSerializer.Serialize(new[] { other, id });
        File.WriteAllText(file, original);
        var settings = new Settings { VoiceId = id, CachedVoice = new() { Title = "Selected" } };
        using var vm = new SettingsViewModel(CreateContext(settings: settings, pluginSettingsDirectoryPath: directory), settings);
        var item = new VoiceSearchItem { Id = id, Title = "Matching result", TaskCount = 12345, IsFavorite = true };
        vm.SearchResults = [item]; vm.ByIdResults = [item]; vm.FavoriteResults = [item]; vm.HasSearched = true;
        var view = SettingsLayoutSuite.CreateThemedView(vm);
        view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.xaml"), UriKind.Absolute) });
        view.Resources.MergedDictionaries.OfType<ThemeResources>().Single().RequestedTheme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ThemeManager.SetRequestedTheme(view, dark ? ElementTheme.Dark : ElementTheme.Light);
        // Explicit headless palette: no Application/OS accent initialization exists on this STA.
        var accent = new SolidColorBrush(dark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8));
        view.Resources["AccentFillColorDefaultBrush"] = accent;
        SettingsLayoutSuite.Layout(view, width);
        var geometry = (Geometry)view.FindResource("VoiceLikeHeartGeometry");
        AssertEqual(false, geometry.FillContains(new Point(12, 3)), "Heart contour has an open notch above its center");
        foreach (var point in new[] { new Point(6, 6), new Point(18, 6), new Point(12, 16) })
            AssertEqual(true, geometry.FillContains(point), "Heart contour retains both lobes and its lower body");
        AssertEqual(original, File.ReadAllText(file), "Rendering existing favorites as likes performs no migration or rewrite");
        var selected = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleSelectedVoiceFavoriteCommand));
        var selectedHeart = (System.Windows.Shapes.Path)selected.Content;
        var (like, unlike) = locale switch
        {
            "zh-cn" => ("喜欢", "取消喜欢"), "zh-tw" => ("喜歡", "取消喜歡"),
            "ja" => ("いいね", "いいねを取り消す"), "ko" => ("좋아요", "좋아요 취소"),
            "ru" => ("Нравится", "Убрать отметку «Нравится»"),
            "tr" => ("Beğen", "Beğenmekten vazgeç"), _ => ("Like", "Unlike"),
        };
        foreach (var mode in new[] { VoiceBrowseMode.Search, VoiceBrowseMode.ById, VoiceBrowseMode.Favorites })
        {
            vm.FavoriteResults = [item]; vm.BrowseMode = mode;
            SettingsLayoutSuite.Layout(view, width);
            var browse = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleFavoriteCommand) && ReferenceEquals(button.CommandParameter, item) && IsShown(button));
            var heart = (System.Windows.Shapes.Path)browse.Content;
            foreach (var button in new[] { selected, browse })
            {
                AssertEqual(32d, button.ActualWidth, "Like keeps its original hit width");
                AssertEqual(32d, button.ActualHeight, "Like keeps its original hit height");
                AssertEqual(true, button.Focusable, "Like retains keyboard reachability");
                AssertEqual(unlike, AutomationProperties.GetName(button), "Persisted likes expose localized Unlike");
                AssertEqual(AutomationProperties.GetName(button), button.ToolTip, "Tooltip and accessible action agree");
                var path = (System.Windows.Shapes.Path)button.Content;
                AssertEqual(true, ReferenceEquals(geometry, path.Data), "All four locations share the same closed heart");
                AssertEqual(18d, path.Width, "Card heart keeps the vector allocation");
                AssertEqual(1.5d, path.StrokeThickness, "Card heart keeps its native stroke width");
                AssertEqual(accent.Color, ((SolidColorBrush)path.Fill).Color, "Liked hearts resolve the theme accent");
                AssertEqual(accent.Color, ((SolidColorBrush)path.Stroke).Color, "Filled heart outline follows the same accent");
            }
            var changedAccent = new SolidColorBrush(Color.FromRgb(0x90, 0x50, 0xB0));
            view.Resources["AccentFillColorDefaultBrush"] = changedAccent;
            SettingsLayoutSuite.Layout(view, width);
            AssertEqual(changedAccent.Color, ((SolidColorBrush)selectedHeart.Fill).Color, "A live accent change updates the selected heart");
            AssertEqual(changedAccent.Color, ((SolidColorBrush)heart.Fill).Color, "A live accent change updates the browse heart");
            view.Resources["AccentFillColorDefaultBrush"] = accent;
            // Avoid metadata requests from mutation refresh while exercising the same real commands.
            vm.BrowseMode = VoiceBrowseMode.Search;
            vm.FavoriteResults = [];
            Click(selected);
            SettingsLayoutSuite.Layout(view, width);
            AssertEqual(false, item.IsFavorite, "Unlike updates matching cards independently of selection");
            AssertEqual(id, settings.VoiceId, "Unlike leaves the applied voice unchanged");
            AssertEnumerableEqual(new[] { other }, JsonSerializer.Deserialize<string[]>(File.ReadAllText(file))!, "Unlike removes only its own persisted ID");
            AssertEqual(Colors.Transparent, ((SolidColorBrush)selectedHeart.Fill).Color, "Unlike restores selected outline");
            AssertEqual(like, AutomationProperties.GetName(selected), "Removed like exposes localized Like");
            vm.FavoriteResults = [item]; vm.BrowseMode = mode;
            SettingsLayoutSuite.Layout(view, width);
            browse = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.ToggleFavoriteCommand) && ReferenceEquals(button.CommandParameter, item) && IsShown(button));
            AssertEqual(like, AutomationProperties.GetName(browse), "Unliked browse card offers Like");
            AssertEqual(Colors.Transparent, ((SolidColorBrush)((System.Windows.Shapes.Path)browse.Content).Fill).Color, "Unliked browse heart remains outlined");
            vm.BrowseMode = VoiceBrowseMode.Search;
            vm.FavoriteResults = [];
            Click(browse);
            SettingsLayoutSuite.Layout(view, width);
            AssertEqual(true, vm.IsSelectedVoiceFavorite, "Browse Like updates the selected facade");
            AssertEnumerableEqual(new[] { other, id }, JsonSerializer.Deserialize<string[]>(File.ReadAllText(file))!, "Like appends the same ID without metadata or order changes");
            AssertEqual(12345L, item.TaskCount, "Like never reinterprets provider popularity");
        }
        using var reloaded = new FavoriteVoiceStore(directory);
        AssertEnumerableEqual(new[] { other, id }, reloaded.Read(), "The unchanged ID-only format survives reopening");
        vm.BrowseMode = VoiceBrowseMode.Favorites;
        SettingsLayoutSuite.Layout(view, width);
        var tab = Visuals(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.SwitchToFavoritesCommand));
        var label = (TextBlock)view.FindName("FavoritesTabLabel");
        var content = (StackPanel)tab.Content;
        AssertEqual(13d, label.FontSize, "Every translated compact caption retains peer metrics");
        AssertEqual(true, content.ActualWidth <= tab.ActualWidth && label.ActualWidth > 0, "Compact Like caption and icon fit at existing narrow widths");
    }

    private static void Click(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(button, null);

    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is FrameworkElement framework; current = VisualTreeHelper.GetParent(current))
            if (framework.Visibility != Visibility.Visible) return false;
        return true;
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var nested in Visuals(child)) yield return nested;
        }
    }
}
