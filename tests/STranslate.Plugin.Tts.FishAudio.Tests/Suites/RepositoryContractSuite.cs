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

internal static class RepositoryContractSuite
{
    internal static void Release110MetadataIsComplete()
    {
        var pluginJson = File.ReadAllText(FindRepoFile(Path.Combine(
            "STranslate.Plugin.Tts.FishAudio",
            "plugin.json")));
        using var pluginDocument = JsonDocument.Parse(pluginJson);
        AssertEqual(
            "1.1.0",
            pluginDocument.RootElement.GetProperty("Version").GetString(),
            "Release plugin version should be 1.1.0");

        var changelog = File.ReadAllText(FindRepoFile("CHANGELOG.md"));
        var unreleasedIndex = changelog.IndexOf("## [Unreleased]", StringComparison.Ordinal);
        var releaseIndex = changelog.IndexOf("## [1.1.0] - 2026-07-24", StringComparison.Ordinal);
        var previousReleaseIndex = changelog.IndexOf("## [1.0.5]", StringComparison.Ordinal);
        AssertEqual(true, unreleasedIndex >= 0, "Changelog should retain the Unreleased heading");
        AssertEqual(true, releaseIndex > unreleasedIndex, "Changelog should contain the dated 1.1.0 release heading after Unreleased");
        AssertEqual(true, previousReleaseIndex > releaseIndex, "Changelog should place 1.1.0 before 1.0.5");
        var unreleasedContent = changelog[unreleasedIndex..releaseIndex];
        AssertEqual(
            true,
            unreleasedContent.Contains("精简 README 构建说明和免费期提示", StringComparison.Ordinal),
            "Post-release README cleanup should remain under Unreleased");

        var releaseContent = changelog[releaseIndex..previousReleaseIndex];
        AssertEqual(
            false,
            releaseContent.Contains("精简 README 构建说明和免费期提示", StringComparison.Ordinal),
            "Post-release README cleanup should not be added retroactively to 1.1.0");
        foreach (var expectedChange in new[]
                 {
                     "SchemaVersion=1",
                     "从 2026-07-24 延长至 2026-08-31",
                     "已选声音与搜索结果试听",
                     "30 秒刷新窗口",
                     "不再回退旧 URL",
                     "启动",
                     "余额行",
                     "试听音频 URL",
                     "封面缓存",
                     "15 秒超时",
                     "SettingsViewModel",
                     "原 AudioPlayer 完成",
                     "不会使用或影响新周期",
                     "Configuration、FishAudio、Lifecycle、Caching、Runtime 和 Presentation",
                     @".\build.ps1 -Release -Clean -Test",
                 })
        {
            AssertEqual(
                true,
                releaseContent.Contains(expectedChange, StringComparison.Ordinal),
                $"1.1.0 changelog should record net change: {expectedChange}");
        }
        AssertEqual(
            false,
            releaseContent.Contains("不会播放", StringComparison.Ordinal),
            "1.1.0 changelog should not claim that an old TTS operation cannot play after reinitialization");

        var designDecisions = File.ReadAllText(FindRepoFile(Path.Combine("docs", "DESIGN_DECISIONS.md")));
        for (var decisionNumber = 36; decisionNumber <= 40; decisionNumber++)
        {
            AssertEqual(
                true,
                designDecisions.Contains($"## DD-{decisionNumber:D3}:", StringComparison.Ordinal),
                $"Design decisions should contain DD-{decisionNumber:D3}");
        }

        var dd031Index = designDecisions.IndexOf("## DD-031:", StringComparison.Ordinal);
        var dd032Index = designDecisions.IndexOf("## DD-032:", StringComparison.Ordinal);
        var dd036Index = designDecisions.IndexOf("## DD-036:", StringComparison.Ordinal);
        var dd037Index = designDecisions.IndexOf("## DD-037:", StringComparison.Ordinal);
        var dd031Content = designDecisions[dd031Index..dd032Index];
        var dd036Content = designDecisions[dd036Index..dd037Index];
        AssertEqual(
            true,
            dd031Content.Contains("Free-model cutoff extended by DD-036", StringComparison.Ordinal),
            "DD-031 should identify DD-036 as the active cutoff extension");
        AssertEqual(
            true,
            dd036Content.Contains("2026-09-01T00:00:00Z", StringComparison.Ordinal),
            "DD-036 should own the extended free-model cutoff");

        string[]? expectedResourceKeys = null;
        foreach (var locale in new[] { "zh-cn", "zh-tw", "en", "ja", "ko" })
        {
            var xaml = File.ReadAllText(FindRepoFile(Path.Combine(
                "STranslate.Plugin.Tts.FishAudio",
                "Languages",
                $"{locale}.xaml")));
            var resourceKeys = Regex.Matches(
                    xaml,
                    "x:Key=\"(?<key>STranslate_Plugin_Tts_FishAudio_[^\"]+)\"",
                    RegexOptions.CultureInvariant)
                .Select(match => match.Groups["key"].Value)
                .Order(StringComparer.Ordinal)
                .ToArray();

            expectedResourceKeys ??= resourceKeys;
            AssertEqual(
                true,
                expectedResourceKeys.SequenceEqual(resourceKeys, StringComparer.Ordinal),
                $"{locale}.xaml should define the same Fish Audio resource keys as zh-cn.xaml");
        }
    }

