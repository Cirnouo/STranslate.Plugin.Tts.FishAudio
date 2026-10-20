namespace FishAudioProbe;

internal sealed record Capture(byte[] Bytes, bool Complete, string? Failure);

internal static class BoundedCapture
{
    internal static async Task<Capture> ReadAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[Math.Min(16 * 1024, maximumBytes)];
        try
        {
            while (bytes.Length < maximumBytes)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumBytes - (int)bytes.Length)), cancellationToken);
                if (read == 0) return new(bytes.ToArray(), true, null);
                bytes.Write(buffer, 0, read);
            }
            return new(bytes.ToArray(), false, "byte-limit");
        }
        catch (Exception ex) { return new(bytes.ToArray(), false, ex.GetType().Name); }
    }
}
