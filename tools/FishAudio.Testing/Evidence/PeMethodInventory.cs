using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace FishAudio.Testing.Evidence;

public enum PeSignatureTypeKind
{
    Primitive, Named, TypeParameter, MethodParameter, GenericInstantiation,
    ByReference, Pointer, SzArray, Array, Modified, Pinned, FunctionPointer, Unresolved,
}

public enum PeTypeResolutionScopeKind { CurrentModule, AssemblyReference, ModuleReference }
public enum PeAccessorOwnerKind { Property, Event }
public enum PeAccessorKind { Getter, Setter, Add, Remove, Raise, Other }

/// <summary>Recorded AssemblyRef identity; it does not authenticate or load the referenced assembly.</summary>
public sealed record PeAssemblyReferenceIdentity(string Name, Version Version, string Culture,
    string PublicKeyOrTokenHex,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(PeAssemblyFlagsUInt32V1JsonConverter))] AssemblyFlags Flags);

public sealed record PeTypeResolutionScope(PeTypeResolutionScopeKind Kind, int MetadataToken,
    PeAssemblyReferenceIdentity? Assembly = null, string? ModuleName = null);

/// <summary>TypeNames retains the outer-to-inner metadata names, including generic arity.</summary>
public sealed record PeNamedTypeIdentity(int MetadataToken, byte RawTypeKind, string Namespace,
    IReadOnlyList<string> TypeNames, PeTypeResolutionScope Scope);

public sealed record PeArrayShape(int Rank, IReadOnlyList<int> Sizes, IReadOnlyList<int> LowerBounds);

/// <summary>
/// A structural metadata type expression, not a display-name or runtime-type key.
/// Modified retains the modifier type, required/optional bit and element type.
/// Unresolved must remain explicit; it cannot participate in a unique semantic join.
/// </summary>
public sealed record PeSignatureType(PeSignatureTypeKind Kind, PrimitiveTypeCode? Primitive = null,
    PeNamedTypeIdentity? NamedType = null, int? GenericParameterIndex = null,
    PeSignatureType? ElementType = null, IReadOnlyList<PeSignatureType>? TypeArguments = null,
    PeArrayShape? ArrayShape = null, bool? IsRequiredModifier = null,
    PeSignatureType? ModifierType = null, PeMethodSignature? FunctionPointerSignature = null);

public sealed record PeMethodSignature(byte HeaderRawValue, SignatureCallingConvention CallingConvention,
    bool IsInstance, bool HasExplicitThis, int GenericParameterCount, int RequiredParameterCount,
    PeSignatureType ReturnType, IReadOnlyList<PeSignatureType> ParameterTypes);

/// <summary>Only actual Param rows are recorded; sequence zero denotes a recorded return parameter.</summary>
public sealed record PeParameterMetadata(int MetadataToken, int Sequence, string Name, ParameterAttributes Attributes);

public sealed record PeGenericParameterMetadata(int Index, string Name, GenericParameterAttributes Attributes,
    IReadOnlyList<PeSignatureType> Constraints);

/// <summary>OwnerToken and accessor kind come from metadata relationships, never name prefixes.</summary>
public sealed record PeAccessorBinding(PeAccessorOwnerKind OwnerKind, int OwnerToken, string OwnerName,
    PeAccessorKind Kind);

/// <summary>
/// MetadataToken is scoped to the enclosing validated binary identity. Raw signatures
/// and names are evidence, not stable identifiers across builds. A null Signature or
/// an Unresolved type requires a fixed UnresolvedReasons entry, not a guessed type.
/// </summary>
public sealed record PeMethodRecord(int MetadataToken, int DeclaringTypeToken, string DeclaringType,
    string MetadataName, MethodAttributes Attributes, MethodImplAttributes ImplementationAttributes,
    int RelativeVirtualAddress, string SignatureBlobHex, PeMethodSignature? Signature,
    IReadOnlyList<PeParameterMetadata> ParameterMetadata,
    IReadOnlyList<PeGenericParameterMetadata> GenericParameters,
    IReadOnlyList<PeGenericParameterMetadata> DeclaringTypeGenericParameters,
    IReadOnlyList<PeAccessorBinding> Accessors, int? StateMachineKickoffMethodToken,
    IReadOnlyList<string> UnresolvedReasons);

public sealed record PeMethodInventoryEvidence(int SchemaVersion, BinaryIdentityRecord Assembly,
    IReadOnlyList<PeMethodRecord> Methods)
{
    public bool BuildInputClosureProven => false;
    public bool GeneratorInputClosureProven => false;
    public bool SourceMappingsResolved => false;
    public bool CompleteBaseline => false;
}