    internal static void PackageReferenceUsesSdk1012()
    {
        var project = File.ReadAllText(FindRepoFile(Path.Combine(
            "STranslate.Plugin.Tts.FishAudio",
            "STranslate.Plugin.Tts.FishAudio.csproj")));

        AssertEqual(
            true,
            project.Contains("<PackageReference Include=\"STranslate.Plugin\" Version=\"1.0.12\" />", StringComparison.Ordinal),
            "Plugin project should reference STranslate.Plugin SDK 1.0.12");
    }

    internal static void ReleaseOutputIsIsolatedFromDebugArtifacts()
    {
        var project = File.ReadAllText(FindRepoFile(Path.Combine(
            "STranslate.Plugin.Tts.FishAudio",
            "STranslate.Plugin.Tts.FishAudio.csproj")));

        AssertEqual(
            true,
            project.Contains("<OutputPath>..\\.artifacts\\Release\\</OutputPath>", StringComparison.Ordinal),
            "Release output should use its own directory so automatic packaging cannot include stale Debug artifacts");
    }

    internal static void ReleaseBuildUsesSingleReleaseSwitch()
    {
        var buildScript = File.ReadAllText(FindRepoFile("build.ps1"));
        AssertEqual(
            true,
            buildScript.Contains("[switch]$Release", StringComparison.Ordinal),
            "Build script should expose a -Release switch");
        AssertEqual(
            false,
            buildScript.Contains("[string]$Configuration", StringComparison.Ordinal),
            "Build script should not expose the removed -Configuration parameter");

        var workflow = File.ReadAllText(FindRepoFile(Path.Combine(".github", "workflows", "dotnet.yml")));
        const string recommendedCommand = @".\build.ps1 -Release -Clean -Test";
        AssertEqual(
            true,
            workflow.Contains($"run: {recommendedCommand}", StringComparison.Ordinal),
            "Release workflow should use the recommended -Release build command");
        AssertEqual(
            false,
            workflow.Contains("-Configuration", StringComparison.Ordinal),
            "Release workflow should not use the removed -Configuration parameter");

        foreach (var relativePath in new[]
                 {
                     "README.md",
                     Path.Combine("docs", "README_EN.md"),
                     Path.Combine("docs", "README_TW.md"),
                     Path.Combine("docs", "README_JA.md"),
                     Path.Combine("docs", "README_KO.md"),
                 })
        {
            var readme = File.ReadAllText(FindRepoFile(relativePath));
            AssertEqual(
                true,
                readme.Contains(recommendedCommand, StringComparison.Ordinal),
                $"{relativePath} should document the recommended Release build command");
            AssertEqual(
                false,
                readme.Contains("-Configuration", StringComparison.Ordinal),
                $"{relativePath} should not document the removed -Configuration parameter");
            AssertEqual(
                false,
                readme.Contains(".artifacts", StringComparison.Ordinal),
                $"{relativePath} should not describe internal build-cleanup directories");
        }

        foreach (var relativePath in new[]
                 {
                     "README.md",
                     Path.Combine("docs", "README_TW.md"),
                 })
        {
            var readme = File.ReadAllText(FindRepoFile(relativePath));
            AssertEqual(
                false,
                readme.Contains("（UTC 全日可用）", StringComparison.Ordinal),
                $"{relativePath} should omit the verbose UTC all-day note from the free-model tip");
        }
    }

