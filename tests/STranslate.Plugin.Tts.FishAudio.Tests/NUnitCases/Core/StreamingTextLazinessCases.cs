using NUnit.Framework;

namespace NUnitCases.Core;

// Ordinary lazy-text scenario; child compatibility retains its separate supervised target.
[TestFixture]
[NonParallelizable]
public sealed class StreamingTextLazinessCases
{
    [Test]
    [Property("FishAudioCaseId", "streaming.text/lazy-unpunctuated-first-window-does-not-read-tail")]
    [Property("FishAudioFreshHost", "true")]
    public Task FirstUnpunctuatedUnicodeWindowLeavesTheTailUnread()
        => StreamingTextSuite.FirstWindowIsLazyAndCutsOrdinaryLongSentencesAsync();
}
