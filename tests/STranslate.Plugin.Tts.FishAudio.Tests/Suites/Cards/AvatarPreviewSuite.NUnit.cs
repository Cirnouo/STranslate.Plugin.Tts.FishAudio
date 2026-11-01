using System.Windows.Automation.Provider;
using STranslate.Plugin.Tts.FishAudio.ViewModel;

internal static partial class AvatarPreviewSuite
{
    internal static Task ObserveContextAsync(VoiceBrowseMode mode, bool dark, double dpi, double width) =>
        RunContextAsync(mode, dark, dpi, width, proofDirectory: null, owner: new NativeAvatarFixtureOwner());

    internal static Task ObserveFailureBoundaryAsync(string point, Action<string, NativeAvatarFixtureTrace> observe) =>
        RunContextAsync(VoiceBrowseMode.Search, false, 1d, 320d, proofDirectory: null,
            owner: new NativeAvatarFixtureOwner(observe, holdAutomationReply: point == "queued-uia-preview"));

    private static Task Track(NativeAvatarFixtureOwner? owner, Task operation) => owner?.TrackOperation(operation) ?? operation;

    private static TaskCompletionSource<Stream> OwnResponse(NativeAvatarFixtureOwner? owner, TaskCompletionSource<Stream> response) =>
        owner?.OwnResponse(response) ?? response;

    private static void InvokeAutomation(NativeAvatarFixtureOwner? owner, IInvokeProvider provider)
    {
        if (owner is null) provider.Invoke();
        else owner.InvokeAutomation(provider);
    }

    private static void Restore(NativeAvatarFixtureOwner? owner, Exception? failure, params Action[] cleanup)
    {
        if (owner is null)
            foreach (var action in cleanup) action();
        else NativeAvatarFixtureOwner.Restore(failure, cleanup);
    }
}
