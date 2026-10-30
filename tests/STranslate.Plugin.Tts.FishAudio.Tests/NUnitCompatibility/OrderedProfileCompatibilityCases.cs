using System.Globalization;
using NUnit.Framework;

namespace NUnitCompatibility;

// Pure logic scheduling probe. Successful execution is not native acceptance.
[TestFixture]
[NonParallelizable]
[Order(10)]
public sealed class OrderedProfileZuluFixture
{
    [OneTimeSetUp]
    public void InitializeTheDedicatedTraceOnce() => OrderedProfileCompatibilityTrace.InitializeOnce();

    [TestCaseSource(nameof(ProfileRows))]
    public Task AProfileLeafRetainsItsOwnerAcrossAwait(int ordinal) =>
        OrderedProfileCompatibilityTrace.ObserveAcrossAwaitAsync(ordinal);

    private static IEnumerable<TestCaseData> ProfileRows()
    {
        yield return new TestCaseData(0)
            .SetName("OrderedProfile_ZuluFixture_ZuluRow0")
            .SetProperty("FishAudioCaseId", "compat.ordered-profile/first-0")
            .SetProperty("FishAudioExecutionProfile", "compat.ordered-profile")
            .SetProperty("FishAudioProfileOrdinal", 0);
        yield return new TestCaseData(1)
            .SetName("OrderedProfile_ZuluFixture_AlphaRow1")
            .SetProperty("FishAudioCaseId", "compat.ordered-profile/first-1")
            .SetProperty("FishAudioExecutionProfile", "compat.ordered-profile")
            .SetProperty("FishAudioProfileOrdinal", 1);
    }
}

[TestFixture]
[NonParallelizable]
[Order(20)]
public sealed class OrderedProfileAlphaFixture
{
    // This fixture must consume the first fixture's trace without resetting it.
    [TestCaseSource(nameof(ProfileRows))]
    public Task AProfileLeafRetainsItsOwnerAcrossAwait(int ordinal) =>
        OrderedProfileCompatibilityTrace.ObserveAcrossAwaitAsync(ordinal);

    private static IEnumerable<TestCaseData> ProfileRows()
    {
        yield return new TestCaseData(2)
            .SetName("OrderedProfile_AlphaFixture_ZuluRow2")
            .SetProperty("FishAudioCaseId", "compat.ordered-profile/second-2")
            .SetProperty("FishAudioExecutionProfile", "compat.ordered-profile")
            .SetProperty("FishAudioProfileOrdinal", 2);
        yield return new TestCaseData(3)
            .SetName("OrderedProfile_AlphaFixture_AlphaRow3")
            .SetProperty("FishAudioCaseId", "compat.ordered-profile/second-3")
            .SetProperty("FishAudioExecutionProfile", "compat.ordered-profile")
            .SetProperty("FishAudioProfileOrdinal", 3);
    }
}

internal static class OrderedProfileCompatibilityTrace
{
    private static readonly object Sync = new();
    private static readonly string[] ExpectedEvents =
    [
        "before:0", "after:0", "before:1", "after:1",
        "before:2", "after:2", "before:3", "after:3",
    ];
    private static TraceOwner? _owner;

    internal static void InitializeOnce()
    {
        lock (Sync)
        {
            if (_owner is not null)
                throw new InvalidOperationException("ordered-profile-fresh-host-required");
            _owner = new TraceOwner();
        }
    }

    internal static async Task ObserveAcrossAwaitAsync(int ordinal)
    {
        TraceOwner owner;
        lock (Sync)
        {
            owner = _owner ?? throw new InvalidOperationException("ordered-profile-predecessor-required");
            if (ordinal is < 0 or > 3)
                throw new InvalidOperationException("ordered-profile-ordinal-invalid");
            if (owner.ActiveOrdinal is not null)
                throw new InvalidOperationException("ordered-profile-overlap");
            if (owner.NextOrdinal != ordinal)
                throw new InvalidOperationException("ordered-profile-predecessor-required");
            owner.ActiveOrdinal = ordinal;
            owner.Events.Add("before:" + ordinal.ToString(CultureInfo.InvariantCulture));
        }

        await Task.Yield();

        string[] observed;
        lock (Sync)
        {
            RequireSameActiveOwner(owner, ordinal);
            owner.Events.Add("after:" + ordinal.ToString(CultureInfo.InvariantCulture));
            observed = owner.Events.ToArray();
        }
        Assert.That(observed, Is.EqualTo(ExpectedEvents[..((ordinal + 1) * 2)]),
            "The dedicated trace must retain the exact ordered prefix across await.");
        lock (Sync)
        {
            RequireSameActiveOwner(owner, ordinal);
            owner.NextOrdinal++;
            owner.ActiveOrdinal = null;
        }
    }

    private static void RequireSameActiveOwner(TraceOwner owner, int ordinal)
    {
        if (!ReferenceEquals(_owner, owner) || owner.ActiveOrdinal != ordinal || owner.NextOrdinal != ordinal)
            throw new InvalidOperationException("ordered-profile-owner-changed");
    }

    private sealed class TraceOwner
    {
        internal List<string> Events { get; } = [];
        internal int NextOrdinal;
        internal int? ActiveOrdinal;
    }
}