public sealed class PeMethodInventoryException : Exception
{
    internal PeMethodInventoryException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Metadata-only reader of the two explicit caller-owned files. The
/// expected identity is caller supplied; actual DLL/PDB linkage and all identity
/// fields must be checked under held read guards before returning copied records.
/// No assembly execution, source lookup or coverage-symbol correspondence is implied.
/// </summary>
public static class PeMethodInventory
{
    private const int MaximumMethods = 100_000;
    private const int MaximumMetadataBytes = 64 * 1024 * 1024;
    private const int MaximumTextCharacters = 16 * 1024 * 1024;
    private const int MaximumSignatureBytes = 64 * 1024;
    private const int MaximumSignatureNodes = 1_000_000;
    private const int MaximumDepth = 128;
    private const int MaximumParameters = 65_535;
    private const int MaximumGenericParameters = 4096;

    public static PeMethodInventoryEvidence ReadValidated(string absoluteDll, string absolutePortablePdb,
        BinaryIdentityRecord expectedAssembly)
    {
        try
        {
            Require(expectedAssembly is not null && expectedAssembly.Mvid != Guid.Empty &&
                expectedAssembly.AssemblyName is { Length: > 0 and <= 1024 } &&
                !expectedAssembly.AssemblyName.Any(char.IsControl) && Hash(expectedAssembly.DllSha256, 64) &&
                Hash(expectedAssembly.PdbSha256, 64) && Hash(expectedAssembly.PortablePdbId, 40),
                "pe-method-inventory-expected-identity");
            var dllPath = InputPath(absoluteDll);
            var pdbPath = InputPath(absolutePortablePdb);
            // These guards cover BinaryIdentity's independent reads, every
            // metadata read and creation of the returned immutable copies.
            using var dll = OpenInput(dllPath, 256 * 1024 * 1024);
            using var pdb = OpenInput(pdbPath, MaximumMetadataBytes);
            var actual = BinaryIdentity.ReadValidated(dllPath, pdbPath);
            Require(actual == expectedAssembly, "pe-method-inventory-assembly-mismatch");
            using var pe = new PEReader(dll, PEStreamOptions.LeaveOpen);
            Metadata(pe.HasMetadata && pe.PEHeaders.MetadataSize > 0);
            Limit(pe.PEHeaders.MetadataSize <= MaximumMetadataBytes);
            var decoder = new MetadataStringDecoder(new UTF8Encoding(false, true));
            var metadata = pe.GetMetadataReader(MetadataReaderOptions.None, decoder);
            Metadata(metadata.IsAssembly && metadata.GetString(metadata.GetAssemblyDefinition().Name) == actual.AssemblyName &&
                metadata.GetGuid(metadata.GetModuleDefinition().Mvid) == actual.Mvid);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen);
            var debug = provider.GetMetadataReader(MetadataReaderOptions.None, decoder);
            return new(1, actual with { }, new InventoryReader(metadata, debug).ReadAll());
        }
        catch (PeMethodInventoryException) { throw; }
        catch (BinaryIdentityException) { throw; }
        catch (Exception error) when (error is BadImageFormatException or OverflowException or IndexOutOfRangeException or
            DecoderFallbackException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            throw new PeMethodInventoryException("pe-method-inventory-invalid-metadata");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or CryptographicException or System.Security.SecurityException or PortablePdbSourceException)
        {
            // No arbitrary exception, path, inner exception or file contents escape.
            throw new PeMethodInventoryException("pe-method-inventory-file-read-failed");
        }
    }

    private sealed class InventoryReader(MetadataReader metadata, MetadataReader debug)
    {
        private readonly MetadataReader _metadata = metadata;
        private readonly MetadataReader _debug = debug;
        private readonly HashSet<int> _activeSpecifications = [];
        private readonly HashSet<string> _reasons = new(StringComparer.Ordinal);
        private readonly Dictionary<int, HashSet<string>> _accessorReasons = [];
        private int _textCharacters;
        private int _signatureNodes;

