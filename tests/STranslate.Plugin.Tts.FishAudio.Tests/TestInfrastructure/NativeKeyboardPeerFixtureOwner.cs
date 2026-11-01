using System.Windows.Interop;

internal sealed class NativeKeyboardPeerFixtureHooks
{
    internal Action<HwndSource>? SourceReturned;
    internal Action<HwndSource>? RootVisualCleared;
    internal Action<HwndSource>? SourceDisposed;
}

// One returned source, after the existing acquisition helper initialized its
// RootVisual. The original keyboard transaction body stays entirely synchronous.
internal sealed class NativeKeyboardPeerFixtureOwner(NativeKeyboardPeerFixtureHooks? hooks = null)
{
    private HwndSource? _source;

    internal Task RunAsync(Action body) => RegressionFixtureOwner.RunAsync(() =>
    {
        body();
        return Task.CompletedTask;
    }, ClearRootVisualAsync, DisposeSourceAsync);

    internal HwndSource Own(HwndSource source)
    {
        if (_source is not null) throw new InvalidOperationException("native-key-peer-source-already-owned");
        _source = source;
        hooks?.SourceReturned?.Invoke(source);
        return source;
    }

    private Task ClearRootVisualAsync()
    {
        if (_source is not null)
        {
            _source.RootVisual = null;
            hooks?.RootVisualCleared?.Invoke(_source);
        }
        return Task.CompletedTask;
    }

    private Task DisposeSourceAsync()
    {
        if (_source is not null)
        {
            _source.Dispose();
            hooks?.SourceDisposed?.Invoke(_source);
        }
        return Task.CompletedTask;
    }
}
