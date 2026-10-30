using NUnit.Framework;
using iNKORE.UI.WPF.Modern.Common;
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Controls.Helpers;

namespace NUnitCases.NativeSettings;

// Source candidate only. The complete profile retains its original cold-first-use order.
[TestFixture]
[NonParallelizable]
[Order(139)]
[Property("FishAudioExecutionProfile", "native-settings")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class InputClearCases
{
    public static IEnumerable<TestCaseData> ConstructionRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.clear/construction",
            139, "native-settings/139-construction");
    }

    [TestCaseSource(nameof(ConstructionRows))]
    [Order(139)]
    public Task CompiledConstruction()
        => InputClearControlSuite.Action("construction", InputClearControlSuite.Construction);

    public static IEnumerable<TestCaseData> GeometryRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/en/light/wide",
            140, "native-settings/140-geometry-en-light-wide", "en", false, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/en/light/narrow",
            141, "native-settings/141-geometry-en-light-narrow", "en", false, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/en/dark/wide",
            142, "native-settings/142-geometry-en-dark-wide", "en", true, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/en/dark/narrow",
            143, "native-settings/143-geometry-en-dark-narrow", "en", true, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/zh-cn/light/wide",
            144, "native-settings/144-geometry-zh-cn-light-wide", "zh-cn", false, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/zh-cn/light/narrow",
            145, "native-settings/145-geometry-zh-cn-light-narrow", "zh-cn", false, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/zh-cn/dark/wide",
            146, "native-settings/146-geometry-zh-cn-dark-wide", "zh-cn", true, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/zh-cn/dark/narrow",
            147, "native-settings/147-geometry-zh-cn-dark-narrow", "zh-cn", true, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/zh-tw/light/wide",
            148, "native-settings/148-geometry-zh-tw-light-wide", "zh-tw", false, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/zh-tw/light/narrow",
            149, "native-settings/149-geometry-zh-tw-light-narrow", "zh-tw", false, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/zh-tw/dark/wide",
            150, "native-settings/150-geometry-zh-tw-dark-wide", "zh-tw", true, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/zh-tw/dark/narrow",
            151, "native-settings/151-geometry-zh-tw-dark-narrow", "zh-tw", true, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/ja/light/wide",
            152, "native-settings/152-geometry-ja-light-wide", "ja", false, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/ja/light/narrow",
            153, "native-settings/153-geometry-ja-light-narrow", "ja", false, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/ja/dark/wide",
            154, "native-settings/154-geometry-ja-dark-wide", "ja", true, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/ja/dark/narrow",
            155, "native-settings/155-geometry-ja-dark-narrow", "ja", true, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/ko/light/wide",
            156, "native-settings/156-geometry-ko-light-wide", "ko", false, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/ko/light/narrow",
            157, "native-settings/157-geometry-ko-light-narrow", "ko", false, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/ko/dark/wide",
            158, "native-settings/158-geometry-ko-dark-wide", "ko", true, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/ko/dark/narrow",
            159, "native-settings/159-geometry-ko-dark-narrow", "ko", true, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/ru/light/wide",
            160, "native-settings/160-geometry-ru-light-wide", "ru", false, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/ru/light/narrow",
            161, "native-settings/161-geometry-ru-light-narrow", "ru", false, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/ru/dark/wide",
            162, "native-settings/162-geometry-ru-dark-wide", "ru", true, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/ru/dark/narrow",
            163, "native-settings/163-geometry-ru-dark-narrow", "ru", true, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/tr/light/wide",
            164, "native-settings/164-geometry-tr-light-wide", "tr", false, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/tr/light/narrow",
            165, "native-settings/165-geometry-tr-light-narrow", "tr", false, true);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/tr/dark/wide",
            166, "native-settings/166-geometry-tr-dark-wide", "tr", true, false);
        yield return NativeTestCaseSource.Row(
            "input.clear/geometry/tr/dark/narrow",
            167, "native-settings/167-geometry-tr-dark-narrow", "tr", true, true);
    }

    [TestCaseSource(nameof(GeometryRows))]
    [Order(140)]
    public Task Geometry(string locale, bool dark, bool narrow)
        => InputClearControlSuite.Action($"{locale}/{dark}/{narrow}",
            stage => InputClearControlSuite.Geometry(locale, dark, narrow, stage));

    public static IEnumerable<TestCaseData> ActivationRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.clear/activation/apikey",
            168, "native-settings/168-activation-apikey", "ApiKey");
        yield return NativeTestCaseSource.Row(
            "input.clear/activation/searchquery",
            169, "native-settings/169-activation-searchquery", "SearchQuery");
        yield return NativeTestCaseSource.Row(
            "input.clear/activation/voiceidinput",
            170, "native-settings/170-activation-voiceidinput", "VoiceIdInput");
    }

    [TestCaseSource(nameof(ActivationRows))]
    [Order(168)]
    public Task Activation(string field)
        => InputClearControlSuite.Action($"activation/{field}", stage => InputClearControlSuite.Activation(field, stage));

    public static IEnumerable<TestCaseData> PasswordCompatibilityRows()
    {
        yield return NativeTestCaseSource.Row(
            "input.clear/password-compatibility/visible",
            171, "native-settings/171-password-compatibility-visible", PasswordRevealMode.Visible);
        yield return NativeTestCaseSource.Row(
            "input.clear/password-compatibility/peek",
            172, "native-settings/172-password-compatibility-peek", PasswordRevealMode.Peek);
    }

    [TestCaseSource(nameof(PasswordCompatibilityRows))]
    [Order(171)]
    public Task PasswordCompatibility(PasswordRevealMode mode)
        => InputClearControlSuite.Action($"forced-password-compatibility/{mode}",
            stage => InputClearControlSuite.PasswordCompatibility(mode, stage));
}