        public IReadOnlyList<PeMethodRecord> ReadAll()
        {
            Limit(_metadata.MethodDefinitions.Count <= MaximumMethods &&
                _metadata.TypeDefinitions.Count <= MaximumMethods && _metadata.TypeReferences.Count <= MaximumMethods &&
                _metadata.PropertyDefinitions.Count <= MaximumMethods && _metadata.EventDefinitions.Count <= MaximumMethods);
            var debugCount = _debug.GetTableRowCount(TableIndex.MethodDebugInformation);
            Metadata(debugCount == 0 || debugCount == _metadata.MethodDefinitions.Count);
            var accessors = ReadAccessors();
            var methods = new List<PeMethodRecord>(_metadata.MethodDefinitions.Count);
            var kickoffLinks = new Dictionary<int, int>();
            foreach (var handle in _metadata.MethodDefinitions)
            {
                _reasons.Clear();
                var token = MetadataTokens.GetToken(handle);
                if (_accessorReasons.TryGetValue(token, out var inheritedReasons)) _reasons.UnionWith(inheritedReasons);
                var definition = _metadata.GetMethodDefinition(handle);
                var owner = definition.GetDeclaringType();
                RequireTypeDefinition(owner);
                var ownerDefinition = _metadata.GetTypeDefinition(owner);
                var context = new GenericContext(ownerDefinition.GetGenericParameters().Count,
                    definition.GetGenericParameters().Count);
                Limit(context.TypeCount <= MaximumGenericParameters && context.MethodCount <= MaximumGenericParameters);
                var name = Text(definition.Name);
                var declaringType = NamedDefinition(owner, 0);
                var signatureReader = SignatureReader(definition.Signature);
                var rawSignature = Convert.ToHexStringLower(_metadata.GetBlobBytes(definition.Signature));
                ChargeText(rawSignature.Length);
                var signature = ReadSignature(ref signatureReader, context, 0, SignatureKind.Method, definition: true);
                Metadata(signatureReader.RemainingBytes == 0 && signature.GenericParameterCount == context.MethodCount &&
                    signature.IsInstance == ((definition.Attributes & MethodAttributes.Static) == 0));
                var parameters = ReadParameters(definition.GetParameters(), signature.ParameterTypes.Count);
                var typeParameters = ReadGenerics(ownerDefinition.GetGenericParameters(), owner,
                    new(context.TypeCount, 0));
                var methodParameters = ReadGenerics(definition.GetGenericParameters(), handle, context);
                int? kickoff = null;
                if (debugCount != 0)
                {
                    var information = _debug.GetMethodDebugInformation(
                        MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(handle)));
                    var target = information.GetStateMachineKickoffMethod();
                    if (!target.IsNil)
                    {
                        RequireMethod(target);
                        Metadata(target != handle);
                        kickoff = MetadataTokens.GetToken(target);
                        kickoffLinks.Add(token, kickoff.Value);
                    }
                }
                // RVA is an observation, not proof that native/runtime/abstract
                // methods have an IL body or a source location.
                if ((definition.ImplAttributes & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL)
                    _reasons.Add("non-il-implementation");
                methods.Add(new(token, MetadataTokens.GetToken(owner), TypeDisplay(declaringType), name,
                    definition.Attributes, definition.ImplAttributes, definition.RelativeVirtualAddress,
                    rawSignature, signature, parameters, methodParameters, typeParameters,
                    accessors.TryGetValue(token, out var relations) ? Freeze(relations) : Freeze<PeAccessorBinding>([]),
                    kickoff, Freeze(_reasons.OrderBy(item => item, StringComparer.Ordinal))));
            }
            Metadata(kickoffLinks.Count == _debug.GetTableRowCount(TableIndex.StateMachineMethod));
            ValidateKickoffLinks(kickoffLinks);
            return methods.AsReadOnly();
        }

