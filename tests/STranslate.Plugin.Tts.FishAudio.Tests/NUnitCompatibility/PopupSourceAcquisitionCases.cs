using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using NUnit.Framework;

namespace NUnitCompatibility;

[TestFixture]
[NonParallelizable]
[Property("FishAudioFreshHost", "true")]
[Property("FishAudioExclusiveResource", "desktop")]
public sealed class PopupSourceAcquisitionCases
{
    public static IEnumerable<TestCaseData> AcquisitionRows()
    {
        yield return Row("successful-initialization-transfers-live-source", false, false, false);
        yield return Row("initialization-failure-disposes-actual-source", true, false, false);
        yield return Row("retirement-failure-keeps-primary-before-rescue", true, true, false);
        yield return Row("unavailable-error-data-cannot-replace-primary", true, true, true);
    }

    [TestCaseSource(nameof(AcquisitionRows))]
    public Task InitializationRetainsTheActualSourceAndOriginalFailure(
        bool failInitialization, bool failRetirement, bool unavailableData) =>
        StaTestHost.RunOnStaThreadAsync(() =>
        {
            HwndSource? created = null;
            HwndSource? failedRetirementSource = null;
            var root = new Border { Width = 24, Height = 16 };
            Exception primary = unavailableData ? new UnavailableDataException()
                : new InvalidOperationException("popup-source-initialization-failure");
            var cleanup = new IOException("popup-source-retirement-failure");
            var creates = 0;
            var initializes = 0;
            var retirements = 0;
            return RegressionFixtureOwner.RunAsync(() =>
            {
                HwndSource Create()
                {
                    creates++;
                    created = new HwndSource(new HwndSourceParameters("Fish Audio source acquisition owner")
                    {
                        Width = 32, Height = 24, WindowStyle = unchecked((int)0x80000000),
                        PositionX = -10000, PositionY = -10000,
                    });
                    return created;
                }
                void Initialize(HwndSource source)
                {
                    initializes++;
                    source.RootVisual = root;
                    if (failInitialization) throw primary;
                }
                Action<HwndSource>? retire = null;
                if (failRetirement)
                    retire = source =>
                    {
                        retirements++;
                        failedRetirementSource = source;
                        throw cleanup;
                    };

                if (failInitialization)
                {
                    var observed = Assert.Catch(() => PopupSourceAcquisition.Acquire(Create, Initialize, retire));
                    Assert.That(observed, Is.SameAs(primary), "Initialization keeps the exact primary error, including when cleanup or error metadata fails.");
                    Assert.That(created, Is.Not.Null);
                    Assert.That(created!.IsDisposed, Is.EqualTo(!failRetirement),
                        "Inspect the actual native source before independent rescue; failed retirement cannot claim disposal.");
                    if (failRetirement)
                    {
                        Assert.That(failedRetirementSource, Is.SameAs(created));
                        Assert.That(retirements, Is.EqualTo(1), "Cleanup makes one attempt without retry.");
                        if (!unavailableData)
                            Assert.That(primary.Data[PopupSourceAcquisition.CleanupFailureKey], Is.SameAs(cleanup));
                    }
                }
                else
                {
                    var returned = PopupSourceAcquisition.Acquire(Create, Initialize, retire);
                    Assert.That(returned, Is.SameAs(created), "The caller receives the exact constructor-returned source.");
                    Assert.That(returned.IsDisposed, Is.False);
                    Assert.That(returned.RootVisual, Is.SameAs(root));
                    Assert.That(returned.Handle, Is.Not.EqualTo(IntPtr.Zero));
                    Assert.That(retirements, Is.Zero, "Successful initialization transfers ownership to the caller.");
                }
                Assert.That(creates, Is.EqualTo(1), "There is no replacement source or construction retry.");
                Assert.That(initializes, Is.EqualTo(1));
                return Task.CompletedTask;
            }, () =>
            {
                // Independent caller/rescue cleanup follows every ownership oracle.
                // It cannot turn a failed helper retirement into the assertion above.
                created?.Dispose();
                return Task.CompletedTask;
            });
        });

    private static TestCaseData Row(string name, bool initialize, bool retire, bool data) =>
        new TestCaseData(initialize, retire, data).SetName("PopupSourceAcquisition_" + name)
            .SetProperty("FishAudioCaseId", "infra.popup-source-acquisition/" + name);

    private sealed class UnavailableDataException : Exception
    {
        public override IDictionary Data => throw new InvalidOperationException("popup-source-primary-data-unavailable");
    }
}
