using STranslate.Plugin.Tts.FishAudio.Model;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal sealed record VoiceSearchPagination(
    int Page,
    int? ReportedTotal,
    bool IsTotalExact,
    int? LastAccessiblePage,
    bool HasNextPage,
    bool IsWindowLimited)
{
    internal static VoiceSearchPagination Initial { get; } = new(1, null, false, 1, false, false);

    internal int InputMaximum(int highestVisitedPage = 1) =>
        (int)Math.Clamp(LastAccessiblePage
            ?? Math.Max(highestVisitedPage, HasNextPage ? (long)Page + 1 : Page), 1, int.MaxValue);

    internal bool CanRequestPage(int requestedPage, int highestVisitedPage = 1) =>
        requestedPage >= 1 && requestedPage <= InputMaximum(highestVisitedPage);

    internal static VoiceSearchPagination FromResponse(ModelListResponse response, int page, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var total = NonNegative(response.Total);
        var hasPaginationMetadata = response.HasMore.HasValue
            || response.MaxOffset.HasValue
            || response.AccessibleUpperBound.HasValue
            || response.WindowLimited.HasValue
            || response.TotalIsExact.HasValue;
        var isTotalExact = total.HasValue
            && (response.TotalIsExact == true || (response.TotalIsExact is null && !hasPaginationMetadata));

        // max_offset limits the starting offset, so a partial final page remains reachable.
        // accessible_upper_bound is a record-count bound and may be rounded to a page boundary.
        int? lastPage = null;
        foreach (var bound in new[]
        {
            NonNegative(response.MaxOffset),
            NonNegative(response.AccessibleUpperBound),
            isTotalExact ? total : null,
        })
        {
            if (bound.HasValue)
            {
                var boundPage = (int)Math.Max(1, ((long)bound.Value + pageSize - 1) / pageSize);
                lastPage = lastPage.HasValue ? Math.Min(lastPage.Value, boundPage) : boundPage;
            }
        }

        // Optional or contradictory flags must not make a capped exact total look fully reachable.
        var isWindowLimited = response.WindowLimited == true
            || (isTotalExact && lastPage.HasValue && (long)lastPage.Value * pageSize < total.GetValueOrDefault());
        var rawItemCount = response.Items?.Count ?? 0;
        var hasNextPage = page < int.MaxValue
            && rawItemCount > 0
            && (response.HasMore ?? (isTotalExact ? page < lastPage.GetValueOrDefault(page) : rawItemCount >= pageSize))
            && (!lastPage.HasValue || page < lastPage.Value);

        if (!hasNextPage)
            lastPage = lastPage.HasValue ? Math.Min(lastPage.Value, page) : page;

        // A changing catalog may return an empty page beyond its new bound. Keep the successful
        // page visible while preventing further forward requests until the next explicit search.
        if (lastPage.HasValue)
            lastPage = Math.Max(page, lastPage.Value);

        return new VoiceSearchPagination(
            page, total, isTotalExact, lastPage, hasNextPage, isWindowLimited);
    }

    private static int? NonNegative(int? value) => value is >= 0 ? value : null;
}