        private Dictionary<int, List<PeAccessorBinding>> ReadAccessors()
        {
            var result = new Dictionary<int, List<PeAccessorBinding>>();
            var seen = new HashSet<(int Method, int Owner, PeAccessorKind Kind)>();
            var total = 0;
            Limit(_metadata.GetTableRowCount(TableIndex.MethodSemantics) <= MaximumSignatureNodes);
            foreach (var handle in _metadata.PropertyDefinitions)
            {
                _reasons.Clear();
                var property = _metadata.GetPropertyDefinition(handle);
                var owner = property.GetDeclaringType();
                RequireTypeDefinition(owner);
                var context = new GenericContext(_metadata.GetTypeDefinition(owner).GetGenericParameters().Count, 0);
                var reader = SignatureReader(property.Signature);
                var signature = ReadSignature(ref reader, context, 0, SignatureKind.Property, definition: false);
                Metadata(reader.RemainingBytes == 0 && signature.GenericParameterCount == 0);
                var name = Text(property.Name);
                var token = MetadataTokens.GetToken(handle);
                var accessors = property.GetAccessors();
                Add(accessors.Getter, owner, PeAccessorOwnerKind.Property, token, name, PeAccessorKind.Getter, signature.IsInstance);
                Add(accessors.Setter, owner, PeAccessorOwnerKind.Property, token, name, PeAccessorKind.Setter, signature.IsInstance);
                foreach (var other in accessors.Others)
                    Add(other, owner, PeAccessorOwnerKind.Property, token, name, PeAccessorKind.Other, signature.IsInstance);
            }
            foreach (var handle in _metadata.EventDefinitions)
            {
                _reasons.Clear();
                var @event = _metadata.GetEventDefinition(handle);
                var owner = @event.GetDeclaringType();
                RequireTypeDefinition(owner);
                var context = new GenericContext(_metadata.GetTypeDefinition(owner).GetGenericParameters().Count, 0);
                _ = ReadTypeHandle(@event.Type, 0, context, 0, allowSpecification: true);
                var name = Text(@event.Name);
                var token = MetadataTokens.GetToken(handle);
                var accessors = @event.GetAccessors();
                Add(accessors.Adder, owner, PeAccessorOwnerKind.Event, token, name, PeAccessorKind.Add, null);
                Add(accessors.Remover, owner, PeAccessorOwnerKind.Event, token, name, PeAccessorKind.Remove, null);
                Add(accessors.Raiser, owner, PeAccessorOwnerKind.Event, token, name, PeAccessorKind.Raise, null);
                foreach (var other in accessors.Others)
                    Add(other, owner, PeAccessorOwnerKind.Event, token, name, PeAccessorKind.Other, null);
            }
            // GetAccessors preserves Other rows but would collapse duplicate
            // Getter/Setter/etc. rows and ignore unknown semantics bits. Requiring
            // exact table conservation prevents those malformed rows disappearing.
            Metadata(total == _metadata.GetTableRowCount(TableIndex.MethodSemantics));
            return result;

            void Add(MethodDefinitionHandle method, TypeDefinitionHandle owner, PeAccessorOwnerKind ownerKind,
                int ownerToken, string ownerName, PeAccessorKind kind, bool? isInstance)
            {
                if (method.IsNil) return;
                RequireMethod(method);
                var definition = _metadata.GetMethodDefinition(method);
                Metadata(definition.GetDeclaringType() == owner &&
                    (isInstance is null || isInstance == ((definition.Attributes & MethodAttributes.Static) == 0)));
                var token = MetadataTokens.GetToken(method);
                Metadata(seen.Add((token, ownerToken, kind)));
                Limit(++total <= MaximumSignatureNodes);
                if (!result.TryGetValue(token, out var relations)) result.Add(token, relations = []);
                relations.Add(new(ownerKind, ownerToken, ownerName, kind));
                if (_reasons.Count != 0)
                {
                    if (!_accessorReasons.TryGetValue(token, out var reasons))
                        _accessorReasons.Add(token, reasons = new(StringComparer.Ordinal));
                    reasons.UnionWith(_reasons);
                }
            }
        }

        private IReadOnlyList<PeParameterMetadata> ReadParameters(ParameterHandleCollection handles, int parameterCount)
        {
            Limit(handles.Count <= MaximumParameters + 1);
            var result = new List<PeParameterMetadata>(handles.Count);
            var sequences = new HashSet<int>();
            foreach (var handle in handles)
            {
                var parameter = _metadata.GetParameter(handle);
                Metadata(parameter.SequenceNumber >= 0 && parameter.SequenceNumber <= parameterCount &&
                    sequences.Add(parameter.SequenceNumber));
                result.Add(new(MetadataTokens.GetToken(handle), parameter.SequenceNumber,
                    Text(parameter.Name, allowEmpty: true), parameter.Attributes));
            }
            return result.AsReadOnly();
        }

        private IReadOnlyList<PeGenericParameterMetadata> ReadGenerics(GenericParameterHandleCollection handles,
            EntityHandle owner, GenericContext context)
        {
            Limit(handles.Count <= MaximumGenericParameters);
            var result = new List<PeGenericParameterMetadata>(handles.Count);
            var positions = new HashSet<int>();
            foreach (var handle in handles)
            {
                var parameter = _metadata.GetGenericParameter(handle);
                Metadata(parameter.Parent == owner && parameter.Index >= 0 && parameter.Index < handles.Count &&
                    positions.Add(parameter.Index));
                var constraints = parameter.GetConstraints();
                Limit(constraints.Count <= MaximumGenericParameters);
                var types = new List<PeSignatureType>(constraints.Count);
                foreach (var constraintHandle in constraints)
                {
                    var constraint = _metadata.GetGenericParameterConstraint(constraintHandle);
                    Metadata(constraint.Parameter == handle);
                    types.Add(ReadTypeHandle(constraint.Type, 0, context, 0, allowSpecification: true));
                }
                result.Add(new(parameter.Index, Text(parameter.Name, allowEmpty: true), parameter.Attributes, types.AsReadOnly()));
            }
            return result.AsReadOnly();
        }

