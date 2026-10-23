using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace STranslate.Plugin.Tts.FishAudio.View;

internal sealed record VoiceLanguageLabel(string Code, string Name, string? Country, ImageSource Flag);

internal static class VoiceLanguagePresentation
{
    private static readonly HashSet<string> KnownCultures = CultureInfo.GetCultures(CultureTypes.AllCultures)
        .Select(culture => culture.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<(BitmapSource Atlas, string[] Countries, BitmapSource Generic)> Flags = new(LoadFlags);
    private static readonly Dictionary<string, ImageSource> Crops = new(StringComparer.Ordinal);

    internal static VoiceLanguageLabel Describe(string code, string uiLocale)
    {
        var name = code;
        string? country = null;
        if (KnownCultures.Contains(code) && code.Length != 0)
        {
            var culture = CultureInfo.GetCultureInfo(code);
            // CultureInfo.DisplayName uses CurrentUICulture. Scope it synchronously on this
            // dispatcher and restore it even on failure; never change the process default.
            var original = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiLocale);
                name = culture.DisplayName;
            }
            finally { CultureInfo.CurrentUICulture = original; }
            try
            {
                country = code switch
                {
                    "en" => "us", "zh" => "cn", "ar" => "sa", "pt" => "pt",
                    _ => new RegionInfo(culture.IsNeutralCulture
                        ? CultureInfo.CreateSpecificCulture(culture.Name).Name : culture.Name).TwoLetterISORegionName.ToLowerInvariant(),
                };
            }
            catch (ArgumentException) { /* A numeric macroregion has no country flag. */ }
        }
        var flags = Flags.Value;
        var index = country is null ? -1 : Array.IndexOf(flags.Countries, country);
        ImageSource flag = flags.Generic;
        if (index >= 0)
        {
            if (!Crops.TryGetValue(country!, out flag!))
            {
                var crop = new CroppedBitmap(flags.Atlas, new Int32Rect(index % 16 * 48, index / 16 * 36, 48, 36));
                crop.Freeze();
                Crops.Add(country!, flag = crop);
            }
        }
        else country = null;
        return new(code, name, country, flag);
    }

    private static (BitmapSource, string[], BitmapSource) LoadFlags()
    {
        var assembly = typeof(VoiceLanguagePresentation).Assembly;
        using var index = Open(assembly, "countries.json");
        return (ReadBitmap(assembly, "countries.png"), JsonSerializer.Deserialize<string[]>(index)!, ReadBitmap(assembly, "language.png"));
    }

    private static Stream Open(Assembly assembly, string name) => assembly.GetManifestResourceStream(
        $"STranslate.Plugin.Tts.FishAudio.Assets.Flags.{name}") ?? throw new InvalidOperationException("Bundled language assets are missing.");

    private static BitmapSource ReadBitmap(Assembly assembly, string name)
    {
        using var stream = Open(assembly, name);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
