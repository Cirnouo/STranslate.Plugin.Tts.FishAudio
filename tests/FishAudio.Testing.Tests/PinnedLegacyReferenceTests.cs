using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PinnedLegacyReferenceTests
{
    private const string Prefix = "fa-pinned-reference-";
    private const string Commit = "ec28a59e1a9f4d5debaf2998f668bb3f7988aeca";
    private const string Tree = "ebbae71fc69aaf5b47d74e4dcbdcc6b6cf4f058c";
    private const string BlobHash = "aa382df4bc3786cfdba9a8c4e1764efd4f99135a982162c388a0f1b5e54cdbef";
    private const string CheckoutHash = "c2b27285534fcd7fc7572b379f9856b42285bfcea7e2d982204c95a470fbb627";
    private const string FirstId = "streaming.entry/formal-playback-uses-session-without-whole-buffer-host-audio";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private string _parent = "";
    private string _root = "";
    private bool _created;
    private byte[] _goldenBytes = [];

    [SetUp]
    public void ReadActualReferenceAndCreateOwnedFixture()
    {
        // Only the driver-supplied root may locate this historical golden file.
        // Discovery and execution never search a live ancestor checkout or CWD.
        var repository = Environment.GetEnvironmentVariable("FISHAUDIO_TEST_REPOSITORY_ROOT");
        Require(repository is not null && Path.IsPathFullyQualified(repository),
            "pinned-reference-fixture-explicit-repository-required");
        var repositoryRoot = RequireDirectory(repository!);
        var goldenPath = Path.Combine(repositoryRoot, "tests", "Migration", "legacy-catalog.json");
        RequireRegularFile(goldenPath);
        using (var stream = new FileStream(goldenPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Require(stream.Length is > 0 and <= 1024 * 1024, "pinned-reference-fixture-golden-size");
            _goldenBytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(_goldenBytes);
        }
        Require(Hash(_goldenBytes) is BlobHash or CheckoutHash, "pinned-reference-fixture-golden-unrecognized");

        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        Require(string.IsNullOrEmpty(configured) || Path.IsPathFullyQualified(configured),
            "pinned-reference-fixture-evidence-root-must-be-absolute");
        _parent = RequireDirectory(string.IsNullOrEmpty(configured) ? Path.GetTempPath() : configured);
        _root = Path.Combine(_parent, Prefix + Guid.NewGuid().ToString("N"));
        Require(!File.Exists(_root) && !Directory.Exists(_root), "pinned-reference-fixture-not-fresh");
        Directory.CreateDirectory(_root);
        _created = true;
        RequireDirectory(_root);
    }

    [TearDown]
    public void RetainFailedEvidenceOrDeleteOwnedFixture()
    {
        if (!_created) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            var full = RequireDirectory(_root);
            var name = Path.GetFileName(full);
            Require(string.Equals(Path.GetDirectoryName(full), _parent, StringComparison.OrdinalIgnoreCase) &&
                name.StartsWith(Prefix, StringComparison.Ordinal) &&
                Guid.TryParseExact(name[Prefix.Length..], "N", out _), "pinned-reference-fixture-owner-changed");
            if (failed)
            {
                TestContext.Progress.WriteLine("pinned-reference-fixture: failed raw evidence retained as " + name + ".");
                return;
            }
            var entries = Directory.GetFileSystemEntries(full);
            foreach (var entry in entries) RequireRegularFile(entry);
            foreach (var entry in entries) File.Delete(entry);
            Directory.Delete(full, recursive: false);
        }
        catch (Exception error) when (failed)
        {
            TestContext.Progress.WriteLine("pinned-reference-fixture: original failure and raw evidence retained; cleanup type=" +
                error.GetType().Name + ".");
        }
    }

    [TestCase("lf", TestName = "pinned-legacy-reference/accepts-git-blob-lf")]
    [TestCase("crlf", TestName = "pinned-legacy-reference/accepts-checkout-crlf")]
    public void ActualPinnedReferenceReturnsItsOriginalOrderedIdentities(string representation)
    {
        var bytes = GoldenRepresentation(representation);
        var expectedHash = representation == "lf" ? BlobHash : CheckoutHash;
        Assert.That(Hash(bytes), Is.EqualTo(expectedHash), "The fixture must be the actual pinned byte representation.");
        var path = WriteNew("reference.json", bytes);
        using var document = JsonDocument.Parse(_goldenBytes);
        var expected = document.RootElement.GetProperty("cases").EnumerateArray()
            .Select(item => new MigrationLegacyCase(item.GetProperty("id").GetString()!,
                item.GetProperty("partition").GetString()!)).ToArray();

        var (reference, error) = ReadWithoutChangingInput(path);

        Assert.That(error, Is.Null);
        Assert.That(reference, Is.Not.Null);
        Assert.That(reference!.SchemaVersion, Is.EqualTo(1));
        Assert.That(reference.Source, Is.EqualTo(new MigrationReferenceSource(Commit, Tree, expectedHash)));
        Assert.That(reference.Cases, Is.EqualTo(expected), "No historical ID, partition or order may be lost.");
    }

    [Test]
    [Property("FishAudioCaseId", "pinned-legacy-reference/tampered-id-cannot-self-authorize")]
    public void RecomputedCallerDigestCannotAuthorizeAChangedHistoricalId()
    {
        var text = Utf8.GetString(GoldenRepresentation("lf"));
        const string changedId = "streaming.entry/caller-invented-replacement";
        Assert.That(text.Split(FirstId, StringSplitOptions.None), Has.Length.EqualTo(2));
        var tampered = Utf8.GetBytes(text.Replace(FirstId, changedId, StringComparison.Ordinal));
        var claimedHash = Hash(tampered);
        Assert.That(claimedHash, Is.Not.EqualTo(BlobHash).And.Not.EqualTo(CheckoutHash));
        var path = WriteNew("reference.json", tampered);
        // A caller can recompute a correct digest of altered bytes, but cannot
        // turn it into a trusted pin through an adjacent file or this API.
        var sidecar = Utf8.GetBytes(claimedHash + "\n");
        var sidecarPath = WriteNew("reference.json.sha256", sidecar);

        var (_, error) = ReadWithoutChangingInput(path);

        AssertRejection(error, "pinned-legacy-reference-hash-mismatch");
        Assert.That(File.ReadAllBytes(sidecarPath), Is.EqualTo(sidecar));
    }

    [TestCase("truncated", TestName = "pinned-legacy-reference/truncated-reference")]
    [TestCase("missing", TestName = "pinned-legacy-reference/missing-reference")]
    public void MissingOrTruncatedReferenceCannotBeAdmitted(string fault)
    {
        var path = Path.Combine(_root, "reference.json");
        if (fault == "truncated")
        {
            var bytes = GoldenRepresentation("lf");
            WriteNew("reference.json", bytes[..(bytes.Length / 2)]);
        }

        var (_, error) = ReadWithoutChangingInput(path);

        AssertRejection(error, fault == "missing"
            ? "pinned-legacy-reference-invalid-input-or-file"
            : "pinned-legacy-reference-hash-mismatch");
    }

    private (MigrationReference? Reference, Exception? Error) ReadWithoutChangingInput(string path)
    {
        var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        MigrationReference? reference = null;
        Exception? error = null;
        try { reference = PinnedLegacyReference.ReadValidated(path); }
        catch (Exception caught) { error = caught; }
        Assert.That(File.Exists(path), Is.EqualTo(before is not null), "Admission must not create or remove input.");
        if (before is not null)
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before), "Admission must preserve the raw input bytes.");
        return (reference, error);
    }

    private byte[] GoldenRepresentation(string representation)
    {
        var lf = Utf8.GetString(_goldenBytes).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.That(lf, Does.Not.Contain("\r"));
        return Utf8.GetBytes(representation == "lf" ? lf : lf.Replace("\n", "\r\n", StringComparison.Ordinal));
    }

    private string WriteNew(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        return path;
    }

    private static void AssertRejection(Exception? error, string expectedCode)
    {
        Assert.That(error, Is.TypeOf<PinnedLegacyReferenceException>());
        var rejected = (PinnedLegacyReferenceException)error!;
        Assert.That(rejected.Code, Is.EqualTo(expectedCode));
        Assert.That(rejected.Message, Is.EqualTo(expectedCode));
        Assert.That(rejected.InnerException, Is.Null);
    }

    private static string RequireDirectory(string path)
    {
        Require(Path.IsPathFullyQualified(path) && !DevicePath(path), "pinned-reference-fixture-directory-invalid");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        for (DirectoryInfo? directory = new(full); directory is not null; directory = directory.Parent)
            Require(directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0,
                "pinned-reference-fixture-directory-missing-or-reparse");
        return full;
    }

    private static void RequireRegularFile(string path)
    {
        Require(Path.IsPathFullyQualified(path) && !DevicePath(path), "pinned-reference-fixture-file-invalid");
        var file = new FileInfo(path);
        Require(file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0,
            "pinned-reference-fixture-file-missing-or-reparse");
        RequireDirectory(file.DirectoryName!);
    }

    private static bool DevicePath(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
        path.StartsWith(@"\\.\", StringComparison.Ordinal) || path.StartsWith(@"\??\", StringComparison.Ordinal);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidOperationException(code);
    }
}