        private PeMethodSignature ReadSignature(ref BlobReader reader, GenericContext context, int depth,
            SignatureKind expectedKind, bool definition)
        {
            Visit(depth);
            var header = reader.ReadSignatureHeader();
            Metadata(header.Kind == expectedKind && (header.RawValue & 0x80) == 0 &&
                (!header.HasExplicitThis || header.IsInstance));
            var genericCount = header.IsGeneric ? Count(ref reader, MaximumGenericParameters) : 0;
            Metadata(!header.IsGeneric || genericCount > 0);
            Metadata(expectedKind != SignatureKind.Property || !header.IsGeneric);
            if (definition) Metadata(genericCount == context.MethodCount);
            var nestedContext = !definition && header.IsGeneric ? context with { MethodCount = genericCount } : context;
            var parameterCount = Count(ref reader, MaximumParameters);
            var returnType = ReadType(ref reader, nestedContext, depth + 1, allowVoid: true);
            var parameters = new List<PeSignatureType>(parameterCount);
            var required = parameterCount;
            var sawSentinel = false;
            for (var index = 0; index < parameterCount; index++)
            {
                var code = reader.ReadCompressedInteger();
                if (code == (int)SignatureTypeCode.Sentinel)
                {
                    Metadata(!definition && !sawSentinel && header.CallingConvention == SignatureCallingConvention.VarArgs);
                    required = index;
                    sawSentinel = true;
                    code = reader.ReadCompressedInteger();
                }
                parameters.Add(ReadTypeCode(ref reader, nestedContext, depth + 1, code, allowVoid: false));
            }
            if (expectedKind == SignatureKind.Method && !Enum.IsDefined(header.CallingConvention))
                _reasons.Add("unsupported-calling-convention");
            return new(header.RawValue, header.CallingConvention, header.IsInstance, header.HasExplicitThis,
                genericCount, required, returnType, parameters.AsReadOnly());
        }

        // BlobReader provides bounded primitive reads. Recursion is owned here
        // instead of letting an unbounded nested signature reach provider callbacks
        // only after its children have already been recursively decoded.
        private PeSignatureType ReadType(ref BlobReader reader, GenericContext context, int depth, bool allowVoid = false) =>
            ReadTypeCode(ref reader, context, depth, reader.ReadCompressedInteger(), allowVoid);

