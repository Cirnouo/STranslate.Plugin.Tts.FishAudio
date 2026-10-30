using NUnit.Framework;

namespace NUnitCases.NativeSettings;

// Source candidate only. The complete profile retains its original cold-first-use order.
[TestFixture]
[NonParallelizable]
[Order(4)]
[Property("FishAudioExecutionProfile", "native-settings")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class ApiKeySettingsCardCases
{
    public static IEnumerable<TestCaseData> WidthMatrixRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/width-saturation/empty",
            4, "native-settings/004-width-saturation-empty", "", null, false);
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/width-saturation/legacy32",
            5, "native-settings/005-width-saturation-legacy32", new string('a', 32), null, false);
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/width-saturation/prefixed51",
            6, "native-settings/006-width-saturation-prefixed51", "sk-fish-" + new string('a', 43), null, false);
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/width-saturation/overlong1000",
            7, "native-settings/007-width-saturation-overlong1000", new string('a', 1000), null, false);
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/controlled-password-metrics/legacy32-font10",
            8, "native-settings/008-controlled-password-metrics-legacy32-font10", new string('a', 32), 10d, true);
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/controlled-password-metrics/prefixed51-font16",
            9, "native-settings/009-controlled-password-metrics-prefixed51-font16", "sk-fish-" + new string('a', 43), 16d, false);
    }

    [TestCaseSource(nameof(WidthMatrixRows))]
    [Order(4)]
    public Task WidthMatrix(string draft, double? passwordFontSize, bool requireWrappedSaturation)
    {
        if (passwordFontSize is null)
            Console.WriteLine($"[api-key-layout] width and saturation matrix, draft length {draft.Length}.");
        else
            Console.WriteLine(passwordFontSize == 10
                ? "[api-key-layout] controlled smaller password metrics."
                : "[api-key-layout] controlled larger password metrics.");
        return StaTestHost.RunOnStaThreadAsync(() =>
            ApiKeySettingsCardSuite.WidthMatrix(draft, passwordFontSize, requireWrappedSaturation));
    }

    public static IEnumerable<TestCaseData> LocalizedHeadersRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/localized-header/en",
            10, "native-settings/010-localized-header-en", "en");
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/localized-header/zh-cn",
            11, "native-settings/011-localized-header-zh-cn", "zh-cn");
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/localized-header/zh-tw",
            12, "native-settings/012-localized-header-zh-tw", "zh-tw");
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/localized-header/ja",
            13, "native-settings/013-localized-header-ja", "ja");
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/localized-header/ko",
            14, "native-settings/014-localized-header-ko", "ko");
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/localized-header/ru",
            15, "native-settings/015-localized-header-ru", "ru");
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/localized-header/tr",
            16, "native-settings/016-localized-header-tr", "tr");
    }

    [TestCaseSource(nameof(LocalizedHeadersRows))]
    [Order(10)]
    public Task LocalizedHeaders(string locale)
    {
        Console.WriteLine($"[api-key-layout] localized header {locale}.");
        return StaTestHost.RunOnStaThreadAsync(() => ApiKeySettingsCardSuite.LocalizedHeaders(locale));
    }

    public static IEnumerable<TestCaseData> TemplateLifecycleRows()
    {
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/template-lifecycle/width-400",
            17, "native-settings/017-template-lifecycle-width-400", 400d);
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/template-lifecycle/width-476",
            18, "native-settings/018-template-lifecycle-width-476", 476d);
        yield return NativeTestCaseSource.Row(
            "settings.api-key-card/template-lifecycle/width-800",
            19, "native-settings/019-template-lifecycle-width-800", 800d);
    }

    [TestCaseSource(nameof(TemplateLifecycleRows))]
    [Order(17)]
    public Task TemplateLifecycle(double width)
    {
        Console.WriteLine($"[api-key-layout] template lifecycle at {width} DIP.");
        return StaTestHost.RunOnStaThreadAsync(() => ApiKeySettingsCardSuite.TemplateLifecycle(width));
    }
}
