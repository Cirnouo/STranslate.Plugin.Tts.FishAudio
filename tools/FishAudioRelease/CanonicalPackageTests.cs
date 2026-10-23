using System.IO.Compression;
using System.Text;
namespace FishAudioRelease;

internal static class CanonicalPackageTests
{
    internal static byte[] Fixture(bool reverse, int year, string payload = "dll-bytes")
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var names = reverse ? new[]
            {
                "fish.dll",
                "plugin.json"
            }

            : new[]
            {
                "plugin.json",
                "fish.dll"
            };
            foreach (var name in names)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(year, 1, 2, 3, 4, 6, TimeSpan.Zero);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(name == "plugin.json" ? PublisherTests.Manifest : payload);
            }
        }

        return stream.ToArray();
    }

    internal static async Task Run()
    {
        var original = Fixture(false, 2025);
        var reordered = Fixture(true, 2026);
        SelfTest.Equal(false, original.SequenceEqual(reordered));
        var first = CanonicalPackage.Normalize(original);
        var second = CanonicalPackage.Normalize(reordered);
        SelfTest.Equal(true, first.SequenceEqual(second));
        SelfTest.Equal(true, first.SequenceEqual(CanonicalPackage.Normalize(first)));
        Publisher.PackageIdentity(PublisherTests.Manifest, first, "1.2.3");
        var input = new ReleaseInput("owner/repo", "v1.2.3", PublisherTests.Sha, PublisherTests.TagObject);
        var fake = new PublisherTests.Fake
        {
            Published = true,
            Bytes = first
        };
        SelfTest.Equal("skipped", await Publisher.Publish(input, fake, PublisherTests.Manifest, second, "## [1.2.3] - 2026-10-23\n- change"));
        var changed = CanonicalPackage.Normalize(Fixture(false, 2025, "different-dll"));
        await PublisherTests.Reject(async () => await Publisher.Publish(input, fake, PublisherTests.Manifest, changed, "## [1.2.3] - 2026-10-23\n- change"));
        using var inputZip = new ZipArchive(new MemoryStream(original));
        using var outputZip = new ZipArchive(new MemoryStream(first));
        foreach (var entry in inputZip.Entries)
        {
            using var before = entry.Open();
            using var after = outputZip.GetEntry(entry.FullName)!.Open();
            using var beforeBytes = new MemoryStream();
            using var afterBytes = new MemoryStream();
            before.CopyTo(beforeBytes);
            after.CopyTo(afterBytes);
            SelfTest.Equal(true, beforeBytes.ToArray().SequenceEqual(afterBytes.ToArray()));
        }

        Console.WriteLine("Canonical package reproducibility and published rerun tests passed.");
    }
}
