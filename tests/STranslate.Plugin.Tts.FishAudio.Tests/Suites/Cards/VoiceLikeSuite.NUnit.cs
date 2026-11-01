using System.Diagnostics;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using static StaTestHost;

internal static partial class VoiceLikeSuite
{
    internal static async Task ObserveContextAsync(string locale, bool dark, double width, VoiceBrowseMode mode)
    {
        var name = $"{locale}/{(dark ? "dark" : "light")}/{width:0}/{mode}";
        var elapsed = Stopwatch.StartNew();
        long enteredAt = -1;
        try
        {
            await RunOnStaThreadAsync(async () =>
            {
                Interlocked.Exchange(ref enteredAt, elapsed.ElapsedMilliseconds);
                var owner = new NativeCardsTempFixtureOwner();
                await owner.RunAsync(() =>
                {
                    InspectLikeState(locale, dark, width, mode, owner);
                    return Task.CompletedTask;
                });
            });
        }
        catch
        {
            try { Console.Error.WriteLine($"[voice-like/{name}] failed after {elapsed.ElapsedMilliseconds}ms; entry={Volatile.Read(ref enteredAt)}ms."); }
            catch { /* Diagnostics cannot replace the original action failure. */ }
            throw;
        }
        Console.WriteLine($"[voice-like/{name}] completed in {elapsed.ElapsedMilliseconds}ms; entry={Volatile.Read(ref enteredAt)}ms; ordinary shared-STA action.");
    }

    internal static Task ObserveSelectedHeartSharedStateAsync() => RunOnStaThreadAsync(async () =>
    {
        var owner = new NativeCardsTempFixtureOwner();
        await owner.RunAsync(() =>
        {
            SettingsLayoutSuite.SelectedVoiceHeartSharesFavoriteState(owner);
            return Task.CompletedTask;
        });
    });
}
