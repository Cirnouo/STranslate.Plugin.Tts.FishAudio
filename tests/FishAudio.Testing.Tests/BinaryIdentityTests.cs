using System.Reflection.PortableExecutable;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class BinaryIdentityTests
{
    private string _root = "";

    [SetUp]
    public void CreateOwnedDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), "fa-binary-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void DeleteOwnedDirectory()
    {
        var full = Path.GetFullPath(_root);
        Assert.That(Path.GetDirectoryName(full), Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetTempPath())).IgnoreCase);
        Assert.That(Path.GetFileName(full), Does.StartWith("fa-binary-tests-"));
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }

    [Test]
    public void ActualMatchedPairReportsAssemblyIdentityDespiteUnrelatedFilenames()
    {
        var pair = CopyToolPair();
        var identity = BinaryIdentity.ReadValidated(pair.Dll, pair.Pdb);
        Assert.That(identity.AssemblyName, Is.EqualTo("FishAudio.Testing"));
        Assert.That(identity.Mvid, Is.EqualTo(typeof(BinaryIdentity).Assembly.ManifestModule.ModuleVersionId));
        Assert.That(identity.Mvid, Is.Not.EqualTo(Guid.Empty));
        Assert.That(identity.DllSha256, Does.Match("^[a-f0-9]{64}$"));
        Assert.That(identity.PdbSha256, Does.Match("^[a-f0-9]{64}$"));
        Assert.That(identity.DllSha256, Is.Not.EqualTo(identity.PdbSha256));
        Assert.That(identity.PortablePdbId, Does.Match("^[a-f0-9]{40}$"));
        Assert.That(identity.PortablePdbId, Is.Not.EqualTo(new string('0', 40)));
    }

    [Test]
    public void DifferentActualAssemblyPdbCannotMatchTheToolDll()
    {
        var pair = CopyToolPair();
        var otherPdb = Path.ChangeExtension(typeof(BinaryIdentityTests).Assembly.Location, ".pdb");
        Assert.That(File.Exists(otherPdb), Is.True, "The freshly built test assembly must supply its portable PDB.");
        var explicitOtherPdb = Path.Combine(_root, "other-symbols.bin");
        File.Copy(otherPdb, explicitOtherPdb);
        Reject(pair.Dll, explicitOtherPdb);
    }

    [Test]
    public void MissingExplicitPdbCannotFallBackToTheDllsRealSibling()
    {
        var actualDll = typeof(BinaryIdentity).Assembly.Location;
        Assert.That(File.Exists(Path.ChangeExtension(actualDll, ".pdb")), Is.True);
        Reject(actualDll, Path.Combine(_root, "missing-private-symbols-marker.pdb"));
    }

    [Test]
    public void TruncatedExplicitPdbCannotEstablishAnIdentity()
    {
        var pair = CopyToolPair();
        using (var output = new FileStream(pair.Pdb, FileMode.Open, FileAccess.Write, FileShare.None)) output.SetLength(12);
        Reject(pair.Dll, pair.Pdb);
    }

    [Test]
    public void CorruptExplicitDllCannotEstablishAnIdentity()
    {
        var pair = CopyToolPair();
        File.WriteAllBytes(pair.Dll, [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
            0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff]);
        Reject(pair.Dll, pair.Pdb);
    }

    [TestCase(false, TestName = "portable-linkage/stamp-mismatch")]
    [TestCase(true, TestName = "portable-linkage/age-mismatch")]
    public void TamperedPortableLinkageCannotMatchByGuidAlone(bool changeAge)
    {
        var pair = CopyToolPair();
        using (var image = new FileStream(pair.Dll, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            long offset;
            using (var pe = new PEReader(image, PEStreamOptions.LeaveOpen))
            {
                var entries = pe.ReadDebugDirectory();
                var index = Enumerable.Range(0, entries.Length).Single(i => entries[i].Type == DebugDirectoryEntryType.CodeView);
                if (changeAge)
                {
                    // RSDS: four-byte signature, sixteen-byte GUID, then age.
                    offset = entries[index].DataPointer + 20;
                }
                else
                {
                    var rva = pe.PEHeaders.PEHeader!.DebugTableDirectory.RelativeVirtualAddress;
                    var section = pe.PEHeaders.SectionHeaders.Single(item => rva >= item.VirtualAddress &&
                        rva - item.VirtualAddress < item.SizeOfRawData);
                    // IMAGE_DEBUG_DIRECTORY is 28 bytes; its stamp starts at +4.
                    offset = section.PointerToRawData + rva - section.VirtualAddress + index * 28 + 4;
                }
            }
            image.Position = offset;
            var original = image.ReadByte();
            Assert.That(original, Is.GreaterThanOrEqualTo(0));
            image.Position = offset;
            image.WriteByte((byte)(original ^ (changeAge ? 2 : 1)));
        }
        var error = Assert.Throws<BinaryIdentityException>(() => BinaryIdentity.ReadValidated(pair.Dll, pair.Pdb))!;
        Assert.That(error.Code, Is.EqualTo("binary-dll-pdb-mismatch"));
    }

    private (string Dll, string Pdb) CopyToolPair()
    {
        var actualDll = typeof(BinaryIdentity).Assembly.Location;
        var actualPdb = Path.ChangeExtension(actualDll, ".pdb");
        Assert.That(File.Exists(actualPdb), Is.True, "The freshly built tool must supply its portable PDB.");
        var dll = Path.Combine(_root, "renamed-image.bin");
        var pdb = Path.Combine(_root, "separately-named-symbols.bin");
        File.Copy(actualDll, dll);
        File.Copy(actualPdb, pdb);
        return (dll, pdb);
    }

    private void Reject(string dll, string pdb)
    {
        var error = Assert.Throws<BinaryIdentityException>(() => BinaryIdentity.ReadValidated(dll, pdb))!;
        Assert.That(error.Code, Does.Match("^binary-[a-z-]+$"));
        Assert.That(error.Message, Is.EqualTo(error.Code));
        Assert.That(error.Message, Does.Not.Contain(_root).And.Not.Contain("private-symbols-marker"));
        Assert.That(error.InnerException, Is.Null);
    }
}
