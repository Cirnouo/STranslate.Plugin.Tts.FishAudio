using STranslate.Plugin;
using STranslate.Plugin.Tts.FishAudio;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using STranslate.Plugin.Tts.FishAudio.Model;
using STranslate.Plugin.Tts.FishAudio.ViewModel;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class AsyncTestWait
{
    internal static async Task WaitUntilAsync(Func<bool> predicate, string description)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!predicate() && elapsed.Elapsed < RegressionExecution.Timeout)
            await Task.Delay(10);

        if (!predicate())
            throw new InvalidOperationException($"Timed out waiting for {description}.");
    }
}
