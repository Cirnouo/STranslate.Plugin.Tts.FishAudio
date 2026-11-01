using System.Windows.Interop;

// Owns only a HwndSource that the factory actually returned, until initialization
// succeeds and transfers the same object to the caller's original lifetime.
// It creates no action, pump, Task, timer, retry or replacement source.
internal static class PopupSourceAcquisition
{
    internal const string CleanupFailureKey = "PopupSourceAcquisition.CleanupFailure";

    internal static HwndSource Acquire(Func<HwndSource> create, Action<HwndSource> initialize,
        Action<HwndSource>? retireFailedSource = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(initialize);
        HwndSource? source = null;
        try
        {
            source = create();
            initialize(source);
            return source;
        }
        catch (Exception primary)
        {
            if (source is not null)
            {
                try { (retireFailedSource ?? Retire)(source); }
                catch (Exception cleanup)
                {
                    try { primary.Data[CleanupFailureKey] = cleanup; }
                    catch { /* Extensible error metadata cannot replace the primary. */ }
                }
            }
            throw;
        }
    }

    private static void Retire(HwndSource source) => source.Dispose();
}
