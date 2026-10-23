using System.Globalization;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal static class PageInputDraft
{
    internal static bool IsDigits(string draft) => draft.All(character => character is >= '0' and <= '9');

    internal static bool TryGetPage(string draft, out int page)
    {
        page = 0;
        return IsDigits(draft)
            && int.TryParse(draft, NumberStyles.None, CultureInfo.InvariantCulture, out page) && page >= 1;
    }

    internal static string Clamp(string draft, int maximum)
    {
        if (!IsDigits(draft)) return draft;
        var significant = draft.AsSpan().TrimStart('0');
        var limit = Math.Max(1, maximum).ToString(CultureInfo.InvariantCulture);
        return significant.Length > limit.Length
            || (significant.Length == limit.Length && significant.CompareTo(limit.AsSpan(), StringComparison.Ordinal) > 0)
            ? limit : draft;
    }
}
