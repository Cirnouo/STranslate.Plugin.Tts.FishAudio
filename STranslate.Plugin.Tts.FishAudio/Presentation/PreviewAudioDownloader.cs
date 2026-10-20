using System.IO;

namespace STranslate.Plugin.Tts.FishAudio.Presentation;

internal sealed class PreviewAudioDownloader(IHttpService httpService)
{
    internal const int MaximumBytes = 8 * 1024 * 1024;
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    internal async Task<MemoryStream> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        // The coordinator's token spans metadata refresh, the request and all body reads.
        // No account or dummy authorization is sent to an existing media resource.
        var options = new Options { Timeout = Timeout };
        await using var source = await httpService.GetAsStreamAsync(uri.AbsoluteUri, options, cancellationToken).ConfigureAwait(false);
        var destination = new MemoryStream(16 * 1024);
        try
        {
            var chunk = new byte[16 * 1024];
            while (destination.Length <= MaximumBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = MaximumBytes + 1 - (int)destination.Length;
                var read = await source.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                destination.Write(chunk, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (destination.Length == 0 || destination.Length > MaximumBytes)
                throw new InvalidDataException("Preview audio is empty or exceeds the size limit.");
            destination.Position = 0;
            return destination;
        }
        catch { destination.Dispose(); throw; }
    }
}