        private PeSignatureType ReadTypeCode(ref BlobReader reader, GenericContext context, int depth, int code, bool allowVoid)
        {
            Visit(depth);
            PeSignatureType result;
            switch (code)
            {
                case (int)SignatureTypeCode.Boolean:
                case (int)SignatureTypeCode.Char:
                case (int)SignatureTypeCode.SByte:
                case (int)SignatureTypeCode.Byte:
                case (int)SignatureTypeCode.Int16:
                case (int)SignatureTypeCode.UInt16:
                case (int)SignatureTypeCode.Int32:
                case (int)SignatureTypeCode.UInt32:
                case (int)SignatureTypeCode.Int64:
                case (int)SignatureTypeCode.UInt64:
                case (int)SignatureTypeCode.Single:
                case (int)SignatureTypeCode.Double:
                case (int)SignatureTypeCode.IntPtr:
                case (int)SignatureTypeCode.UIntPtr:
                case (int)SignatureTypeCode.Object:
                case (int)SignatureTypeCode.String:
                case (int)SignatureTypeCode.Void:
                case (int)SignatureTypeCode.TypedReference:
                    result = new(PeSignatureTypeKind.Primitive, Primitive: (PrimitiveTypeCode)code);
                    break;
                case (int)SignatureTypeKind.Class:
                case (int)SignatureTypeKind.ValueType:
                    result = ReadTypeHandle(reader.ReadTypeHandle(), (byte)code, context, depth + 1, allowSpecification: false);
                    break;
                case (int)SignatureTypeCode.GenericTypeParameter:
                case (int)SignatureTypeCode.GenericMethodParameter:
                    var index = Count(ref reader, MaximumGenericParameters);
                    var isMethod = code == (int)SignatureTypeCode.GenericMethodParameter;
                    Metadata(index < (isMethod ? context.MethodCount : context.TypeCount));
                    result = new(isMethod ? PeSignatureTypeKind.MethodParameter : PeSignatureTypeKind.TypeParameter,
                        GenericParameterIndex: index);
                    break;
                case (int)SignatureTypeCode.ByReference:
                    var target = ReadType(ref reader, context, depth + 1);
                    Metadata(Unmodified(target).Kind != PeSignatureTypeKind.ByReference);
                    result = new(PeSignatureTypeKind.ByReference, ElementType: target);
                    break;
                case (int)SignatureTypeCode.Pointer:
                    result = new(PeSignatureTypeKind.Pointer, ElementType: ReadType(ref reader, context, depth + 1, allowVoid: true));
                    break;
                case (int)SignatureTypeCode.SZArray:
                    var element = ReadType(ref reader, context, depth + 1);
                    Metadata(Unmodified(element).Kind != PeSignatureTypeKind.ByReference);
                    result = new(PeSignatureTypeKind.SzArray, ElementType: element);
                    break;
                case (int)SignatureTypeCode.Array:
                    var arrayElement = ReadType(ref reader, context, depth + 1);
                    Metadata(Unmodified(arrayElement).Kind != PeSignatureTypeKind.ByReference);
                    var rank = Count(ref reader, MaximumParameters);
                    Metadata(rank > 0);
                    var sizes = new List<int>();
                    var sizeCount = Count(ref reader, rank);
                    ChargeSignatureWork(sizeCount);
                    for (var dimension = 0; dimension < sizeCount; dimension++) sizes.Add(reader.ReadCompressedInteger());
                    var bounds = new List<int>();
                    var boundCount = Count(ref reader, rank);
                    ChargeSignatureWork(boundCount);
                    for (var dimension = 0; dimension < boundCount; dimension++) bounds.Add(reader.ReadCompressedSignedInteger());
                    result = new(PeSignatureTypeKind.Array, ElementType: arrayElement,
                        ArrayShape: new(rank, sizes.AsReadOnly(), bounds.AsReadOnly()));
                    break;
                case (int)SignatureTypeCode.GenericTypeInstance:
                    var rawKind = reader.ReadCompressedInteger();
                    Metadata(rawKind == (int)SignatureTypeKind.Class || rawKind == (int)SignatureTypeKind.ValueType);
                    var genericType = ReadTypeHandle(reader.ReadTypeHandle(), (byte)rawKind, context, depth + 1, allowSpecification: false);
                    var argumentCount = Count(ref reader, MaximumGenericParameters);
                    Metadata(argumentCount > 0);
                    var arguments = new List<PeSignatureType>(argumentCount);
                    for (var argument = 0; argument < argumentCount; argument++)
                        arguments.Add(ReadType(ref reader, context, depth + 1));
                    result = new(PeSignatureTypeKind.GenericInstantiation, ElementType: genericType, TypeArguments: arguments.AsReadOnly());
                    break;
                case (int)SignatureTypeCode.RequiredModifier:
                case (int)SignatureTypeCode.OptionalModifier:
                    var modifier = ReadTypeHandle(reader.ReadTypeHandle(), 0, context, depth + 1, allowSpecification: true);
                    result = new(PeSignatureTypeKind.Modified, ElementType: ReadType(ref reader, context, depth + 1, allowVoid),
                        IsRequiredModifier: code == (int)SignatureTypeCode.RequiredModifier, ModifierType: modifier);
                    break;
                case (int)SignatureTypeCode.FunctionPointer:
                    result = new(PeSignatureTypeKind.FunctionPointer, FunctionPointerSignature:
                        ReadSignature(ref reader, context, depth + 1, SignatureKind.Method, definition: false));
                    break;
                default:
                    // Includes pinned locals/sentinels in method-type positions
                    // and reserved codes; these are not well-formed method types.
                    throw new PeMethodInventoryException("pe-method-inventory-invalid-metadata");
            }
            Metadata(allowVoid || Unmodified(result).Primitive != PrimitiveTypeCode.Void);
            return result;
        }

        private PeSignatureType ReadTypeHandle(EntityHandle handle, byte rawKind, GenericContext context,
            int depth, bool allowSpecification)
        {
            Visit(depth);
            Metadata(!handle.IsNil);
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                    return new(PeSignatureTypeKind.Named, NamedType: NamedDefinition((TypeDefinitionHandle)handle, rawKind));
                case HandleKind.TypeReference:
                    return NamedReference((TypeReferenceHandle)handle, rawKind);
                case HandleKind.TypeSpecification:
                    Metadata(allowSpecification && MetadataTokens.GetRowNumber(handle) <= _metadata.GetTableRowCount(TableIndex.TypeSpec));
                    var token = MetadataTokens.GetToken(handle);
                    Metadata(_activeSpecifications.Add(token));
                    try
                    {
                        var specification = _metadata.GetTypeSpecification((TypeSpecificationHandle)handle);
                        var reader = SignatureReader(specification.Signature);
                        var result = ReadType(ref reader, context, depth + 1);
                        Metadata(reader.RemainingBytes == 0);
                        return result;
                    }
                    finally { _activeSpecifications.Remove(token); }
                default:
                    throw new PeMethodInventoryException("pe-method-inventory-invalid-metadata");
            }
        }

