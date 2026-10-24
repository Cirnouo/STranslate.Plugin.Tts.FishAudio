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
    internal static void UiLocalesHaveCompleteResourcesAndMetadata()
    {
        const string prefix = "STranslate_Plugin_Tts_FishAudio_";
        var keyName = System.Xml.Linq.XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
        Dictionary<string, string> Load(string locale)
        {
            var root = System.Xml.Linq.XDocument.Load(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.xaml")).Root!;
            var entries = root.Elements().ToArray();
            AssertEqual(107, entries.Length, $"{locale} must have all 107 own resources without fallback");
            AssertEqual(true, entries.All(entry => entry.Name.LocalName == "String"
                && ((string?)entry.Attribute(keyName))?.StartsWith(prefix, StringComparison.Ordinal) == true
                && !string.IsNullOrWhiteSpace(entry.Value)), $"{locale} must have nonempty keyed string resources");
            AssertEqual(entries.Length, entries.Select(entry => (string)entry.Attribute(keyName)!).Distinct(StringComparer.Ordinal).Count(),
                $"{locale} must not have duplicate resource keys");
            return entries.ToDictionary(entry => (string)entry.Attribute(keyName)!, entry => entry.Value, StringComparer.Ordinal);
        }

        var english = Load("en");
        string[] protectedTokens = ["S2-Pro", "s2.1-pro-free", "s2.1-pro", "S2", "Fish Audio", "API", "ID", "MP3", "kbps", "dB", "80+", "300", "2026-11-30", "UTC"];
        foreach (var locale in UiLocales.All)
        {
            var resources = Load(locale);
            AssertEqual(locale, resources[prefix + "Languages_UiLocale"].ToLowerInvariant(),
                "Each UI dictionary must explicitly identify its language-name culture");
            AssertEqual(true, english.Keys.Order(StringComparer.Ordinal).SequenceEqual(resources.Keys.Order(StringComparer.Ordinal)),
                $"{locale} must define exactly the English key set");
            using var metadata = JsonDocument.Parse(File.ReadAllText(FindRepoFile($"STranslate.Plugin.Tts.FishAudio/Languages/{locale}.json")));
            var properties = metadata.RootElement.EnumerateObject().ToArray();
            AssertEqual(true, properties.Select(property => property.Name).Order(StringComparer.Ordinal).SequenceEqual(new[] { "Description", "Name" }),
                $"{locale} metadata must contain only one Name and Description");
            AssertEqual(true, properties.All(property => property.Value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(property.Value.GetString())), $"{locale} metadata must contain nonempty strings");
            AssertEqual("Fish Audio TTS", metadata.RootElement.GetProperty("Name").GetString(), "The plugin brand must remain unchanged");
            // The two added translations retain every source technical token and formatting boundary.
            if (locale is not ("ru" or "tr"))
                continue;
            foreach (var (key, value) in resources)
            {
                var source = english[key];
                foreach (var token in protectedTokens)
                    AssertEqual(Regex.Matches(source, Regex.Escape(token)).Count, Regex.Matches(value, Regex.Escape(token)).Count,
                        $"{locale}/{key} must preserve {token}");
                const string formatting = @"\{\d+[^}]*\}|%[sdif]|https?://\S+|\r?\n";
                AssertEqual(true, Regex.Matches(source, formatting).Select(match => match.Value)
                    .SequenceEqual(Regex.Matches(value, formatting).Select(match => match.Value)),
                    $"{locale}/{key} must preserve placeholders, URLs and newlines");
                AssertEqual(Regex.Match(source, @"^\s*").Value, Regex.Match(value, @"^\s*").Value, $"{locale}/{key} leading boundary");
                AssertEqual(Regex.Match(source, @"\s*$").Value, Regex.Match(value, @"\s*$").Value, $"{locale}/{key} trailing boundary");
            }
        }
    }

    internal static void Release112MetadataIsComplete()
    {
        var pluginJson = File.ReadAllText(FindRepoFile(Path.Combine(
            "STranslate.Plugin.Tts.FishAudio",
            "plugin.json")));
        using var pluginDocument = JsonDocument.Parse(pluginJson);
        AssertEqual(
            "1.1.2",
            pluginDocument.RootElement.GetProperty("Version").GetString(),
            "Release plugin version should be 1.1.2");

        var changelog = File.ReadAllText(FindRepoFile("CHANGELOG.md"));
        var unreleasedIndex = changelog.IndexOf("## [Unreleased]", StringComparison.Ordinal);
        var release112Index = changelog.IndexOf("## [1.1.2] - 2026-10-01", StringComparison.Ordinal);
        var release111Index = changelog.IndexOf("## [1.1.1] - 2026-09-01", StringComparison.Ordinal);
        AssertEqual(true, unreleasedIndex >= 0, "Changelog should retain the Unreleased heading");
        AssertEqual(true, release112Index > unreleasedIndex, "Changelog should contain the dated 1.1.2 release heading after Unreleased");
        AssertEqual(true, release111Index > release112Index, "Changelog should place 1.1.2 before 1.1.1");

        var releaseContent = changelog[release112Index..release111Index];
        foreach (var expectedChange in new[]
                 {
                     "### Fixed",
                     "sk-fish-",
                     "43",
                     "32 位小写十六进制",
                     "预检",
                     "https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/issues/1",
                 })
        {
            AssertEqual(true, releaseContent.Contains(expectedChange, StringComparison.Ordinal),
                $"1.1.2 changelog should record the compatibility fix: {expectedChange}");
        }

        var designDecision = File.ReadAllText(FindRepoFile(Path.Combine("docs", "adr", "current", "DD-046.md")));
        AssertEqual(true, designDecision.Contains("# DD-046:", StringComparison.Ordinal),
            "Design decisions should document dual-format API Key preflight");
    }

    internal static void Release111MetadataIsComplete()
    {
        var changelog = File.ReadAllText(FindRepoFile("CHANGELOG.md"));
        var unreleasedIndex = changelog.IndexOf("## [Unreleased]", StringComparison.Ordinal);
        var release111Index = changelog.IndexOf("## [1.1.1] - 2026-09-01", StringComparison.Ordinal);
        var release110Index = changelog.IndexOf("## [1.1.0] - 2026-07-24", StringComparison.Ordinal);
        AssertEqual(true, unreleasedIndex >= 0, "Changelog should retain the Unreleased heading");
        AssertEqual(true, release111Index > unreleasedIndex, "Changelog should contain the dated 1.1.1 release heading after Unreleased");
        AssertEqual(true, release110Index > release111Index, "Changelog should place 1.1.1 before 1.1.0");

        var releaseContent = changelog[release111Index..release110Index];
        foreach (var expectedChange in new[]
                 {
                     "从 2026-08-31 延长至 2026-11-30",
                     "2026-12-01T00:00:00Z",
                     "精简 README 构建说明和免费期提示",
                 })
        {
            AssertEqual(
                true,
                releaseContent.Contains(expectedChange, StringComparison.Ordinal),
                $"1.1.1 changelog should record net change: {expectedChange}");
        }

        foreach (var decisionNumber in new[] { 44, 45 })
        {
            var decision = File.ReadAllText(FindRepoFile(Path.Combine(
                "docs", "adr", "current", $"DD-{decisionNumber:D3}.md")));
            AssertEqual(
                true,
                decision.Contains($"# DD-{decisionNumber:D3}:", StringComparison.Ordinal),
                $"Current design decisions should contain DD-{decisionNumber:D3}");
        }

        var dd045Content = File.ReadAllText(FindRepoFile(Path.Combine("docs", "adr", "current", "DD-045.md")));
        AssertEqual(true, dd045Content.Contains("2026-12-01T00:00:00Z", StringComparison.Ordinal), "DD-045 should own the current free-model cutoff");
        AssertEqual(true, dd045Content.Contains("2026-11-30", StringComparison.Ordinal), "DD-045 should identify the last free UTC day");
        AssertEqual(true, dd045Content.Contains("DD-036", StringComparison.Ordinal), "DD-045 should identify the superseded cutoff decision");
    }

    internal static void Release110MetadataIsComplete()
    {
        var changelog = File.ReadAllText(FindRepoFile("CHANGELOG.md"));
        var releaseIndex = changelog.IndexOf("## [1.1.0] - 2026-07-24", StringComparison.Ordinal);
        var previousReleaseIndex = changelog.IndexOf("## [1.0.5]", StringComparison.Ordinal);
        AssertEqual(true, releaseIndex >= 0, "Changelog should retain the dated 1.1.0 release heading");
        AssertEqual(true, previousReleaseIndex > releaseIndex, "Changelog should place 1.1.0 before 1.0.5");

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

        for (var decisionNumber = 36; decisionNumber <= 40; decisionNumber++)
        {
            var decisionFolder = decisionNumber is 36 or 37 ? "history" : "current";
            var decision = File.ReadAllText(FindRepoFile(Path.Combine(
                "docs", "adr", decisionFolder, $"DD-{decisionNumber:D3}.md")));
            AssertEqual(
                true,
                decision.Contains($"# DD-{decisionNumber:D3}:", StringComparison.Ordinal),
                $"Design decisions should retain DD-{decisionNumber:D3} after migration");
        }

        var dd031Content = File.ReadAllText(FindRepoFile(Path.Combine("docs", "adr", "current", "DD-031.md")));
        var dd036Content = File.ReadAllText(FindRepoFile(Path.Combine("docs", "adr", "history", "DD-036.md")));
        AssertEqual(
            true,
            dd031Content.Contains("DD-045", StringComparison.Ordinal),
            "DD-031 should identify DD-045 as the latest cutoff extension");
        AssertEqual(
            true,
            dd036Content.Contains("2026-09-01T00:00:00Z", StringComparison.Ordinal),
            "Historical DD-036 should retain its superseded free-model cutoff");

        string[]? expectedResourceKeys = null;
        foreach (var locale in UiLocales.All)
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

    internal static void PackageReferenceUsesSdk1015()
    {
        var project = File.ReadAllText(FindRepoFile(Path.Combine(
            "STranslate.Plugin.Tts.FishAudio",
            "STranslate.Plugin.Tts.FishAudio.csproj")));

        AssertEqual(
            true,
            project.Contains("<PackageReference Include=\"STranslate.Plugin\" Version=\"1.0.15\" />", StringComparison.Ordinal),
            "Plugin project should reference STranslate.Plugin SDK 1.0.15");
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
        var validationWorkflow = File.ReadAllText(FindRepoFile(Path.Combine(".github", "workflows", "validation.yml")));
        const string recommendedCommand = @".\build.ps1 -Release -Clean -Test";
        AssertEqual(
            true,
            workflow.Contains("uses: ./.github/workflows/validation.yml", StringComparison.Ordinal),
            "Release workflow should call the complete shared validation before publication");
        AssertEqual(
            true,
            validationWorkflow.Contains($"run: {recommendedCommand}", StringComparison.Ordinal),
            "Shared release validation should use the recommended -Release build command");
        AssertEqual(
            false,
            (workflow + validationWorkflow).Contains("-Configuration", StringComparison.Ordinal),
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
        const string staleFreeDate = "2026-08-31";
        const string lastFreeDate = "2026-11-30";
        const string cutoffDate = "2026-12-01";

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
            AssertEqual(true, readme.Contains(lastFreeDate, StringComparison.Ordinal), $"{relativePath} should use the November 30 free-model deadline");
            var guidePath = Path.Combine("docs", "user-guide", Path.GetFileName(relativePath));
            var guide = File.ReadAllText(FindRepoFile(guidePath));
            // Guides use readable local dates; overviews keep their ISO date.
            var guideCutoff = Path.GetFileName(relativePath) switch
            {
                "README_EN.md" => "1 December 2026",
                "README_JA.md" => "2026年12月1日",
                "README_KO.md" => "2026년 12월 1일",
                _ => cutoffDate,
            };
            AssertEqual(true, guide.Contains(guideCutoff, StringComparison.Ordinal), $"{guidePath} should identify the December 1 cutoff");
            AssertEqual(false, readme.Contains(staleFreeDate, StringComparison.Ordinal), $"{relativePath} should not retain the August 31 free-model deadline");
        }

        foreach (var locale in UiLocales.All)
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
            AssertEqual(true, freeDescription.Contains(lastFreeDate, StringComparison.Ordinal), $"{locale} free-model description should use the November 30 deadline");
            AssertEqual(true, freeDescription.Contains("UTC", StringComparison.Ordinal), $"{locale} free-model description should identify the deadline as UTC");
            AssertEqual(false, freeDescription.Contains(staleFreeDate, StringComparison.Ordinal), $"{locale} free-model description should not retain the August 31 deadline");
        }

        var apiDocumentation = File.ReadAllText(FindRepoFile(Path.Combine("docs", "references", "engine-catalog.md")));
        AssertEqual(true, apiDocumentation.Contains(lastFreeDate, StringComparison.Ordinal), "TTS API documentation should use the November 30 free-model deadline");
        AssertEqual(true, apiDocumentation.Contains($"{cutoffDate}T00:00:00Z", StringComparison.Ordinal), "TTS API documentation should use the exact December 1 UTC cutoff");
        AssertEqual(false, apiDocumentation.Contains(staleFreeDate, StringComparison.Ordinal), "TTS API documentation should not retain the August 31 free-model deadline");

        var changelog = File.ReadAllText(FindRepoFile("CHANGELOG.md"));
        AssertEqual(
            true,
            changelog.Contains("从 2026-08-31 延长至 2026-11-30", StringComparison.Ordinal),
            "1.1.1 changelog should explicitly record both the old and extended free-model deadlines");
    }
}
