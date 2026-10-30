using System.Security.Cryptography;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace NUnitCompatibility;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class SnapshotInputPathCases
{
    private const string OwnedPrefix = "fa-snapshot-input-";
    private string _parent = "";
    private string _root = "";

    [SetUp]
    public void CreateOwnedSnapshotDirectory()
    {
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        _root = Path.Combine(_parent, OwnedPrefix + Guid.NewGuid().ToString("N"));
        if (File.Exists(_root) || Directory.Exists(_root))
            throw new InvalidOperationException("snapshot-input-path-fixture-already-exists");
        Directory.CreateDirectory(_root);
        RequireOwnedDirectory();
    }

    [TearDown]
    public void DeleteOwnedSnapshotDirectory()
    {
        if (_root.Length == 0) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            RequireOwnedDirectory();
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            if (failed)
            {
                TestContext.Progress.WriteLine("snapshot-input-path: cleanup failed; owned fixture retained.");
                return;
            }
            throw new InvalidOperationException("snapshot-input-path-fixture-cleanup-failed");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "snapshot-input-path/declared-missing-file")]
    public void ADeclaredFileMustNotBecomeAnExpectedAbsenceWhenItsBytesAreMissing()
    {
        const string relative = "owned.txt";
        var snapshot = new SnapshotContext
        {
            Root = _root,
            Files = [Entry(relative, [0x61, 0x00, 0xff, 0x0a])],
        };
        Assert.That(File.Exists(Path.Combine(_root, relative)), Is.False);

        Assert.Throws<FileNotFoundException>(() => snapshot.InputPath(relative),
            "The inventory declares this file as present; a missing input cannot satisfy a negative existence check.");
    }

    [Test]
    [Property("FishAudioCaseId", "snapshot-input-path/declared-descendant-missing-directory")]
    public void ADeclaredDescendantMustNotDisappearBehindItsMissingDirectory()
    {
        const string directory = "owned-directory";
        var snapshot = new SnapshotContext
        {
            Root = _root,
            Files = [Entry("owned-directory/nested/input.txt", [0x62, 0x00, 0xfe, 0x0a])],
        };
        Assert.That(Directory.Exists(Path.Combine(_root, directory)), Is.False);

        Assert.Throws<FileNotFoundException>(() => snapshot.InputPath(directory),
            "Looking up a missing directory must still reject its declared but missing descendant.");
    }

    [Test]
    [Property("FishAudioCaseId", "snapshot-input-path/unlisted-missing-path")]
    public void AnUndeclaredMissingPathRemainsBoundToTheOwnedRoot()
    {
        var snapshot = new SnapshotContext { Root = _root, Files = [] };
        var expected = Path.Combine(_root, "absent-owned-input.cs");

        var path = snapshot.InputPath("absent-owned-input.cs");

        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo(expected));
            Assert.That(File.Exists(path), Is.False);
            Assert.That(Directory.Exists(path), Is.False);
        });
    }

    [Test]
    [Property("FishAudioCaseId", "snapshot-input-path/declared-matching-file")]
    public void ADeclaredFileWithMatchingBytesResolvesWithoutChangingThoseBytes()
    {
        byte[] bytes = [0x63, 0x00, 0xfd, 0x0a];
        var path = Path.Combine(_root, "owned.txt");
        File.WriteAllBytes(path, bytes);
        var snapshot = new SnapshotContext { Root = _root, Files = [Entry("owned.txt", bytes)] };

        var resolved = snapshot.InputPath("owned.txt");

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.EqualTo(path));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
        });
    }

    [Test]
    [Property("FishAudioCaseId", "snapshot-input-path/declared-wrong-bytes")]
    public void ADeclaredFileWithDifferentBytesIsRejected()
    {
        byte[] expected = [0x64, 0x00, 0xfc, 0x0a];
        byte[] actual = [0x65, 0x00, 0xfb, 0x0a];
        var path = Path.Combine(_root, "owned.txt");
        File.WriteAllBytes(path, actual);
        var snapshot = new SnapshotContext { Root = _root, Files = [Entry("owned.txt", expected)] };

        Assert.Throws<InvalidDataException>(() => snapshot.InputPath("owned.txt"));

        Assert.That(File.ReadAllBytes(path), Is.EqualTo(actual), "Rejected input remains caller-owned.");
    }

    [Test]
    [Property("FishAudioCaseId", "snapshot-input-path/existing-unlisted-file")]
    public void AnExistingUnlistedFileCannotBecomeAnOwnedInput()
    {
        byte[] bytes = [0x66, 0x00, 0xfa, 0x0a];
        var path = Path.Combine(_root, "unlisted.txt");
        File.WriteAllBytes(path, bytes);
        var snapshot = new SnapshotContext { Root = _root, Files = [] };

        Assert.Throws<FileNotFoundException>(() => snapshot.InputPath("unlisted.txt"));

        Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes), "Admission must not remove an unlisted file.");
    }

    [Test]
    [Property("FishAudioCaseId", "snapshot-input-path/existing-directory-missing-descendant")]
    public void AnExistingDirectoryStillRequiresItsDeclaredDescendants()
    {
        Directory.CreateDirectory(Path.Combine(_root, "owned-directory"));
        var snapshot = new SnapshotContext
        {
            Root = _root,
            Files = [Entry("owned-directory/input.txt", [0x67, 0x00, 0xf9, 0x0a])],
        };

        Assert.Throws<FileNotFoundException>(() => snapshot.InputPath("owned-directory"));
    }

    [TestCaseSource(nameof(EscapeCases))]
    public void APathOutsideTheOwnedRootIsRejected(string form)
    {
        var snapshot = new SnapshotContext { Root = _root, Files = [] };
        var relative = form switch
        {
            "parent-forward" => "../outside.cs",
            "parent-backslash" => @"..\outside.cs",
            "absolute" => Path.Combine(_parent, "outside.cs"),
            _ => throw new InvalidOperationException("snapshot-input-path-fixture-escape-form"),
        };

        Assert.Throws<InvalidDataException>(() => snapshot.InputPath(relative));
    }

    private static IEnumerable<TestCaseData> EscapeCases()
    {
        yield return new TestCaseData("parent-forward")
            .SetName("InputPath_RejectsParentForward")
            .SetProperty("FishAudioCaseId", "snapshot-input-path/rejects-parent-forward");
        yield return new TestCaseData("parent-backslash")
            .SetName("InputPath_RejectsParentBackslash")
            .SetProperty("FishAudioCaseId", "snapshot-input-path/rejects-parent-backslash");
        yield return new TestCaseData("absolute")
            .SetName("InputPath_RejectsAbsolutePath")
            .SetProperty("FishAudioCaseId", "snapshot-input-path/rejects-absolute");
    }

    private static SnapshotFile Entry(string relative, byte[] expectedBytes) => new()
    {
        Path = relative,
        Blob = new string('c', 40),
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(expectedBytes)),
    };

    private void RequireOwnedDirectory()
    {
        var full = Path.GetFullPath(_root);
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(_root) ||
            !Path.GetDirectoryName(full)!.Equals(_parent, StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(OwnedPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[OwnedPrefix.Length..], "N", out _) ||
            !Directory.Exists(full) ||
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("snapshot-input-path-fixture-owner-changed");
    }
}