    internal static void FreeModelDeadlineDocumentationIsConsistent()
    {
        const string oldFreeDate = "2026-07-24";
        const string lastFreeDate = "2026-08-31";

        foreach (var relativePath in new[]
                 {
                     "README.md",
                     Path.Combine("docs", "README_EN.md"),
                     Path.Combine("docs", "README_TW.md"),
                     Path.Combine("docs", "README_JA.md"),
                     Path.Combine("docs", "README_KO.md"),
                 })
        {
            var readme = File.ReadAllText(FindRepoFile(relativePath));
            AssertEqual(true, readme.Contains(lastFreeDate, StringComparison.Ordinal), $"{relativePath} should use the August 31 free-model deadline");
            AssertEqual(false, readme.Contains(oldFreeDate, StringComparison.Ordinal), $"{relativePath} should not retain the old July 24 free-model deadline");
        }

        foreach (var locale in new[] { "zh-cn", "zh-tw", "en", "ja", "ko" })
        {
            var xaml = File.ReadAllText(FindRepoFile(Path.Combine(
                "STranslate.Plugin.Tts.FishAudio",
                "Languages",
                $"{locale}.xaml")));
            var freeDescriptionMatch = Regex.Match(
                xaml,
                "<sys:String x:Key=\"STranslate_Plugin_Tts_FishAudio_Engine_Description_Free\">(?<text>.*?)</sys:String>",
                RegexOptions.CultureInvariant);

            AssertEqual(true, freeDescriptionMatch.Success, $"{locale} should define the free-model description");
            var freeDescription = freeDescriptionMatch.Groups["text"].Value;
            AssertEqual(true, freeDescription.Contains(lastFreeDate, StringComparison.Ordinal), $"{locale} free-model description should use the August 31 deadline");
            AssertEqual(true, freeDescription.Contains("UTC", StringComparison.Ordinal), $"{locale} free-model description should identify the deadline as UTC");
            AssertEqual(false, freeDescription.Contains(oldFreeDate, StringComparison.Ordinal), $"{locale} free-model description should not retain the old July 24 deadline");
        }

        foreach (var relativePath in new[]
                 {
                     Path.Combine("docs", "api-tts.md"),
                     Path.Combine("docs", "DESIGN_DECISIONS.md"),
                 })
        {
            var documentation = File.ReadAllText(FindRepoFile(relativePath));
            AssertEqual(true, documentation.Contains(lastFreeDate, StringComparison.Ordinal), $"{relativePath} should use the August 31 free-model deadline");
            AssertEqual(true, documentation.Contains("UTC", StringComparison.Ordinal), $"{relativePath} should identify the free-model deadline as UTC");
        }

        var changelog = File.ReadAllText(FindRepoFile("CHANGELOG.md"));
        AssertEqual(
            true,
            changelog.Contains("从 2026-07-24 延长至 2026-08-31", StringComparison.Ordinal),
            "1.1.0 changelog should explicitly record both the old and extended free-model deadlines");
    }
}
