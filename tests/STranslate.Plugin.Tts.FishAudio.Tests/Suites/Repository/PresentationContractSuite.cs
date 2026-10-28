using NUnit.Framework;
using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using static AsyncTestWait;
using static ContextProxy;
using static RuntimeOverrideScopes;
using static StaTestHost;
using static TestAssertions;

[TestFixture]
[NonParallelizable]
internal static class PresentationContractSuite
{
    [Test]
    [Property("FishAudioCaseId", "repository.presentation/promo-state-persists-dismissal-and-use")]
    [Order(258)]
    public static void PromoStatePersistsDismissalAndUse()
    {
        var settings = new Settings();
        var context = CreateContext(settings: settings);
        var proxy = (ContextProxy)(object)context;
        var viewModel = new SettingsViewModel(context, settings, nowUtc: FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));

        AssertEqual(true, viewModel.ShowS21ProFreePromo, "Promo should be visible before cutoff when not dismissed");

        viewModel.DismissS21ProFreePromoCommand.Execute(null);
        AssertEqual(true, settings.IsS21ProFreePromoDismissed, "Dismiss promo should persist dismissal");
        AssertEqual(false, viewModel.ShowS21ProFreePromo, "Dismiss promo should hide promo card");

        settings = new Settings { SelectedModel = FishAudioModelPolicy.S1Model };
        context = CreateContext(settings: settings);
        proxy = (ContextProxy)(object)context;
        viewModel = new SettingsViewModel(context, settings, nowUtc: FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));

        viewModel.UseS21ProFreePromoCommand.Execute(null);
        AssertEqual(FishAudioModelPolicy.S21ProFreeModel, settings.SelectedModel, "Using promo should select free model");
        AssertEqual(false, settings.IsS21ProFreePromoDismissed, "Using promo should not persist dismissal");
        AssertEqual(true, viewModel.ShowS21ProFreePromo, "Using promo should leave promo card visible");
        AssertEqual(1, proxy.SaveCount, "Using promo should save the selected model once");

        settings = new Settings();
        viewModel = new SettingsViewModel(CreateContext(settings: settings), settings, nowUtc: FishAudioModelPolicy.FreeModelCutoffUtc);
        AssertEqual(false, viewModel.ShowS21ProFreePromo, "Promo should be hidden at and after cutoff");

        settings = new Settings { SelectedModel = FishAudioModelPolicy.S1Model };
        viewModel = new SettingsViewModel(CreateContext(settings: settings), settings, nowUtc: FishAudioModelPolicy.FreeModelCutoffUtc.AddDays(-1));
        AssertEqual(false, viewModel.IsNormalizeLoudnessEnabled, "Normalize loudness should remain visible but disabled for s1");
        viewModel.SelectedModel = FishAudioModelPolicy.S2ProModel;
        AssertEqual(true, viewModel.IsNormalizeLoudnessEnabled, "Normalize loudness should be enabled for s2-pro and newer models");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.presentation/settings-view-removes-api-key-validation-ui-and-uses-latency-bars")]
    [Order(259)]
    public static void SettingsViewRemovesApiKeyValidationUiAndUsesLatencyBars()
    {
        var xaml = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "Presentation", "View", "SettingsView.xaml")));

        AssertEqual(false, xaml.Contains("ConfirmApiKeyCommand", StringComparison.Ordinal), "Settings view should not contain API Key confirm command");
        AssertEqual(false, xaml.Contains("ApiKeyStatusKind", StringComparison.Ordinal), "Settings view should not contain API Key validation status UI");
        AssertEqual(false, xaml.Contains("ApiKeyStatusText", StringComparison.Ordinal), "Settings view should not bind API Key validation status text");
        AssertEqual(false, xaml.Contains("STranslate_Plugin_Tts_FishAudio_ApiKey_Waiting", StringComparison.Ordinal), "Settings view should not show API Key waiting text");
        AssertEqual(false, xaml.Contains("STranslate_Plugin_Tts_FishAudio_ApiKey_Applied", StringComparison.Ordinal), "Settings view should not show API Key applied text");
        AssertEqual(false, xaml.Contains("<PasswordBox.Style>", StringComparison.Ordinal), "API Key PasswordBox should not override the theme style");
        AssertEqual(true, xaml.Contains("IsEnabled=\"{Binding IsApiKeyInputEnabled}\"", StringComparison.Ordinal), "API Key PasswordBox should bind enabled state without replacing its style");

        var latencyBarsIndex = xaml.IndexOf("x:Name=\"LatencyBars\"", StringComparison.Ordinal);
        AssertEqual(true, latencyBarsIndex >= 0, "Latency display should use the named three-bar icon");
        var latencyTextIndex = xaml.IndexOf("Text=\"{Binding LatencyText}\"", latencyBarsIndex, StringComparison.Ordinal);
        AssertEqual(true, latencyTextIndex > latencyBarsIndex, "Latency bars should appear before latency text");
        var latencyBarsXaml = xaml.Substring(latencyBarsIndex, latencyTextIndex - latencyBarsIndex);
        AssertEqual(3, CountOccurrences(latencyBarsXaml, "<Rectangle"), "Latency icon should contain exactly three rectangles");
        AssertEqual(3, CountOccurrences(latencyBarsXaml, "VerticalAlignment=\"Bottom\""), "All latency rectangles should align to the same bottom baseline");
        AssertEqual(false, latencyBarsXaml.Contains("Segoe MDL2 Assets", StringComparison.Ordinal), "Latency icon should not use the old font glyph");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.presentation/settings-view-shows-persistent-credit-placeholder")]
    [Order(260)]
    public static void SettingsViewShowsPersistentCreditPlaceholder()
    {
        var xaml = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "Presentation", "View", "SettingsView.xaml")));
        const string placeholderKey = "STranslate_Plugin_Tts_FishAudio_Credit_NotLoaded";

        AssertEqual(
            true,
            xaml.Contains($"Text=\"{{DynamicResource {placeholderKey}}}\"", StringComparison.Ordinal),
            "The not-loaded credit state should use a persistent DynamicResource");
        AssertEqual(true, xaml.Contains("Binding=\"{Binding UserCredit}\" Value=\"\"", StringComparison.Ordinal), "The not-loaded placeholder should be driven by empty UserCredit state");
        AssertEqual(true, xaml.Contains("Text=\"{Binding UserCredit}\"", StringComparison.Ordinal), "Loaded credit should keep displaying the balance binding");
        AssertEqual(true, xaml.Contains("Text=\" $\"", StringComparison.Ordinal), "Loaded credit should keep displaying the dollar symbol");

        foreach (var locale in UiLocales.All)
        {
            var localeXaml = File.ReadAllText(FindRepoFile(Path.Combine(
                "STranslate.Plugin.Tts.FishAudio",
                "Languages",
                $"{locale}.xaml")));
            var match = Regex.Match(
                localeXaml,
                $"<sys:String\\s+x:Key=\"{placeholderKey}\">(?<value>[^<]+)</sys:String>",
                RegexOptions.CultureInvariant);
            AssertEqual(true, match.Success, $"{locale}.xaml should define {placeholderKey}");
            AssertEqual(false, string.IsNullOrWhiteSpace(match.Groups["value"].Value), $"{locale}.xaml should provide a complete credit placeholder");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "repository.presentation/settings-view-includes-s21-promo-and-dynamic-model-descriptions")]
    [Order(261)]
    public static void SettingsViewIncludesS21PromoAndDynamicModelDescriptions()
    {
        var xaml = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "Presentation", "View", "SettingsView.xaml")));

        AssertEqual(true, xaml.Contains("s2-pro-free-promo.webp", StringComparison.Ordinal), "Settings view should use the local Fish Audio s2.1 promo image");
        AssertEqual(false, xaml.Contains("media/blog-images/f614a042a9ac407890cad88d69abbf33", StringComparison.Ordinal), "Settings view should not load the promo image from the network");
        var promoImageIndex = xaml.IndexOf("s2-pro-free-promo.webp", StringComparison.Ordinal);
        var promoImageStart = xaml.LastIndexOf("<Image", promoImageIndex, StringComparison.Ordinal);
        var promoImageEnd = xaml.IndexOf("/>", promoImageIndex, StringComparison.Ordinal);
        AssertEqual(true, promoImageStart >= 0 && promoImageEnd > promoImageStart, "Promo image should be an Image element");
        var promoImageXaml = xaml.Substring(promoImageStart, promoImageEnd - promoImageStart);
        AssertEqual(true, promoImageXaml.Contains("Width=\"1024\"", StringComparison.Ordinal), "Promo placeholder should reserve the image width");
        AssertEqual(true, promoImageXaml.Contains("Height=\"540\"", StringComparison.Ordinal), "Promo placeholder should reserve the image height");
        AssertEqual(true, xaml.Contains("Stretch=\"Uniform\"", StringComparison.Ordinal), "Promo placeholder should preserve the image aspect ratio while resizing");
        AssertEqual(true, xaml.Contains("ShowS21ProFreePromo", StringComparison.Ordinal), "Promo visibility should be bound to ViewModel state");
        AssertEqual(true, xaml.Contains("S21ProFreePromoCard_MouseLeftButtonUp", StringComparison.Ordinal), "Promo card should route clicks to view-only code-behind behavior");
        AssertEqual(true, xaml.Contains("DismissS21ProFreePromoCommand", StringComparison.Ordinal), "Promo card should expose a dismissal command");
        AssertEqual(true, xaml.Contains("x:Name=\"SynthesisModelCard\"", StringComparison.Ordinal), "Synthesis model card should be named for scrolling/highlight behavior");
        AssertEqual(false, xaml.Contains("Visibility=\"{Binding ShowNormalizeLoudness", StringComparison.Ordinal), "Normalize loudness card should not be hidden for s1");
        AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Engine_Description_Free", StringComparison.Ordinal), "Model description should have a free-period DynamicResource");
        AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Engine_Description_Paid", StringComparison.Ordinal), "Model description should have a post-cutoff DynamicResource");
        AssertEqual(false, xaml.Contains("免费限时", StringComparison.Ordinal), "Persistent promo/model text should not be hard-coded in XAML");

        var project = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "STranslate.Plugin.Tts.FishAudio.csproj")));
        AssertEqual(true, project.Contains("<Resource Include=\"s2-pro-free-promo.webp\" />", StringComparison.Ordinal), "Project should embed the local promo image as a WPF resource");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.presentation/language-resources-match-api-key-rollback")]
    [Order(262)]
    public static void LanguageResourcesMatchApiKeyRollback()
    {
        foreach (var locale in UiLocales.All)
        {
            var xaml = File.ReadAllText(FindRepoFile(Path.Combine(
                "STranslate.Plugin.Tts.FishAudio",
                "Languages",
                $"{locale}.xaml")));

            AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Network_Unavailable", StringComparison.Ordinal), $"{locale} should define network unavailable text");
            AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Request_Timeout", StringComparison.Ordinal), $"{locale} should define timeout text");
            AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Engine_Description_Free", StringComparison.Ordinal), $"{locale} should define free-period model description");
            AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Engine_Description_Paid", StringComparison.Ordinal), $"{locale} should define paid-period model description");
            AssertEqual(true, xaml.Contains("STranslate_Plugin_Tts_FishAudio_Promo_Close", StringComparison.Ordinal), $"{locale} should define promo close tooltip");
            AssertEqual(false, xaml.Contains("STranslate_Plugin_Tts_FishAudio_ApiKey_NotVerified", StringComparison.Ordinal), $"{locale} should remove API Key not-verified text");
            AssertEqual(false, xaml.Contains("STranslate_Plugin_Tts_FishAudio_ApiKey_Waiting", StringComparison.Ordinal), $"{locale} should remove API Key waiting text");
            AssertEqual(false, xaml.Contains("STranslate_Plugin_Tts_FishAudio_ApiKey_Applied", StringComparison.Ordinal), $"{locale} should remove API Key applied text");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "repository.presentation/preview-failure-language-resources-are-complete")]
    [Order(263)]
    public static void PreviewFailureLanguageResourcesAreComplete()
    {
        var keys = new[]
        {
            "STranslate_Plugin_Tts_FishAudio_Preview_Unavailable",
            "STranslate_Plugin_Tts_FishAudio_Preview_PlaybackFailed",
            "STranslate_Plugin_Tts_FishAudio_Preview_RefreshFailed",
        };

        foreach (var locale in UiLocales.All)
        {
            var xaml = File.ReadAllText(FindRepoFile(Path.Combine("STranslate.Plugin.Tts.FishAudio", "Languages", $"{locale}.xaml")));
            foreach (var key in keys)
            {
                var match = Regex.Match(
                    xaml,
                    $"<sys:String\\s+x:Key=\"{Regex.Escape(key)}\">(?<value>[^<]+)</sys:String>",
                    RegexOptions.CultureInvariant);
                AssertEqual(true, match.Success, $"{locale}.xaml should define {key}");
                AssertEqual(false, string.IsNullOrWhiteSpace(match.Groups["value"].Value), $"{locale}.xaml should provide non-empty text for {key}");
            }
        }
    }

    [Test]
    [Property("FishAudioCaseId", "repository.presentation/language-resources-describe-context-conditioning-consistency")]
    [Order(264)]
    public static void LanguageResourcesDescribeContextConditioningConsistency()
    {
        var expectedDescriptions = new Dictionary<string, string>
        {
            ["zh-cn"] = "使用同一次合成音频的前序片段保持声音一致性，不会使用之前生成的其他音频",
            ["zh-tw"] = "使用同一次合成音訊的前序片段保持聲音一致性，不會使用先前產生的其他音訊",
            ["en"] = "Uses earlier chunks from the same synthesis to maintain voice consistency; it does not reference previously generated audio",
            ["ja"] = "同じ合成音声内の前のチャンクを使って声の一貫性を保ち、以前に生成した別の音声は参照しません",
            ["ko"] = "같은 합성 오디오 안의 앞선 조각을 사용해 보이스 일관성을 유지하며, 이전에 생성한 다른 오디오는 참조하지 않습니다",
            ["ru"] = "Использует предыдущие фрагменты этого же синтеза для согласованности голоса; аудио, созданное ранее в других запросах, не используется",
            ["tr"] = "Ses tutarlılığı için aynı sentezin önceki parçalarını kullanır; önceki isteklerde üretilen başka sesleri kullanmaz",
        };

        foreach (var (locale, expectedDescription) in expectedDescriptions)
        {
            var xaml = File.ReadAllText(FindRepoFile(Path.Combine(
                "STranslate.Plugin.Tts.FishAudio",
                "Languages",
                $"{locale}.xaml")));

            AssertEqual(
                true,
                xaml.Contains(expectedDescription, StringComparison.Ordinal),
                $"{locale} should describe context conditioning as same-synthesis voice consistency");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "repository.presentation/translated-readmes-match-current-source-and-control-names")]
    [Order(265)]
    public static void TranslatedReadmesMatchCurrentSourceAndControlNames()
    {
        var documentationLocales = new Dictionary<string, string>
        {
            ["README.en.md"] = "en",
            ["README.zh-TW.md"] = "zh-tw",
            ["README.ja.md"] = "ja",
            ["README.ko.md"] = "ko",
        };

        foreach (var (fileName, locale) in documentationLocales)
        {
            var readme = File.ReadAllText(FindRepoFile(fileName));

            AssertEqual(false, readme.Contains("s2-pro-free-promo.webp", StringComparison.Ordinal), $"{fileName} should not reference the removed README promo image");
            AssertEqual(true, readme.Contains("> [!TIP]", StringComparison.Ordinal), $"{fileName} should use the source README tip callout");
            AssertEqual(false, readme.Contains("[!NOTE]", StringComparison.Ordinal), $"{fileName} should remove the random voice note from section 2.5");
            var guide = File.ReadAllText(FindRepoFile(Path.Combine("docs", "user-guide", fileName)));
            var resources = System.Xml.Linq.XDocument.Load(FindRepoFile(Path.Combine(
                "STranslate.Plugin.Tts.FishAudio", "Languages", $"{locale}.xaml")));
            var keyAttribute = System.Xml.Linq.XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
            var controlName = resources.Root!.Elements().Single(element =>
                (string?)element.Attribute(keyAttribute) == "STranslate_Plugin_Tts_FishAudio_ConditionOnPreviousChunks").Value;
            AssertEqual(true, guide.Contains(controlName, StringComparison.Ordinal),
                $"{fileName} user guide should use the localized context conditioning control name");
        }

        var koreanReadme = File.ReadAllText(FindRepoFile(Path.Combine("docs", "user-guide", "README.ko.md")));
        AssertEqual(true, koreanReadme.Contains("컨텍스트 연동", StringComparison.Ordinal), "Korean user guide should use the localized control name for context conditioning");
        AssertEqual(false, koreanReadme.Contains("컨텍스트 연결", StringComparison.Ordinal), "Korean user guide should not use a different context conditioning label");
    }

}
