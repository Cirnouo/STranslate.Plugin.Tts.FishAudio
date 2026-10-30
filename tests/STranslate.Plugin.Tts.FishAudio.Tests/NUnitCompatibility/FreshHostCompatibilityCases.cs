using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
public sealed class FreshHostCompatibilityCases
{
    // Intentionally shared by both rows in one CLR. There is no reset, setup,
    // teardown or fallback: only a fresh host can provide an unclaimed owner.
    private static int _owner;

    public static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData("first").SetName("FreshHost_First")
            .SetProperty("FishAudioCaseId", "compat.fresh-host/first");
        yield return new TestCaseData("second").SetName("FreshHost_Second")
            .SetProperty("FishAudioCaseId", "compat.fresh-host/second");
    }

    [TestCaseSource(nameof(Cases))]
    public async Task EachLeafNeedsAnUnclaimedStaticOwner(string row)
    {
        Assert.That(Interlocked.CompareExchange(ref _owner, 1, 0), Is.Zero,
            "fresh-host-static-owner-must-start-unclaimed: " + row);
        Assert.That(Volatile.Read(ref _owner), Is.EqualTo(1));
        await Task.Yield();
        Assert.That(Volatile.Read(ref _owner), Is.EqualTo(1),
            "fresh-host-static-owner-must-survive-await: " + row);
    }
}