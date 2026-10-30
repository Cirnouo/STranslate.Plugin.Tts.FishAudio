using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
public sealed class PeMethodInventoryTests
{
    private const string FixturePrefix = "fa-pe-method-";
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
                    TestContext.Progress.WriteLine("pe-method-inventory: failed fixture retained in its original owned temporary directory.");
                    return;
                }
                if (!Path.IsPathFullyQualified(configured) || !Directory.Exists(configured))
                    throw new InvalidOperationException("pe-method-inventory-retention-root-invalid");
                var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
                RequireNoReparseAncestors(parent);
                if (parent.Equals(_root, StringComparison.OrdinalIgnoreCase) ||
                    parent.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("pe-method-inventory-retention-overlap");
                CopyEvidence(_root, Path.Combine(parent, "pe-method-inventory-" + Guid.NewGuid().ToString("N")));
                TestContext.Progress.WriteLine("pe-method-inventory: failed fixture copied to its private evidence root.");
            }
            RequireRegularTree(_root);
            Directory.Delete(_root, recursive: true);
        }
        catch when (failed)
        {
            // Keep the primary assertion and all original files on any retention failure.
            TestContext.Progress.WriteLine("pe-method-inventory: retention or cleanup failed; original evidence remains owned.");
        }
        catch
        {
            throw new InvalidOperationException("pe-method-inventory-fixture-cleanup-failed");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "pe-method-inventory/ordinary-overloads")]
    public void OrdinaryOverloadsKeepTheirSignaturesAndExternalAssemblyScopes()
    {
        var scopeA = Emit("ScopeA", """
            [assembly: System.Reflection.AssemblyVersion("1.2.3.4")]
            namespace Collision;
            public sealed class Payload { }
            """);
        var scopeB = Emit("ScopeB", """
            [assembly: System.Reflection.AssemblyVersion("5.6.7.8")]
            namespace Collision;
            public sealed class Payload { }
            """);
        var fixture = Emit("Overloads", """
            extern alias ScopeA;
            extern alias ScopeB;
            namespace PeFixture;
            public static class Probe
            {
                public static int Echo(int value) => value;
                public static string Echo(string value) => value;
                public static int Scoped(ScopeA::Collision.Payload value) => 1;
                public static int Scoped(ScopeB::Collision.Payload value) => 2;
            }
            """, [Alias(scopeA, "ScopeA"), Alias(scopeB, "ScopeB")]);
        var before = new[] { Hash(fixture.Dll), Hash(fixture.Pdb) };

        var evidence = Read(fixture);

        AssertInventory(evidence, fixture);
        var echoes = evidence.Methods.Where(item => item.DeclaringType == "PeFixture.Probe" && item.MetadataName == "Echo").ToArray();
        Assert.That(echoes, Has.Length.EqualTo(2));
        Assert.That(echoes.Select(item => item.MetadataToken).Distinct().Count(), Is.EqualTo(2));
        Assert.That(echoes.Select(item => item.SignatureBlobHex).Distinct().Count(), Is.EqualTo(2));
        foreach (var primitive in new[] { PrimitiveTypeCode.Int32, PrimitiveTypeCode.String })
        {
            var method = echoes.Single(item => item.Signature!.ParameterTypes.Single().Primitive == primitive);
            AssertPrimitive(method.Signature!.ParameterTypes.Single(), primitive);
            AssertPrimitive(method.Signature.ReturnType, primitive);
            Assert.That(method.Signature.CallingConvention, Is.EqualTo(SignatureCallingConvention.Default));
            Assert.That(method.Signature.HeaderRawValue, Is.EqualTo((byte)0x00));
            Assert.That(method.Signature.IsInstance || method.Signature.HasExplicitThis, Is.False);
            Assert.That((method.Attributes & MethodAttributes.Static) != 0, Is.True);
        }
        var scoped = evidence.Methods.Where(item => item.DeclaringType == "PeFixture.Probe" && item.MetadataName == "Scoped").ToArray();
        Assert.That(scoped, Has.Length.EqualTo(2));
        foreach (var (assembly, version) in new[] { (scopeA, new Version(1, 2, 3, 4)), (scopeB, new Version(5, 6, 7, 8)) })
        {
            var parameter = scoped.Single(item => item.Signature!.ParameterTypes.Single().NamedType!.Scope.Assembly!.Name == assembly.AssemblyName)
                .Signature!.ParameterTypes.Single();
            Assert.That(parameter.Kind, Is.EqualTo(PeSignatureTypeKind.Named));
            Assert.That(parameter.NamedType!.Namespace, Is.EqualTo("Collision"));
            Assert.That(parameter.NamedType.TypeNames, Is.EqualTo(new[] { "Payload" }));
            Assert.That(parameter.NamedType.Scope.Kind, Is.EqualTo(PeTypeResolutionScopeKind.AssemblyReference));
            Assert.That(parameter.NamedType.Scope.Assembly!.Version, Is.EqualTo(version));
            Assert.That(parameter.NamedType.Scope.Assembly.Culture, Is.Empty);
            Assert.That(parameter.NamedType.Scope.Assembly.PublicKeyOrTokenHex, Is.Empty);
        }
        Assert.That(new[] { Hash(fixture.Dll), Hash(fixture.Pdb) }, Is.EqualTo(before));
    }

    [Test]
    [Property("FishAudioCaseId", "pe-method-inventory/constructors-and-accessors")]
    public void ConstructorsAndRealAccessorRelationshipsRemainDistinctWithoutLoadingTheAssembly()
    {
        var fixture = Emit("Accessors", """
            namespace PeFixture;
            public sealed class Owner
            {
                static Owner() { throw new System.InvalidOperationException("fixture-must-not-execute"); }
                public Owner() { throw new System.InvalidOperationException("fixture-must-not-execute"); }
                public int Value { get; set; }
                public int this[int index] { get => index; set { } }
                public event System.Action Changed { add { } remove { } }
                public int get_Lookalike() => 9;
            }
            """);
        Assert.That(IsLoaded(fixture.AssemblyName), Is.False);

        var evidence = Read(fixture);

        AssertInventory(evidence, fixture);
        Assert.That(IsLoaded(fixture.AssemblyName), Is.False, "The emitted assembly must remain metadata, including its throwing constructors.");
        var instance = Method(evidence, "PeFixture.Owner", ".ctor");
        var typeInitializer = Method(evidence, "PeFixture.Owner", ".cctor");
        Assert.That(instance.MetadataToken, Is.Not.EqualTo(typeInitializer.MetadataToken));
        Assert.That(instance.Signature!.IsInstance, Is.True);
        Assert.That(typeInitializer.Signature!.IsInstance, Is.False);
        Assert.That((instance.Attributes & MethodAttributes.Static) == 0, Is.True);
        Assert.That((typeInitializer.Attributes & MethodAttributes.Static) != 0, Is.True);
        AssertPrimitive(instance.Signature.ReturnType, PrimitiveTypeCode.Void);
        AssertPrimitive(typeInitializer.Signature.ReturnType, PrimitiveTypeCode.Void);
        Assert.That(instance.Signature.ParameterTypes, Is.Empty);
        Assert.That(typeInitializer.Signature.ParameterTypes, Is.Empty);
        RequireAccessor(evidence, "get_Value", PeAccessorOwnerKind.Property, "Value", PeAccessorKind.Getter);
        RequireAccessor(evidence, "set_Value", PeAccessorOwnerKind.Property, "Value", PeAccessorKind.Setter);
        RequireAccessor(evidence, "get_Item", PeAccessorOwnerKind.Property, "Item", PeAccessorKind.Getter);
        RequireAccessor(evidence, "set_Item", PeAccessorOwnerKind.Property, "Item", PeAccessorKind.Setter);
        RequireAccessor(evidence, "add_Changed", PeAccessorOwnerKind.Event, "Changed", PeAccessorKind.Add);
        RequireAccessor(evidence, "remove_Changed", PeAccessorOwnerKind.Event, "Changed", PeAccessorKind.Remove);
        var valueOwner = Method(evidence, "PeFixture.Owner", "get_Value").Accessors.Single().OwnerToken;
        var itemOwner = Method(evidence, "PeFixture.Owner", "get_Item").Accessors.Single().OwnerToken;
        Assert.That(Method(evidence, "PeFixture.Owner", "set_Value").Accessors.Single().OwnerToken, Is.EqualTo(valueOwner));
        Assert.That(Method(evidence, "PeFixture.Owner", "set_Item").Accessors.Single().OwnerToken, Is.EqualTo(itemOwner));
        Assert.That(valueOwner, Is.Not.EqualTo(itemOwner));
        Assert.That(Method(evidence, "PeFixture.Owner", "add_Changed").Accessors.Single().OwnerToken,
            Is.EqualTo(Method(evidence, "PeFixture.Owner", "remove_Changed").Accessors.Single().OwnerToken));
        Assert.That(Method(evidence, "PeFixture.Owner", "get_Lookalike").Accessors, Is.Empty,
            "A method name resembling an accessor is not a MethodSemantics relationship.");
    }

    [Test]
    [Property("FishAudioCaseId", "pe-method-inventory/generic-ref-out-signatures")]
    public void GenericPositionsByReferenceTypesAndOutMetadataAreNotCollapsed()
    {
        var fixture = Emit("Generic", """
            namespace PeFixture;
            public sealed class Container<T>
            {
                public TResult Transform<TResult>(T item, ref int counter, out string text) where TResult : class
                {
                    counter++;
                    text = "retained UTF-8: λ";
                    return default!;
                }
                public int[] Copy(int[,] values) => new int[0];
            }
            """);

        var evidence = Read(fixture);

        AssertInventory(evidence, fixture);
        var method = Method(evidence, "PeFixture.Container`1", "Transform");
        var signature = method.Signature!;
        Assert.That(signature.IsInstance, Is.True);
        Assert.That(signature.HeaderRawValue, Is.EqualTo((byte)0x30));
        Assert.That(signature.GenericParameterCount, Is.EqualTo(1));
        Assert.That(signature.RequiredParameterCount, Is.EqualTo(3));
        Assert.That(signature.ParameterTypes, Has.Count.EqualTo(3));
        Assert.That(signature.ReturnType.Kind, Is.EqualTo(PeSignatureTypeKind.MethodParameter));
        Assert.That(signature.ReturnType.GenericParameterIndex, Is.EqualTo(0));
        Assert.That(signature.ParameterTypes[0].Kind, Is.EqualTo(PeSignatureTypeKind.TypeParameter));
        Assert.That(signature.ParameterTypes[0].GenericParameterIndex, Is.EqualTo(0));
        Assert.That(signature.ParameterTypes[1].Kind, Is.EqualTo(PeSignatureTypeKind.ByReference));
        AssertPrimitive(signature.ParameterTypes[1].ElementType!, PrimitiveTypeCode.Int32);
        Assert.That(signature.ParameterTypes[2].Kind, Is.EqualTo(PeSignatureTypeKind.ByReference));
        AssertPrimitive(signature.ParameterTypes[2].ElementType!, PrimitiveTypeCode.String);
        // ECMA signature: generic+instance, arity 1, three parameters,
        // !!0 return, !0 input, int32& and string&. Out is separate Param metadata.
        Assert.That(method.SignatureBlobHex, Is.EqualTo("3001031e0013001008100e"));
        Assert.That(method.ParameterMetadata.Single(item => item.Sequence == 2).Attributes & ParameterAttributes.Out, Is.EqualTo(ParameterAttributes.None));
        var output = method.ParameterMetadata.Single(item => item.Sequence == 3);
        Assert.That(output.Name, Is.EqualTo("text"));
        Assert.That((output.Attributes & ParameterAttributes.Out) != 0, Is.True);
        Assert.That(method.GenericParameters.Single().Name, Is.EqualTo("TResult"));
        Assert.That(method.GenericParameters.Single().Attributes & GenericParameterAttributes.ReferenceTypeConstraint,
            Is.EqualTo(GenericParameterAttributes.ReferenceTypeConstraint));
        Assert.That(method.DeclaringTypeGenericParameters.Single().Name, Is.EqualTo("T"));
        var copy = Method(evidence, "PeFixture.Container`1", "Copy").Signature!;
        Assert.That(copy.ReturnType.Kind, Is.EqualTo(PeSignatureTypeKind.SzArray));
        AssertPrimitive(copy.ReturnType.ElementType!, PrimitiveTypeCode.Int32);
        Assert.That(copy.ParameterTypes.Single().Kind, Is.EqualTo(PeSignatureTypeKind.Array));
        Assert.That(copy.ParameterTypes.Single().ArrayShape!.Rank, Is.EqualTo(2));
        AssertPrimitive(copy.ParameterTypes.Single().ElementType!, PrimitiveTypeCode.Int32);
    }

    [Test]
    [Property("FishAudioCaseId", "pe-method-inventory/async-and-generated-methods")]
    public void PortablePdbKickoffIsRecordedWhileSeparateLambdaMethodsStaySeparate()
    {
        var fixture = Emit("Async", """
            namespace PeFixture;
            public static class AsyncOwner
            {
                public static async System.Threading.Tasks.Task<int> ComputeAsync(int value)
                {
                    System.Func<int, int> add = item => item + 1;
                    System.Func<int, int> twice = item => item * 2;
                    await System.Threading.Tasks.Task.Yield();
                    return add(value) + twice(value);
                }
            }
            """);

        var evidence = Read(fixture);

        AssertInventory(evidence, fixture);
        var kickoff = Method(evidence, "PeFixture.AsyncOwner", "ComputeAsync");
        var moveNext = evidence.Methods.Single(item => item.StateMachineKickoffMethodToken == kickoff.MetadataToken);
        Assert.That(moveNext.MetadataName, Is.EqualTo("MoveNext"));
        Assert.That(moveNext.MetadataToken, Is.Not.EqualTo(kickoff.MetadataToken));
        Assert.That(moveNext.DeclaringTypeToken, Is.Not.EqualTo(kickoff.DeclaringTypeToken));
        Assert.That(moveNext.Signature!.IsInstance, Is.True);
        Assert.That(kickoff.StateMachineKickoffMethodToken, Is.Null);
        var lambdaMethods = evidence.Methods.Where(item => item.DeclaringType != "PeFixture.AsyncOwner" &&
            item.Signature is { ParameterTypes.Count: 1 } signature &&
            signature.ParameterTypes[0].Kind == PeSignatureTypeKind.Primitive &&
            signature.ParameterTypes[0].Primitive == PrimitiveTypeCode.Int32 &&
            signature.ReturnType.Kind == PeSignatureTypeKind.Primitive &&
            signature.ReturnType.Primitive == PrimitiveTypeCode.Int32).ToArray();
        Assert.That(lambdaMethods, Has.Length.EqualTo(2));
        Assert.That(lambdaMethods.Select(item => item.MetadataToken).Distinct().Count(), Is.EqualTo(2));
        Assert.That(lambdaMethods.All(item => item.StateMachineKickoffMethodToken is null), Is.True);
        Assert.That(evidence.SourceMappingsResolved, Is.False,
            "Two recorded generated methods do not identify their source lambda expressions.");
    }

    [Test]
    [Property("FishAudioCaseId", "pe-method-inventory/bodyless-definitions-retained")]
    public void InterfaceAndAbstractDefinitionsRemainInventoryWithoutClaimingSourceMappings()
    {
        var fixture = Emit("Bodyless", """
            namespace PeFixture;
            public interface IContract { int Read(int value); }
            public abstract class Base : IContract
            {
                public abstract int Read(int value);
                public abstract string Name { get; }
                public static int Concrete(int value) => value + 1;
            }
            """);

        var evidence = Read(fixture);

        AssertInventory(evidence, fixture);
        var contract = Method(evidence, "PeFixture.IContract", "Read");
        var implementation = Method(evidence, "PeFixture.Base", "Read");
        Assert.That(contract.MetadataToken, Is.Not.EqualTo(implementation.MetadataToken));
        Assert.That(contract.DeclaringTypeToken, Is.Not.EqualTo(implementation.DeclaringTypeToken));
        foreach (var method in new[] { contract, implementation, Method(evidence, "PeFixture.Base", "get_Name") })
        {
            Assert.That(method.RelativeVirtualAddress, Is.Zero);
            Assert.That((method.Attributes & MethodAttributes.Abstract) != 0, Is.True);
            Assert.That(method.Signature, Is.Not.Null);
            Assert.That(method.Signature!.IsInstance, Is.True);
        }
        Assert.That(Method(evidence, "PeFixture.Base", "get_Name").Accessors.Single().OwnerName, Is.EqualTo("Name"));
        Assert.That(Method(evidence, "PeFixture.Base", "Concrete").RelativeVirtualAddress, Is.GreaterThan(0));
    }

    [Test]
    [Property("FishAudioCaseId", "pe-method-inventory/binary-identity-boundary")]
    public void WrongPdbAndAnotherValidPairCannotSatisfyTheExpectedAssembly()
    {
        var first = Emit("IdentityA", "namespace PeFixture; public static class Probe { public static int Read(int value) => value + 1; }");
        var second = Emit("IdentityB", "namespace PeFixture; public static class Probe { public static int Read(int value) => value + 2; } // different source checksum");
        Assert.That(first.Identity.PdbSha256, Is.Not.EqualTo(second.Identity.PdbSha256));
        Assert.That(first.Identity.PortablePdbId, Is.Not.EqualTo(second.Identity.PortablePdbId));

        Assert.Multiple(() =>
        {
            var mismatch = Assert.Throws<BinaryIdentityException>(() =>
                PeMethodInventory.ReadValidated(first.Dll, second.Pdb, first.Identity));
            if (mismatch is not null) Assert.That(mismatch.Code, Is.EqualTo("binary-dll-pdb-mismatch"));
            var foreign = Assert.Throws<PeMethodInventoryException>(() =>
                PeMethodInventory.ReadValidated(second.Dll, second.Pdb, first.Identity));
            if (foreign is not null)
            {
                Assert.That(foreign.Code, Is.EqualTo("pe-method-inventory-assembly-mismatch"));
                Assert.That(foreign.Message, Is.EqualTo(foreign.Code));
                Assert.That(foreign.InnerException, Is.Null);
            }
        });
    }

    [Test]
    [Property("FishAudioCaseId", "pe-method-inventory/malformed-signature-rejected")]
    public void DamagedSignatureIsRejectedUsingTheActualChangedDllIdentity()
    {
        var original = Emit("Corrupt", "namespace PeFixture; public static class Probe { public static int CorruptMe(int value) => value + 1; }");
        var before = new[] { Hash(original.Dll), Hash(original.Pdb) };
        var bytes = File.ReadAllBytes(original.Dll);
        int signatureStart;
        int methodToken;
        using (var image = new MemoryStream(bytes, writable: false))
        using (var pe = new PEReader(image))
        {
            var metadata = pe.GetMetadataReader();
            var handle = metadata.MethodDefinitions.Single(candidate =>
                metadata.GetString(metadata.GetMethodDefinition(candidate).Name) == "CorruptMe");
            var definition = metadata.GetMethodDefinition(handle);
            var signature = metadata.GetBlobBytes(definition.Signature);
            Assert.That(Convert.ToHexStringLower(signature), Is.EqualTo("00010808"));
            var blobStart = checked(pe.PEHeaders.MetadataStartOffset + metadata.GetHeapMetadataOffset(HeapIndex.Blob) +
                MetadataTokens.GetHeapOffset(definition.Signature));
            Assert.That(bytes[blobStart], Is.EqualTo(signature.Length), "This four-byte fixture uses a one-byte blob-length prefix.");
            signatureStart = blobStart + 1;
            Assert.That(bytes.AsSpan(signatureStart, signature.Length).SequenceEqual(signature), Is.True);
            methodToken = MetadataTokens.GetToken(handle);
        }
        bytes[signatureStart] = 0xff; // Invalid method-signature header; leave CodeView and PDB untouched.
        var corruptDll = Path.Combine(Path.GetDirectoryName(original.Dll)!, "corrupt.dll");
        WriteNew(corruptDll, bytes);
        var actualChangedIdentity = BinaryIdentity.ReadValidated(corruptDll, original.Pdb);
        Assert.That(actualChangedIdentity.DllSha256, Is.Not.EqualTo(original.Identity.DllSha256));
        Assert.That(actualChangedIdentity.Mvid, Is.EqualTo(original.Identity.Mvid));
        Assert.That(actualChangedIdentity.PdbSha256, Is.EqualTo(original.Identity.PdbSha256));
        Assert.That(actualChangedIdentity.PortablePdbId, Is.EqualTo(original.Identity.PortablePdbId));
        WriteJson(Path.Combine(Path.GetDirectoryName(original.Dll)!, "corruption.json"), new
        {
            methodToken, signatureStart, originalSignatureBlobHex = "00010808", changedSignatureBlobHex = "ff010808",
            original = original.Identity, changed = actualChangedIdentity,
        });

        var error = Assert.Throws<PeMethodInventoryException>(() =>
            PeMethodInventory.ReadValidated(corruptDll, original.Pdb, actualChangedIdentity));

        Assert.That(error!.Code, Is.EqualTo("pe-method-inventory-invalid-metadata"));
        Assert.That(error.Message, Is.EqualTo(error.Code));
        Assert.That(error.InnerException, Is.Null);
        Assert.That(new[] { Hash(original.Dll), Hash(original.Pdb) }, Is.EqualTo(before));
    }

    [Test]
    [Property("FishAudioCaseId", "pe-method-inventory/repeated-array-shape-budget")]
    public void RepeatedPropertyShapesConsumeOneAggregateParsingBudget()
    {
        const int rank = 65_535;
        const int sizeCount = 65_500;
        const int repeatedProperties = 17;
        const int signatureBytes = 65_513;

        // A matching one-property image proves that the shape and portable-PDB
        // pair are individually readable, rather than relying on malformed data.
        var control = EmitShapeFixture("ShapeControl", 1);
        var controlEvidence = Read(control);
        AssertInventory(controlEvidence, control);
        var controlMethod = Method(controlEvidence, "PeFixture.IShapeOwner", "get_Shared");
        Assert.That(controlMethod.Accessors, Has.Count.EqualTo(1));
        Assert.That(controlMethod.Signature!.ReturnType.ArrayShape!.Rank, Is.EqualTo(rank));
        Assert.That(controlMethod.Signature.ReturnType.ArrayShape.Sizes, Has.Count.EqualTo(sizeCount));
        Assert.That(controlMethod.Signature.ReturnType.ArrayShape.Sizes.All(size => size == 1), Is.True);
        Assert.That(controlMethod.Signature.ReturnType.ArrayShape.LowerBounds, Is.Empty);

        var repeated = EmitShapeFixture("RepeatedShape", repeatedProperties);
        Assert.That(IsLoaded(repeated.AssemblyName), Is.False);
        using (var image = File.OpenRead(repeated.Dll))
        using (var pe = new PEReader(image))
        {
            var metadata = pe.GetMetadataReader();
            var properties = metadata.PropertyDefinitions.ToArray();
            Assert.That(properties, Has.Length.EqualTo(repeatedProperties));
            Assert.That(properties.Select(handle => metadata.GetPropertyDefinition(handle).Signature).Distinct().Count(), Is.EqualTo(1));
            Assert.That(metadata.GetTableRowCount(TableIndex.MethodSemantics), Is.EqualTo(repeatedProperties));
            Assert.That(properties.All(handle => metadata.GetPropertyDefinition(handle).GetAccessors().Getter ==
                MetadataTokens.MethodDefinitionHandle(1)), Is.True);
            var shared = metadata.GetBlobReader(metadata.GetPropertyDefinition(properties[0]).Signature);
            Assert.That(shared.Length, Is.EqualTo(signatureBytes).And.LessThan(64 * 1024));
            Assert.That(shared.ReadByte(), Is.EqualTo(0x28)); // Instance PropertySig.
            Assert.That(shared.ReadCompressedInteger(), Is.Zero); // No index parameters.
            Assert.That(shared.ReadByte(), Is.EqualTo((byte)SignatureTypeCode.Array));
            Assert.That(shared.ReadByte(), Is.EqualTo((byte)SignatureTypeCode.Int32));
            Assert.That(shared.ReadCompressedInteger(), Is.EqualTo(rank));
            var actualSizeCount = shared.ReadCompressedInteger();
            Assert.That(actualSizeCount, Is.EqualTo(sizeCount));
            Assert.That(checked((long)properties.Length * actualSizeCount), Is.GreaterThan(1_000_000),
                "The 17 property rows alone require 1,113,500 decoded shape entries from one shared blob.");
        }

        var error = Assert.Throws<PeMethodInventoryException>(() => Read(repeated),
            "repeated-array-shapes-must-consume-the-aggregate-budget");

        Assert.That(error!.Code, Is.EqualTo("pe-method-inventory-limit"));
        Assert.That(error.Message, Is.EqualTo(error.Code));
        Assert.That(error.InnerException, Is.Null);
        Assert.That(IsLoaded(repeated.AssemblyName), Is.False);

        EmittedFixture EmitShapeFixture(string label, int propertyCount)
        {
            var assemblyName = "PeInventory_" + label + "_" + Guid.NewGuid().ToString("N");
            var directory = Path.Combine(_root, assemblyName);
            Directory.CreateDirectory(directory);
            var metadata = new MetadataBuilder();
            metadata.AddModule(0, metadata.GetOrAddString("image.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
            metadata.AddAssembly(metadata.GetOrAddString(assemblyName), new Version(1, 0, 0, 0),
                default, default, (AssemblyFlags)0, AssemblyHashAlgorithm.None);
            metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"),
                default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            var owner = metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
                metadata.GetOrAddString("PeFixture"), metadata.GetOrAddString("IShapeOwner"),
                default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            var getter = metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Abstract |
                MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                MethodImplAttributes.IL, metadata.GetOrAddString("get_Shared"), ShapeSignature(metadata, 0x20),
                bodyOffset: -1, parameterList: MetadataTokens.ParameterHandle(1));
            var propertySignature = ShapeSignature(metadata, 0x28);
            for (var index = 0; index < propertyCount; index++)
            {
                var property = metadata.AddProperty(PropertyAttributes.None, metadata.GetOrAddString("Shape" + index), propertySignature);
                metadata.AddMethodSemantics(property, MethodSemanticsAttributes.Getter, getter);
            }
            metadata.AddPropertyMap(owner, MetadataTokens.PropertyDefinitionHandle(1));

            var pdbMetadata = new MetadataBuilder();
            pdbMetadata.AddMethodDebugInformation(default, default);
            var pdbBuilder = new PortablePdbBuilder(pdbMetadata, metadata.GetRowCounts(), default);
            var pdbImage = new BlobBuilder();
            var pdbId = pdbBuilder.Serialize(pdbImage);
            var debugDirectory = new DebugDirectoryBuilder();
            debugDirectory.AddCodeViewEntry("fixture.pdb", pdbId, pdbBuilder.FormatVersion);
            var peBuilder = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(metadata), new BlobBuilder(), debugDirectoryBuilder: debugDirectory,
                strongNameSignatureSize: 0);
            var peImage = new BlobBuilder();
            peBuilder.Serialize(peImage);
            Assert.That(peImage.Count, Is.LessThan(256 * 1024), "This bounded fixture must remain a small image.");
            Assert.That(pdbImage.Count, Is.LessThan(16 * 1024));

            var dll = Path.Combine(directory, "image.dll");
            var pdb = Path.Combine(directory, "symbols.pdb");
            WriteNew(dll, peImage.ToArray());
            WriteNew(pdb, pdbImage.ToArray());
            var identity = BinaryIdentity.ReadValidated(dll, pdb);
            WriteJson(Path.Combine(directory, "identity.json"), identity);
            WriteJson(Path.Combine(directory, "shape-fixture.json"), new
            {
                propertyCount, rank, sizeCount, signatureBytes,
                propertyShapeEntries = checked((long)propertyCount * sizeCount),
            });
            return new(assemblyName, dll, pdb, identity);
        }

        BlobHandle ShapeSignature(MetadataBuilder metadata, byte header)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(header);
            signature.WriteCompressedInteger(0);
            signature.WriteByte((byte)SignatureTypeCode.Array);
            signature.WriteByte((byte)SignatureTypeCode.Int32);
            signature.WriteCompressedInteger(rank);
            signature.WriteCompressedInteger(sizeCount);
            for (var index = 0; index < sizeCount; index++) signature.WriteCompressedInteger(1);
            signature.WriteCompressedInteger(0);
            Assert.That(signature.Count, Is.EqualTo(signatureBytes));
            return metadata.GetOrAddBlob(signature);
        }
    }

    private static PeMethodInventoryEvidence Read(EmittedFixture fixture) =>
        PeMethodInventory.ReadValidated(fixture.Dll, fixture.Pdb, fixture.Identity);

    private static PeMethodRecord Method(PeMethodInventoryEvidence evidence, string type, string name) =>
        evidence.Methods.Single(item => item.DeclaringType == type && item.MetadataName == name);

    private static void AssertInventory(PeMethodInventoryEvidence evidence, EmittedFixture fixture)
    {
        Assert.That(evidence.SchemaVersion, Is.EqualTo(1));
        Assert.That(evidence.Assembly, Is.EqualTo(fixture.Identity));
        Assert.That(evidence.Methods, Is.Not.Empty);
        Assert.That(evidence.Methods.Select(item => item.MetadataToken).Distinct().Count(), Is.EqualTo(evidence.Methods.Count));
        foreach (var method in evidence.Methods)
        {
            Assert.That(method.MetadataToken & unchecked((int)0xff000000), Is.EqualTo(0x06000000));
            Assert.That(method.DeclaringTypeToken & unchecked((int)0xff000000), Is.EqualTo(0x02000000));
            Assert.That(method.SignatureBlobHex, Does.Match("^([a-f0-9]{2})+$"));
            Assert.That(method.UnresolvedReasons, Is.Empty, "These hand-written fixtures use supported signatures.");
        }
        Assert.That(evidence.BuildInputClosureProven, Is.False);
        Assert.That(evidence.GeneratorInputClosureProven, Is.False);
        Assert.That(evidence.SourceMappingsResolved, Is.False);
        Assert.That(evidence.CompleteBaseline, Is.False);
    }

    private static void AssertPrimitive(PeSignatureType type, PrimitiveTypeCode expected)
    {
        Assert.That(type.Kind, Is.EqualTo(PeSignatureTypeKind.Primitive));
        Assert.That(type.Primitive, Is.EqualTo(expected));
    }

    private static void RequireAccessor(PeMethodInventoryEvidence evidence, string methodName,
        PeAccessorOwnerKind ownerKind, string ownerName, PeAccessorKind kind)
    {
        var relation = Method(evidence, "PeFixture.Owner", methodName).Accessors.Single();
        Assert.That(relation.OwnerKind, Is.EqualTo(ownerKind));
        Assert.That(relation.OwnerName, Is.EqualTo(ownerName));
        Assert.That(relation.Kind, Is.EqualTo(kind));
        Assert.That(relation.OwnerToken & unchecked((int)0xff000000),
            Is.EqualTo(ownerKind == PeAccessorOwnerKind.Property ? 0x17000000 : 0x14000000));
    }

    private EmittedFixture Emit(string label, string source, IReadOnlyList<MetadataReference>? extraReferences = null)
    {
        var assemblyName = "PeInventory_" + label + "_" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(_root, assemblyName);
        Directory.CreateDirectory(directory);
        var sourceBytes = Utf8.GetBytes(source);
        WriteNew(Path.Combine(directory, "Fixture.cs"), sourceBytes);
        using var sourceStream = new MemoryStream(sourceBytes, writable: false);
        var text = SourceText.From(sourceStream, Utf8, SourceHashAlgorithm.Sha256);
        var tree = CSharpSyntaxTree.ParseText(text, path: "/_pe-fixture/" + assemblyName + "/Fixture.cs");
        var references = new List<MetadataReference> { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        if (extraReferences is not null) references.AddRange(extraReferences);
        var compilation = CSharpCompilation.Create(assemblyName, [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug,
                deterministic: true).WithNullableContextOptions(NullableContextOptions.Enable));
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
        WriteJson(Path.Combine(directory, "identity.json"), identity);
        return new(assemblyName, dll, pdb, identity);
    }

    private static MetadataReference Alias(EmittedFixture fixture, string alias) =>
        MetadataReference.CreateFromImage(ImmutableArray.CreateRange(File.ReadAllBytes(fixture.Dll)),
            MetadataReferenceProperties.Assembly.WithAliases(ImmutableArray.Create(alias)));

    private static bool IsLoaded(string assemblyName) =>
        AppDomain.CurrentDomain.GetAssemblies().Any(item => item.GetName().Name == assemblyName);

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void WriteNew(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
    }

    private static void WriteJson<T>(string path, T value) =>
        WriteNew(path, Utf8.GetBytes(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })));

    private void RequireOwnedRoot()
    {
        var full = Path.GetFullPath(_root);
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(_root) || !full.Equals(_root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(FixturePrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(name[FixturePrefix.Length..], "N", out var id) || id == Guid.Empty)
            throw new InvalidOperationException("pe-method-inventory-fixture-owner-changed");
        RequireNoReparseAncestors(full);
    }

    private static void RequireNoReparseAncestors(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("pe-method-inventory-fixture-reparse-or-missing");
    }

    private static void RequireRegularTree(string path, int depth = 0)
    {
        if (depth > 32 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("pe-method-inventory-fixture-tree-invalid");
        foreach (var entry in Directory.GetFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("pe-method-inventory-fixture-reparse");
            if ((attributes & FileAttributes.Directory) != 0) RequireRegularTree(entry, depth + 1);
        }
    }

    private static void CopyEvidence(string source, string destination)
    {
        if (File.Exists(destination) || Directory.Exists(destination) ||
            (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("pe-method-inventory-retention-not-fresh");
        Directory.CreateDirectory(destination);
        RequireNoReparseAncestors(destination);
        foreach (var entry in Directory.GetFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("pe-method-inventory-retention-reparse");
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0) CopyEvidence(entry, target);
            else File.Copy(entry, target, overwrite: false);
        }
    }

    private sealed record EmittedFixture(string AssemblyName, string Dll, string Pdb, BinaryIdentityRecord Identity);
}
