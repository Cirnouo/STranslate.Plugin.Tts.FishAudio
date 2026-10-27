using System.Security.Cryptography;
using System.Text;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class DotCoverConsoleDiagnosticTests
{
    private const string Prefix = "fa-console-diagnostics-";
    // Literal protocol from the retained sync session. The copyright dash is
    // the raw A8 43 pair, not UTF-8 or a replacement-decoded character.
    private const string Cover =
        "[JetBrains dotCover] Coverage session started [Tue 10 27 2026 11:25:51 PM]\r\n" +
        "[JetBrains dotCover] Coverage session finished [Tue 10 27 2026 11:26:01 PM]\r\n" +
        "[JetBrains dotCover] Coverage results post-processing started [Tue 10 27 2026 11:26:01 PM]\r\n" +
        "[JetBrains dotCover] Merging snapshots [Tue 10 27 2026 11:26:01 PM]\r\n" +
        "[JetBrains dotCover] Snapshots merging finished [Tue 10 27 2026 11:26:01 PM]\r\n" +
        "[JetBrains dotCover] Coverage results post-processing finished [Tue 10 27 2026 11:26:01 PM]\r\n";
    private const string Export =
        "[JetBrains dotCover] Report generation started. [Tue 10 27 2026 11:26:03 PM]\r\n" +
        "[JetBrains dotCover] JSON report generated. [Tue 10 27 2026 11:26:04 PM]\r\n" +
        "[JetBrains dotCover] XML report generated. [Tue 10 27 2026 11:26:04 PM]\r\n";
    private string _parent = "";
    private string _root = "";
    private string[] _paths = [];

    [SetUp]
    public void PrepareRawConsoleStreams()
    {
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        var parent = string.IsNullOrEmpty(configured) ? Path.GetTempPath() : configured;
        Assert.That(Path.IsPathFullyQualified(parent) && Directory.Exists(parent), Is.True);
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        for (DirectoryInfo? directory = new(_parent); directory is not null; directory = directory.Parent)
            Assert.That((directory.Attributes & FileAttributes.ReparsePoint) == 0, Is.True);
        _root = Path.Combine(_parent, Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new[] { "cover.stdout", "cover.stderr", "export.stdout", "export.stderr" }
            .Select(name => Path.Combine(_root, name)).ToArray();
        File.WriteAllBytes(_paths[0], ObservedBytes(Cover));
        File.WriteAllBytes(_paths[1], []);
        File.WriteAllBytes(_paths[2], ObservedBytes(Export));
        File.WriteAllBytes(_paths[3], []);
    }

    [TearDown]
    public void ReleaseOwnedStreams()
    {
        if (string.IsNullOrEmpty(_root) || !Directory.Exists(_root)) return;
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            TestContext.Error.WriteLine("console-diagnostics: failed raw fixture retained as " + Path.GetFileName(_root) + ".");
            return;
        }
        var full = Path.GetFullPath(_root);
        var name = Path.GetFileName(full);
        Assert.That(Path.GetDirectoryName(full), Is.EqualTo(_parent).IgnoreCase);
        Assert.That(name.StartsWith(Prefix, StringComparison.Ordinal) && Guid.TryParseExact(name[Prefix.Length..], "N", out _) &&
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0, Is.True);
        Directory.Delete(full, recursive: true);
    }

    [Test]
    [Property("FishAudioCaseId", "console-diagnostics/exact-observed-console-protocol")]
    public void ObservedConsoleProtocolReturnsAllRawDigestsWithoutRewriting()
    {
        var before = _paths.Select(File.ReadAllBytes).ToArray();
        Assert.That(before.Select(bytes => bytes.Length), Is.EqualTo(new[] { 632, 0, 372, 0 }));
        var review = Read();
        Assert.That(review.SchemaVersion, Is.EqualTo(1));
        Assert.That(review.RuleId, Is.EqualTo("dotcover-2026.2.3.1-build-20260930.210750-windows-x64-console-cp936-v1"));
        Assert.That(review.Evidence.Select(item => item.Path), Is.EqualTo(_paths));
        Assert.That(review.Evidence.Select(item => item.Sha256), Is.EqualTo(before
            .Select(bytes => Convert.ToHexStringLower(SHA256.HashData(bytes)))));
        Assert.That(review.Evidence[0].Sha256, Is.EqualTo("a1d1312736b57d6c8b622939ca54c9ec0409b87e7a11c5c3cdc8e0a84f4dfb64"));
        Assert.That(review.Evidence[2].Sha256, Is.EqualTo("380ad9f8656fc4071236b410752652eaa5a7d1c4811d953d25291e340fc7d587"));
        Assert.That(review.BaselineClaim, Is.False);
        Assert.That(review.Scope, Does.Contain("console-only").And.Contain("no process, test-pass or baseline claim"));
        for (var index = 0; index < _paths.Length; index++)
            Assert.That(File.ReadAllBytes(_paths[index]), Is.EqualTo(before[index]));
    }

    [TestCase("warning", "console-incomplete-or-extra-content", TestName = "console-diagnostics/extra-cover-warning")]
    [TestCase("error", "console-incomplete-or-extra-content", TestName = "console-diagnostics/extra-export-error")]
    [TestCase("unknown", "console-message-mismatch", TestName = "console-diagnostics/unknown-console-message")]
    [TestCase("truncated", "console-incomplete-or-extra-content", TestName = "console-diagnostics/truncated-cover-stream")]
    [TestCase("incomplete", "console-incomplete-or-extra-content", TestName = "console-diagnostics/missing-xml-report-completion")]
    [TestCase("order", "console-message-mismatch", TestName = "console-diagnostics/reordered-completion")]
    [TestCase("cover-stderr", "console-unexpected-stderr", TestName = "console-diagnostics/nonempty-cover-stderr")]
    [TestCase("export-stderr", "console-unexpected-stderr", TestName = "console-diagnostics/nonempty-export-stderr")]
    [TestCase("banner", "console-banner-or-encoding-mismatch", TestName = "console-diagnostics/different-tool-build")]
    [TestCase("encoding", "console-banner-or-encoding-mismatch", TestName = "console-diagnostics/changed-console-encoding")]
    [TestCase("time", "console-invalid-timestamp", TestName = "console-diagnostics/invalid-calendar-timestamp")]
    [TestCase("missing", "console-missing-or-nonregular-file", TestName = "console-diagnostics/missing-raw-stream")]
    [TestCase("version", "console-toolchain-mismatch", TestName = "console-diagnostics/different-tool-version")]
    [TestCase("size", "console-size-limit", TestName = "console-diagnostics/oversized-raw-stream")]
    [TestCase("duplicate", "console-artifact-paths-must-differ", TestName = "console-diagnostics/reused-stream-path")]
    [TestCase("trailing", "console-incomplete-or-extra-content", TestName = "console-diagnostics/extra-trailing-blank-line")]
    public void UnreviewedOrIncompleteConsoleBytesAreRejected(string fault, string code)
    {
        var cover = Cover;
        var export = Export;
        switch (fault)
        {
            case "warning": cover += "[JetBrains dotCover] Warning: collection was incomplete\r\n"; break;
            case "error": export += "[JetBrains dotCover] Error: report could not be saved\r\n"; break;
            case "unknown": cover = cover.Replace("Merging snapshots", "Unknown success message", StringComparison.Ordinal); break;
            case "truncated": cover = cover[..^1]; break;
            case "incomplete": export = export[..export.LastIndexOf("[JetBrains", StringComparison.Ordinal)]; break;
            case "order": cover = cover.Replace("Merging snapshots", "Snapshots merging finished", StringComparison.Ordinal); break;
            case "time": cover = cover.Replace("10 27 2026", "02 30 2026", StringComparison.Ordinal); break;
            case "trailing": export += "\r\n"; break;
        }
        File.WriteAllBytes(_paths[0], ObservedBytes(cover));
        File.WriteAllBytes(_paths[2], ObservedBytes(export));
        if (fault == "cover-stderr") File.WriteAllBytes(_paths[1], [0x20]);
        if (fault == "export-stderr") File.WriteAllBytes(_paths[3], [0x0d, 0x0a]);
        if (fault == "banner")
        {
            var bytes = File.ReadAllBytes(_paths[2]);
            bytes[bytes.AsSpan().IndexOf("20260930"u8)] = (byte)'1';
            File.WriteAllBytes(_paths[2], bytes);
        }
        if (fault == "encoding")
        {
            var bytes = File.ReadAllBytes(_paths[0]);
            var dash = bytes.AsSpan().IndexOf(new byte[] { 0xa8, 0x43 });
            File.WriteAllBytes(_paths[0], [.. bytes[..dash], 0xe2, 0x80, 0x93, .. bytes[(dash + 2)..]]);
        }
        if (fault == "missing") File.Delete(_paths[3]);
        if (fault == "size") File.WriteAllBytes(_paths[0], new byte[64 * 1024 + 1]);
        var failure = Assert.Throws<DotCoverConsoleDiagnosticException>(() => Read(
            version: fault == "version" ? "2026.2.3.2" : "2026.2.3.1",
            coverStderr: fault == "duplicate" ? _paths[3] : null));
        Assert.That(failure!.Code, Is.EqualTo(code));
        Assert.That(failure.Message, Is.EqualTo(code));
    }

    private ConsoleDiagnosticReview Read(string version = "2026.2.3.1", string? coverStderr = null) =>
        DotCoverConsoleDiagnostics.ReadValidated(_paths[0], coverStderr ?? _paths[1], _paths[2], _paths[3], version);

    private static byte[] ObservedBytes(string body) =>
    [
        .. Encoding.ASCII.GetBytes("JetBrains dotCover Console Runner 2026.2.3.1. Build 20260930.210750 (windows-x64)\r\nCopyright (c) 2009"),
        0xa8, 0x43,
        .. Encoding.ASCII.GetBytes("2026 JetBrains s.r.o. All rights reserved.\r\n" + body),
    ];
}
