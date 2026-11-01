using System.Windows.Interop;
using STranslate.Plugin.Tts.FishAudio.View;

internal sealed class PopupHeadingSourceFixtureHooks
{
    internal Action<PopupHeadingSourceFixtureProbe>? NativeHeadingReady;
    internal Action<HwndSource>? BeforeSecondDispose;
    internal Action<HwndSource>? SecondDisposed;
}

internal sealed record PopupHeadingSourceFixtureProbe(HwndSource FirstSource, HwndSource SecondSource,
    NativePopupWindowTextApi NativeApi, string PriorText);

// Only the original continuous native text/source replacement scenario uses
// these two returned sources. No Popup, timer or asynchronous production work.
internal sealed class PopupHeadingSourceFixtureOwner(PopupHeadingSourceFixtureHooks? hooks = null)
{
    internal HwndSource? FirstSource;
    internal HwndSource? SecondSource;

    internal Task RunAsync(Func<Task> body) => RegressionFixtureOwner.RunAsync(body,
        ObserveSecondRetiringAsync, DisposeSecondAsync, DisposeFirstAsync);

    internal void NativeHeadingReady(NativePopupWindowTextApi native, string prior) =>
        hooks?.NativeHeadingReady?.Invoke(new PopupHeadingSourceFixtureProbe(FirstSource!, SecondSource!, native, prior));

    private Task ObserveSecondRetiringAsync()
    {
        if (SecondSource is not null) hooks?.BeforeSecondDispose?.Invoke(SecondSource);
        return Task.CompletedTask;
    }

    private Task DisposeSecondAsync()
    {
        if (SecondSource is not null)
        {
            SecondSource.Dispose();
            hooks?.SecondDisposed?.Invoke(SecondSource);
        }
        return Task.CompletedTask;
    }

    private Task DisposeFirstAsync()
    {
        FirstSource?.Dispose();
        return Task.CompletedTask;
    }
}
