using System.IO.Compression;

namespace FishAudioRelease;

internal static class CanonicalPackage
{
    internal static byte[] Normalize(byte[] package)
    {
        using var input = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in input.Entries)
        {
            var name = entry.FullName;
            Protocol.Require(name.Length > 0 &&
            !name.Contains('\\') &&
            !name.StartsWith('/') &&
            !name.Contains(':') &&
            !name.Split('/').Any(part => part is "." or "..") &&
            names.Add(name), "Unsafe or duplicate package entry.");
        }

        using var bytes = new MemoryStream();
        using (var output = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            foreach (var entry in input.Entries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
            {
                // Store uncompressed bytes so output does not depend on a native compression-library version.
                var normalized = output.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                normalized.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                normalized.ExternalAttributes = 0;
                using var source = entry.Open();
                using var destination = normalized.Open();
                source.CopyTo(destination);
            }
        }

        return bytes.ToArray();
    }
}
