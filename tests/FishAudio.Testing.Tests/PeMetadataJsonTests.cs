using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FishAudio.Testing.Evidence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class PeMetadataJsonTests
{
    private const string FixturePrefix = "fa-pe-json-";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private string _root = "";

    [SetUp]
    public void CreateOwnedFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), FixturePrefix + Guid.NewGuid().ToString("N"));
        Assert.That(File.Exists(_root) || Directory.Exists(_root), Is.False);
        Directory.CreateDirectory(_root);
        RequireOwnedRoot();
    }

    [TearDown]
    public void PreserveFailureOrDeleteOwnedFixture()
    {
        if (_root.Length == 0) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            RequireOwnedRoot();
            if (!Directory.Exists(_root)) return;
            RequireRegularTree(_root);
            if (failed)
            {
                var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
                if (string.IsNullOrWhiteSpace(configured))
                {
                    TestContext.Progress.WriteLine("pe-metadata-json: failed fixture retained in its owned temporary directory.");
                    return;
                }
                if (!Path.IsPathFullyQualified(configured) || !Directory.Exists(configured))
                    throw new InvalidOperationException("pe-metadata-json-retention-root-invalid");
                var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
                RequireNoReparseAncestors(parent);
                if (parent.Equals(_root, StringComparison.OrdinalIgnoreCase) ||
                    parent.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("pe-metadata-json-retention-overlap");
                CopyEvidence(_root, Path.Combine(parent, "pe-metadata-json-" + Guid.NewGuid().ToString("N")));
                TestContext.Progress.WriteLine("pe-metadata-json: failed fixture copied to its private evidence root.");
            }
            RequireRegularTree(_root);
            Directory.Delete(_root, recursive: true);
        }
        catch when (failed)
        {
            // Preserve the primary assertion and the original bytes on retention failure.
            TestContext.Progress.WriteLine("pe-metadata-json: retention or cleanup failed; original evidence remains owned.");
        }
        catch
        {
            throw new InvalidOperationException("pe-metadata-json-fixture-cleanup-failed");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "pe-metadata-json/emitted-named-scope-round-trips")]
    public void EmittedNamedParameterScopeRoundTripsTheCompleteInventoryWithStrictEnums()
    {
        var dependency = Emit("Dependency", "namespace MetadataJsonFixture; public sealed class Payload { }");
        var subject = Emit("Subject", """
            namespace MetadataJsonFixture;
            public static class Consumer
            {
                public static Payload RoundTrip(Payload value) => value;
            }
            """, MetadataReference.CreateFromFile(dependency.Dll));
        var originalHashes = new[] { Hash(subject.Dll), Hash(subject.Pdb) };
        using (var image = File.OpenRead(subject.Dll))
        using (var pe = new PEReader(image))
        {
            var metadata = pe.GetMetadataReader();
            var actualReference = metadata.AssemblyReferences.Select(metadata.GetAssemblyReference)
                .Single(item => metadata.GetString(item.Name) == dependency.Identity.AssemblyName);
            Assert.That(unchecked((uint)actualReference.Flags), Is.Zero,
                "The unsigned emitted dependency must supply the actual unnamed AssemblyRef zero value.");
        }
        var inventory = PeMethodInventory.ReadValidated(subject.Dll, subject.Pdb, subject.Identity);
        Assert.That(inventory.SchemaVersion, Is.EqualTo(1));
        var method = inventory.Methods.Single(item => item.DeclaringType == "MetadataJsonFixture.Consumer" && item.MetadataName == "RoundTrip");
        var parameter = method.Signature!.ParameterTypes.Single();
        Assert.That(parameter.Kind, Is.EqualTo(PeSignatureTypeKind.Named));
        Assert.That(parameter.NamedType!.TypeNames, Is.EqualTo(new[] { "Payload" }));
        Assert.That(parameter.NamedType.Scope.Kind, Is.EqualTo(PeTypeResolutionScopeKind.AssemblyReference));
        var assembly = parameter.NamedType.Scope.Assembly!;
        Assert.That(assembly.Name, Is.EqualTo(dependency.Identity.AssemblyName));
        Assert.That(unchecked((uint)assembly.Flags), Is.Zero);
        Assert.That(method.Signature.ReturnType.NamedType!.Scope.Assembly, Is.EqualTo(assembly));

        var options = StrictOptions();
        // Schema 1 AssemblyRef flags are lossless UInt32 metadata, not a closed
        // named enum. The global converter must remain strict for other enums.
        var json = JsonSerializer.Serialize(inventory, options);
        var path = Path.Combine(_root, "inventory.json");
        WriteNew(path, Utf8.GetBytes(json));
        var retainedBytes = File.ReadAllBytes(path);
        var reread = JsonSerializer.Deserialize<PeMethodInventoryEvidence>(retainedBytes, options);

        Assert.That(reread, Is.Not.Null);
        Assert.That(JsonSerializer.Serialize(reread, options), Is.EqualTo(json),
            "Every field of the complete inventory must survive storage and reconstruction.");
        Assert.That(reread!.Assembly, Is.EqualTo(subject.Identity));
        Assert.That(reread.Methods.Select(item => item.MetadataToken), Is.EqualTo(inventory.Methods.Select(item => item.MetadataToken)));
        var retainedMethod = reread.Methods.Single(item => item.MetadataToken == method.MetadataToken);
        Assert.That(retainedMethod.SignatureBlobHex, Is.EqualTo(method.SignatureBlobHex));
        Assert.That(retainedMethod.Signature!.ParameterTypes.Single().NamedType!.Scope.Assembly, Is.EqualTo(assembly));
        Assert.That(reread.BuildInputClosureProven || reread.GeneratorInputClosureProven ||
            reread.SourceMappingsResolved || reread.CompleteBaseline, Is.False);
        using var document = JsonDocument.Parse(retainedBytes);
        var rawFlags = document.RootElement.GetProperty("Methods").EnumerateArray()
            .Single(item => item.GetProperty("MetadataToken").GetInt32() == method.MetadataToken)
            .GetProperty("Signature").GetProperty("ParameterTypes")[0]
            .GetProperty("NamedType").GetProperty("Scope").GetProperty("Assembly").GetProperty("Flags");
        Assert.That(rawFlags.ValueKind, Is.EqualTo(JsonValueKind.Number));
        Assert.That(rawFlags.GetUInt32(), Is.Zero);
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(retainedBytes));
        Assert.That(new[] { Hash(subject.Dll), Hash(subject.Pdb) }, Is.EqualTo(originalHashes));
        Assert.That(AppDomain.CurrentDomain.GetAssemblies().Any(item =>
            item.GetName().Name == subject.Identity.AssemblyName || item.GetName().Name == dependency.Identity.AssemblyName), Is.False);
    }

    [Test]
    [Property("FishAudioCaseId", "pe-metadata-json/assembly-flags-preserve-uint32")]
    public void AssemblyReferenceFlagsPreserveZeroKnownAndUnknownUInt32Bits()
    {
        var options = StrictOptions();
        foreach (var raw in new uint[] { 0, 1, 0x80000000, uint.MaxValue })
        {
            var identity = new PeAssemblyReferenceIdentity("RawFlags", new Version(1, 2, 3, 4), "", "", unchecked((AssemblyFlags)raw));
            var json = JsonSerializer.Serialize(identity, options);
            var path = Path.Combine(_root, "flags-" + raw.ToString(CultureInfo.InvariantCulture) + ".json");
            WriteNew(path, Utf8.GetBytes(json));
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var token = document.RootElement.GetProperty("Flags");
            Assert.That(token.ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(token.GetRawText(), Is.EqualTo(raw.ToString(CultureInfo.InvariantCulture)));
            var roundTrip = JsonSerializer.Deserialize<PeAssemblyReferenceIdentity>(File.ReadAllBytes(path), options);
            Assert.That(roundTrip, Is.EqualTo(identity));
            Assert.That(unchecked((uint)roundTrip!.Flags), Is.EqualTo(raw));
        }
    }

    [Test]
    [Property("FishAudioCaseId", "pe-metadata-json/raw-flags-do-not-open-closed-enums")]
    public void RawFlagsRejectNonUInt32WireValuesWithoutOpeningOtherClosedEnums()
    {
        var options = StrictOptions();
        var malformed = new[] { "-1", "4294967296", "\"PublicKey\"", "\"0\"", "0.0", "1e0", "null", "true" };
        for (var index = 0; index < malformed.Length; index++)
        {
            var json = """{"Name":"RawFlags","Version":"1.2.3.4","Culture":"","PublicKeyOrTokenHex":"","Flags":VALUE}"""
                .Replace("VALUE", malformed[index], StringComparison.Ordinal);
            WriteNew(Path.Combine(_root, "invalid-flags-" + index.ToString(CultureInfo.InvariantCulture) + ".json"), Utf8.GetBytes(json));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PeAssemblyReferenceIdentity>(json, options),
                "pe-assembly-flags-v1-requires-an-unquoted-uint32-integer");
        }
        var scope = new PeTypeResolutionScope(PeTypeResolutionScopeKind.AssemblyReference, 0x23000001);
        var scopeJson = JsonSerializer.Serialize(scope, options);
        using (var document = JsonDocument.Parse(scopeJson))
            Assert.That(document.RootElement.GetProperty("Kind").GetString(), Is.EqualTo("AssemblyReference"));
        Assert.That(JsonSerializer.Deserialize<PeTypeResolutionScope>(scopeJson, options), Is.EqualTo(scope));
        foreach (var kind in new[] { "\"NoSuchScope\"", "99", "0" })
        {
            var json = """{"Kind":VALUE,"MetadataToken":1,"Assembly":null,"ModuleName":null}"""
                .Replace("VALUE", kind, StringComparison.Ordinal);
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PeTypeResolutionScope>(json, options),
                "The raw metadata flag exception must not relax closed-enum strings.");
        }
    }

    private static JsonSerializerOptions StrictOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }

    private EmittedFixture Emit(string label, string source, MetadataReference? dependency = null)
    {
        var assemblyName = "PeMetadataJson_" + label + "_" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_root, assemblyName);
        Directory.CreateDirectory(directory);
        var bytes = Utf8.GetBytes(source);
        WriteNew(Path.Combine(directory, "Fixture.cs"), bytes);
        using var sourceStream = new MemoryStream(bytes, writable: false);
        var text = SourceText.From(sourceStream, Utf8, SourceHashAlgorithm.Sha256);
        var syntax = CSharpSyntaxTree.ParseText(text, path: "/_pe-json-fixture/" + assemblyName + "/Fixture.cs");
        var references = new List<MetadataReference> { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        if (dependency is not null) references.Add(dependency);
        var compilation = CSharpCompilation.Create(assemblyName, [syntax], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug, deterministic: true));
        var dll = Path.Combine(directory, "image.dll");
        var pdb = Path.Combine(directory, "symbols.pdb");
        using (var image = new FileStream(dll, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var symbols = new FileStream(pdb, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            var emitted = compilation.Emit(image, symbols,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: "fixture.pdb"));
            Assert.That(emitted.Success, Is.True, "Fixture emit must succeed: " +
                string.Join(",", emitted.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).Select(item => item.Id)));
        }
        var identity = BinaryIdentity.ReadValidated(dll, pdb);
        WriteNew(Path.Combine(directory, "identity.json"), Utf8.GetBytes(JsonSerializer.Serialize(identity)));
        return new(dll, pdb, identity);
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void WriteNew(string path, byte[] bytes)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        output.Write(bytes);
    }

    private void RequireOwnedRoot()
    {
        var full = Path.GetFullPath(_root);
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(_root) || !full.Equals(_root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(FixturePrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[FixturePrefix.Length..], "N", out var id) || id == Guid.Empty)
            throw new InvalidOperationException("pe-metadata-json-fixture-owner-changed");
        RequireNoReparseAncestors(full);
    }

    private static void RequireNoReparseAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("pe-metadata-json-fixture-reparse-or-missing");
    }

    private static void RequireRegularTree(string path, int depth = 0)
    {
        if (depth > 32 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("pe-metadata-json-fixture-tree-invalid");
        foreach (var entry in Directory.GetFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("pe-metadata-json-fixture-reparse");
            if ((attributes & FileAttributes.Directory) != 0) RequireRegularTree(entry, depth + 1);
        }
    }

    private static void CopyEvidence(string source, string destination)
    {
        if (File.Exists(destination) || Directory.Exists(destination) ||
            (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("pe-metadata-json-retention-not-fresh");
        Directory.CreateDirectory(destination);
        RequireNoReparseAncestors(destination);
        foreach (var entry in Directory.GetFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("pe-metadata-json-retention-reparse");
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0) CopyEvidence(entry, target);
            else File.Copy(entry, target, overwrite: false);
        }
    }

    private sealed record EmittedFixture(string Dll, string Pdb, BinaryIdentityRecord Identity);
}