        private PeNamedTypeIdentity NamedDefinition(TypeDefinitionHandle handle, byte rawKind)
        {
            var original = handle;
            var names = new List<string>();
            var seen = new HashSet<int>();
            var ns = "";
            while (!handle.IsNil)
            {
                Limit(names.Count < MaximumDepth);
                RequireTypeDefinition(handle);
                Metadata(seen.Add(MetadataTokens.GetToken(handle)));
                var definition = _metadata.GetTypeDefinition(handle);
                names.Add(Text(definition.Name));
                var parent = definition.GetDeclaringType();
                var recordedNamespace = Text(definition.Namespace, allowEmpty: true);
                if (parent.IsNil) ns = recordedNamespace;
                else if (recordedNamespace.Length != 0) _reasons.Add("nested-type-namespace");
                handle = parent;
            }
            names.Reverse();
            return new(MetadataTokens.GetToken(original), rawKind, ns, names.AsReadOnly(),
                new(PeTypeResolutionScopeKind.CurrentModule, 1, ModuleName: Text(_metadata.GetModuleDefinition().Name)));
        }

        private PeSignatureType NamedReference(TypeReferenceHandle handle, byte rawKind)
        {
            var original = handle;
            var names = new List<string>();
            var seen = new HashSet<int>();
            var ns = "";
            EntityHandle scope;
            while (true)
            {
                Limit(names.Count < MaximumDepth);
                Metadata(!handle.IsNil && MetadataTokens.GetRowNumber(handle) <= _metadata.TypeReferences.Count &&
                    seen.Add(MetadataTokens.GetToken(handle)));
                var reference = _metadata.GetTypeReference(handle);
                names.Add(Text(reference.Name));
                var recordedNamespace = Text(reference.Namespace, allowEmpty: true);
                scope = reference.ResolutionScope;
                if (scope.Kind != HandleKind.TypeReference) { ns = recordedNamespace; break; }
                if (recordedNamespace.Length != 0) _reasons.Add("nested-type-namespace");
                handle = (TypeReferenceHandle)scope;
            }
            names.Reverse();
            PeTypeResolutionScope resolution;
            if (scope.IsNil)
            {
                // A nil scope may require exported-type/multi-module resolution.
                // No parent/module search is authorized by these two input files.
                _reasons.Add("type-reference-without-resolution-scope");
                return new(PeSignatureTypeKind.Unresolved);
            }
            switch (scope.Kind)
            {
                case HandleKind.ModuleDefinition:
                    Metadata(MetadataTokens.GetRowNumber(scope) == 1);
                    resolution = new(PeTypeResolutionScopeKind.CurrentModule, MetadataTokens.GetToken(scope),
                        ModuleName: Text(_metadata.GetModuleDefinition().Name));
                    break;
                case HandleKind.ModuleReference:
                    Metadata(MetadataTokens.GetRowNumber(scope) <= _metadata.GetTableRowCount(TableIndex.ModuleRef));
                    resolution = new(PeTypeResolutionScopeKind.ModuleReference, MetadataTokens.GetToken(scope),
                        ModuleName: Text(_metadata.GetModuleReference((ModuleReferenceHandle)scope).Name));
                    break;
                case HandleKind.AssemblyReference:
                    Metadata(MetadataTokens.GetRowNumber(scope) <= _metadata.AssemblyReferences.Count);
                    var assembly = _metadata.GetAssemblyReference((AssemblyReferenceHandle)scope);
                    var key = _metadata.GetBlobBytes(assembly.PublicKeyOrToken);
                    Limit(key.Length <= MaximumSignatureBytes);
                    Metadata((assembly.Flags & AssemblyFlags.PublicKey) != 0 ? key.Length > 0 : key.Length is 0 or 8);
                    ChargeText(checked(key.Length * 2));
                    resolution = new(PeTypeResolutionScopeKind.AssemblyReference, MetadataTokens.GetToken(scope),
                        new(Text(assembly.Name), assembly.Version, Text(assembly.Culture, allowEmpty: true),
                            Convert.ToHexStringLower(key), assembly.Flags));
                    break;
                default:
                    throw new PeMethodInventoryException("pe-method-inventory-invalid-metadata");
            }
            return new(PeSignatureTypeKind.Named, NamedType:
                new(MetadataTokens.GetToken(original), rawKind, ns, names.AsReadOnly(), resolution));
        }

        private BlobReader SignatureReader(BlobHandle handle)
        {
            Metadata(!handle.IsNil);
            var reader = _metadata.GetBlobReader(handle);
            Metadata(reader.Length > 0);
            Limit(reader.Length <= MaximumSignatureBytes);
            return reader;
        }

        private string Text(StringHandle handle, bool allowEmpty = false)
        {
            var value = _metadata.GetString(handle);
            Metadata((allowEmpty || value.Length > 0) && !value.Any(char.IsControl));
            Limit(value.Length <= 4096);
            ChargeText(value.Length);
            return value;
        }

        private void ChargeText(int count)
        {
            _textCharacters = checked(_textCharacters + count);
            Limit(_textCharacters <= MaximumTextCharacters);
        }

        private void Visit(int depth)
        {
            Limit(depth <= MaximumDepth);
            ChargeSignatureWork(1);
        }

        private void ChargeSignatureWork(int count)
        {
            // Repeated property/accessor signatures may share one blob. Its
            // integer shape entries still consume the same aggregate work limit
            // on every decode, before allocating or looping over those entries.
            Limit(count >= 0 && count <= MaximumSignatureNodes - _signatureNodes);
            _signatureNodes += count;
        }

        private void RequireTypeDefinition(TypeDefinitionHandle handle) =>
            Metadata(!handle.IsNil && MetadataTokens.GetRowNumber(handle) <= _metadata.TypeDefinitions.Count);

        private void RequireMethod(MethodDefinitionHandle handle) =>
            Metadata(!handle.IsNil && MetadataTokens.GetRowNumber(handle) <= _metadata.MethodDefinitions.Count);

        private static string TypeDisplay(PeNamedTypeIdentity type) =>
            (type.Namespace.Length == 0 ? "" : type.Namespace + ".") + string.Join("+", type.TypeNames);

        private static PeSignatureType Unmodified(PeSignatureType type)
        {
            while (type.Kind == PeSignatureTypeKind.Modified) type = type.ElementType!;
            return type;
        }

        private static int Count(ref BlobReader reader, int maximum)
        {
            var count = reader.ReadCompressedInteger();
            Metadata(count >= 0);
            Limit(count <= maximum);
            return count;
        }

        private static void ValidateKickoffLinks(Dictionary<int, int> links)
        {
            var complete = new HashSet<int>();
            foreach (var start in links.Keys)
            {
                var path = new HashSet<int>();
                var current = start;
                while (!complete.Contains(current) && links.TryGetValue(current, out var next))
                {
                    Metadata(path.Add(current));
                    current = next;
                }
                complete.UnionWith(path);
            }
        }

        private readonly record struct GenericContext(int TypeCount, int MethodCount);
    }

    private static string InputPath(string path)
    {
        Require(path is { Length: > 0 and <= 32768 } && Path.IsPathFullyQualified(path),
            "pe-method-inventory-path-invalid");
        var normalized = path.Replace('\\', '/');
        Require(!normalized.StartsWith("//", StringComparison.Ordinal) && !normalized.StartsWith("/??/", StringComparison.Ordinal),
            "pe-method-inventory-path-invalid");
        var root = Path.GetPathRoot(path);
        Require(!string.IsNullOrEmpty(root) && PortablePdbSource.RelativePath(path[root.Length..].Replace('\\', '/')),
            "pe-method-inventory-path-invalid");
        // Validate raw components before GetFullPath can erase dot/parent/alias syntax.
        return Path.GetFullPath(path);
    }

    private static FileStream OpenInput(string path, long maximum)
    {
        PortablePdbSource.RequireNoReparseAncestors(path, directory: false);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            PortablePdbSource.RequireNoReparseAncestors(path, directory: false);
            Limit(stream.Length is > 0 && stream.Length <= maximum);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());
    private static bool Hash(string value, int length) => value is not null && value.Length == length &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Metadata([DoesNotReturnIf(false)] bool condition) =>
        Require(condition, "pe-method-inventory-invalid-metadata");
    private static void Limit([DoesNotReturnIf(false)] bool condition) =>
        Require(condition, "pe-method-inventory-limit");
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw new PeMethodInventoryException(code);
    }
}
