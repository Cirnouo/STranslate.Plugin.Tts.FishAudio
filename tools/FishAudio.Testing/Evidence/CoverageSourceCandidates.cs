using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;

[assembly: InternalsVisibleTo("FishAudio.Testing.Tests")]

namespace FishAudio.Testing.Evidence;

/// <summary>
/// Caller-supplied source expectations. SourceDirectory and Product binary paths
/// come exclusively from the independently supplied session admission.
/// </summary>
public sealed record CoverageSourceCandidatesInput(int SchemaVersion,
    BuildSourceInventoryIdentity ProductBuild,
    IReadOnlyList<PortablePdbDocumentMap> DocumentMaps,
    IReadOnlyList<PortablePdbSourceFile> TrackedFiles,
    IReadOnlyList<string> GeneratedSourceRoots);

public enum CoverageCandidateScopeKind { Assembly, Type }
public enum CoverageCandidateFallback { None, Type, Assembly }
public enum CoverageObservationRole { ContainerSummary, SupportedMemberHint, UnknownMember }

/// <summary>Id is only an interned reference inside this result; Product scopes every metadata token.</summary>
public sealed record CoverageCandidateScope(int Id, CoverageCandidateScopeKind Kind,
    int? DeclaringTypeToken, string? DeclaringType);

public sealed record CoverageCandidateDocument(BuildSourceDocumentEvidence Provenance,
    PortablePdbDocumentEvidence CompilerDocument);

/// <summary>
/// Sequence points describe compiler document provenance, never actual executed
/// source lines. Metadata retains actual accessor and PDB kickoff relationships.
/// </summary>
public sealed record CoverageCandidateMethod(int MetadataToken, int ScopeId, PeMethodRecord Metadata,
    IReadOnlyList<PortablePdbSequencePoint> NonHiddenSequencePoints,
    IReadOnlyList<string> UnresolvedReasons);

/// <summary>
/// The aggregate root spans the entire raw report, not a fabricated Product
/// method or assembly relation. Its counts never become candidate edges.
/// </summary>
public sealed record CoverageContainerRootSummary(long CoveredStatements, long TotalStatements, int CoveragePercent)
{
    public string Kind => "Root";
    public string RawPath => "";
    public CoverageObservationRole Role => CoverageObservationRole.ContainerSummary;
    public string Reason => "container-summary";
    public bool HasObservedMemberCoverage => false;
}

public sealed record CoverageObservationCandidates(DotCoverMemberRecord Observation,
    CoverageObservationRole Role, int ScopeId, IReadOnlyList<int> CandidateMethodTokens,
    CoverageCandidateFallback Fallback, IReadOnlyList<string> UnresolvedReasons)
{
    public bool HasObservedMemberCoverage =>
        Role != CoverageObservationRole.ContainerSummary && Observation.CoveredStatements > 0;
}

public sealed record CoverageSourceCandidatesEvidence(int SchemaVersion, CoverageSessionIdentity Identity,
    BinaryIdentityRecord Product, string AggregateSha256, CoverageContainerRootSummary RootSummary,
    IReadOnlyList<CoverageCandidateScope> Scopes,
    IReadOnlyList<CoverageCandidateDocument> Documents,
    IReadOnlyList<CoverageCandidateMethod> Methods,
    IReadOnlyList<CoverageObservationCandidates> Observations, EmptyProductCoverage? EmptyProduct)
{
    public string SourceIdentityAuthority => "caller-supplied";
    public bool BuildInputClosureProven => false;
    public bool GeneratorInputClosureProven => false;
    public bool SourceMappingsResolved => false;
    public bool CompleteBaseline => false;
    public bool BaselineClaim => false;
}

public sealed class CoverageSourceCandidatesException : Exception
{
    internal CoverageSourceCandidatesException(string code) : base(code) => Code = code;
    public string Code { get; }
}

// Parser-work seam only. Production caps are fixed; the implementation must
// reject non-positive values or values above either cap, never raise them.
internal readonly record struct CoverageSourceCandidateBudget(int MaximumCandidateReferences,
    int MaximumSequencePointAssociations);

/// <summary>
/// Reads raw isolated-session evidence itself, then binds the same Product pair
/// to PE/PDB/source evidence. No deserialized CaseCoverage admission overload,
/// assembly execution, path search, source semantic join or baseline is implied.
/// </summary>
public static class CoverageSourceCandidates
{
    internal const int MaximumCandidateReferences = 250_000;
    internal const int MaximumSequencePointAssociations = 2_000_000;
    internal const int MaximumScopes = 65_537;
    internal const int MaximumMetadataTextCharacters = 16 * 1024 * 1024;

    public static CoverageSourceCandidatesEvidence ReadValidated(IsolatedCoverageSessionInput sessionInput,
        IsolatedCoverageAdmission expectedSession, CoverageSourceCandidatesInput sourceInput) =>
        ReadValidated(sessionInput, expectedSession, sourceInput,
            new(MaximumCandidateReferences, MaximumSequencePointAssociations));

    internal static CoverageSourceCandidatesEvidence ReadValidated(IsolatedCoverageSessionInput sessionInput,
        IsolatedCoverageAdmission expectedSession, CoverageSourceCandidatesInput sourceInput,
        CoverageSourceCandidateBudget budget)
    {
        var guards = new List<FileStream>();
        var completed = false;
        try
        {
            Require(budget.MaximumCandidateReferences is > 0 and <= MaximumCandidateReferences &&
                budget.MaximumSequencePointAssociations is > 0 and <= MaximumSequencePointAssociations, "invalid-budget");
            Require(sessionInput is not null && expectedSession is not null && sourceInput is not null &&
                sessionInput.SchemaVersion == 1 && expectedSession.SchemaVersion == 1 && sourceInput.SchemaVersion == 1 &&
                sourceInput.ProductBuild is not null && sourceInput.ProductBuild.SchemaVersion == 1 &&
                sourceInput.ProductBuild.ExpectedAssembly is not null &&
                sourceInput.DocumentMaps is not null && sourceInput.TrackedFiles is not null &&
                sourceInput.GeneratedSourceRoots is not null && expectedSession.Binaries is not null &&
                sessionInput.Artifacts is not null && sourceInput.DocumentMaps.Count <= 128 &&
                sourceInput.TrackedFiles.Count <= 200_000 && sourceInput.GeneratedSourceRoots.Count <= 128 &&
                expectedSession.Binaries.Count == 2 && sessionInput.Artifacts.Count == Enum.GetValues<CoverageArtifactKind>().Length,
                "invalid-input");
            var source = sourceInput with
            {
                ProductBuild = sourceInput.ProductBuild with { ExpectedAssembly = sourceInput.ProductBuild.ExpectedAssembly with { } },
                DocumentMaps = Array.AsReadOnly(sourceInput.DocumentMaps.ToArray()),
                TrackedFiles = Array.AsReadOnly(sourceInput.TrackedFiles.ToArray()),
                GeneratedSourceRoots = Array.AsReadOnly(sourceInput.GeneratedSourceRoots.ToArray()),
            };
            var expected = expectedSession with { Binaries = Array.AsReadOnly(expectedSession.Binaries.ToArray()) };
            var input = sessionInput with { Artifacts = Array.AsReadOnly(sessionInput.Artifacts.ToArray()) };
            var bindings = expected.Binaries.Where(item => item is not null && item.Role == CoverageBinaryRole.Product).Take(2).ToArray();
            var aggregates = input.Artifacts.Where(item => item is not null && item.Kind == CoverageArtifactKind.AggregateJson).Take(2).ToArray();
            Require(bindings.Length == 1 && aggregates.Length == 1, "invalid-input");
            var binding = bindings[0];
            var buildRoot = Root(expected.BuildDirectory);
            var sourceRoot = Root(expected.SourceDirectory);
            var artifactRoot = Root(input.ArtifactDirectory);
            var dllPath = Child(buildRoot, binding.DllRelativePath);
            var pdbPath = Child(buildRoot, binding.PdbRelativePath);
            var aggregatePath = Child(artifactRoot, aggregates[0].RelativePath);
            // These same three file guards span admission, both metadata/source
            // readers and candidate construction. A later path or cached object
            // cannot substitute for the current aggregate or Product pair.
            guards.Add(PortablePdbSource.OpenRead(dllPath, 256 * 1024 * 1024));
            guards.Add(PortablePdbSource.OpenRead(pdbPath, 64 * 1024 * 1024));
            var aggregateGuard = PortablePdbSource.OpenRead(aggregatePath, 64 * 1024 * 1024);
            guards.Add(aggregateGuard);
            CaseCoverage admitted;
            try { admitted = IsolatedCoverageSession.ReadValidated(input, expected); }
            catch (IsolatedCoverageSessionException) { throw Rejected("session-evidence-rejected"); }
            Require(source.ProductBuild.BuildId == admitted.Identity.BuildId &&
                source.ProductBuild.SourceTree == admitted.Identity.Source.Tree &&
                source.ProductBuild.SourceFingerprint == admitted.Identity.Source.Fingerprint, "session-build-mismatch");
            var product = admitted.Binaries.Single(item => item.Role == CoverageBinaryRole.Product).Identity;
            Require(source.ProductBuild.ExpectedAssembly == product && binding.ExpectedIdentity == product, "product-identity-mismatch");
            aggregateGuard.Position = 0;
            var aggregateBytes = new byte[checked((int)aggregateGuard.Length)];
            aggregateGuard.ReadExactly(aggregateBytes);
            var digest = Convert.ToHexStringLower(SHA256.HashData(aggregateBytes));
            Require(digest == aggregates[0].Sha256 && digest == admitted.Aggregate.ReportSha256 &&
                admitted.Aggregate.TargetAssemblyName == product.AssemblyName, "aggregate-digest-mismatch");

            PeMethodInventoryEvidence pe;
            try { pe = PeMethodInventory.ReadValidated(dllPath, pdbPath, product); }
            catch (Exception error) when (error is PeMethodInventoryException or BinaryIdentityException)
            { throw Rejected("pe-evidence-rejected"); }
            BuildSourceInventoryEvidence sources;
            try
            {
                sources = BuildSourceInventory.ReadValidated(source.ProductBuild, dllPath, pdbPath, sourceRoot,
                    source.DocumentMaps, source.TrackedFiles, source.GeneratedSourceRoots);
            }
            catch (Exception error) when (error is BuildSourceInventoryException or PortablePdbSourceException or BinaryIdentityException)
            { throw Rejected("source-evidence-rejected"); }
            Require(pe.Assembly == product && sources.SourceEvidence.Assembly == product &&
                sources.ExpectedBuild == source.ProductBuild, "pe-source-identity-mismatch");
            var offset = aggregateBytes.Length >= 3 && aggregateBytes[0] == 0xef &&
                aggregateBytes[1] == 0xbb && aggregateBytes[2] == 0xbf ? 3 : 0;
            using var document = JsonDocument.Parse(aggregateBytes.AsMemory(offset), new JsonDocumentOptions { MaxDepth = 128 });
            var root = document.RootElement;
            var summary = new CoverageContainerRootSummary(root.GetProperty("CoveredStatements").GetInt64(),
                root.GetProperty("TotalStatements").GetInt64(), root.GetProperty("CoveragePercent").GetInt32());
            var builder = new CandidateBuilder(pe, sources, budget);
            var result = builder.Build(admitted, digest, summary);
            completed = true;
            return result;
        }
        catch (CoverageSourceCandidatesException) { throw; }
        catch (PortablePdbSourceException) { throw Rejected("invalid-input-or-file"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or JsonException or OverflowException or KeyNotFoundException or
            FormatException or CryptographicException or System.Security.SecurityException)
        { throw Rejected("invalid-input-or-file"); }
        finally
        {
            var failed = false;
            foreach (var guard in guards)
            {
                try { guard.Dispose(); }
                catch { failed = true; }
            }
            if (failed && completed) throw Rejected("file-release-failed");
        }
    }

    private sealed class CandidateBuilder
    {
        private readonly PeMethodInventoryEvidence _pe;
        private readonly Work _work;
        private readonly Dictionary<int, PeMethodRecord> _metadata = [];
        private readonly Dictionary<int, PortablePdbMethodEvidence> _debug = [];
        private readonly Dictionary<int, TypeMethods> _types = [];
        private readonly Dictionary<string, List<TypeMethods>> _typeNames = new(StringComparer.Ordinal);
        private readonly Dictionary<int, List<int>> _relations = [];
        private readonly Dictionary<int, CoverageCandidateScope> _typeScopes = [];
        private readonly Dictionary<int, CoverageCandidateMethod> _methods = [];
        private readonly List<CoverageCandidateScope> _scopes = [new(0, CoverageCandidateScopeKind.Assembly, null, null)];
        private readonly List<CoverageCandidateDocument> _documents = [];
        private readonly HashSet<int> _documentRows = [];

        internal CandidateBuilder(PeMethodInventoryEvidence pe, BuildSourceInventoryEvidence sources,
            CoverageSourceCandidateBudget budget)
        {
            _pe = pe;
            _work = new Work(budget);
            foreach (var source in sources.SourceEvidence.Methods)
                Require(_debug.TryAdd(source.MetadataToken, source), "pe-source-method-mismatch");
            Require(pe.Methods.Count == _debug.Count, "pe-source-method-mismatch");
            foreach (var method in pe.Methods)
            {
                Require(_metadata.TryAdd(method.MetadataToken, method) &&
                    _debug.TryGetValue(method.MetadataToken, out var source) &&
                    source.Name == method.MetadataName && source.DeclaringType == method.DeclaringType &&
                    source.StateMachineKickoffMethodToken == method.StateMachineKickoffMethodToken, "pe-source-method-mismatch");
                _work.Text(method.DeclaringType);
                _work.Text(method.MetadataName);
                foreach (var accessor in method.Accessors) _work.Text(accessor.OwnerName);
                if (!_types.TryGetValue(method.DeclaringTypeToken, out var type))
                {
                    Require(_types.Count < MaximumScopes - 1, "scope-limit");
                    type = new(method.DeclaringTypeToken, method.DeclaringType);
                    _types.Add(type.Token, type);
                    if (!_typeNames.TryGetValue(type.Name, out var matches)) _typeNames.Add(type.Name, matches = []);
                    matches.Add(type);
                }
                Require(type.Name == method.DeclaringType, "pe-source-method-mismatch");
                type.Add(method);
            }
            foreach (var method in pe.Methods)
                if (method.StateMachineKickoffMethodToken is { } kickoff)
                {
                    Require(_metadata.ContainsKey(kickoff), "pe-source-method-mismatch");
                    Link(method.MetadataToken, kickoff);
                    Link(kickoff, method.MetadataToken);
                }
            var compilerDocuments = sources.SourceEvidence.Documents.ToDictionary(item => item.RowId);
            foreach (var provenance in sources.Documents)
            {
                Require(compilerDocuments.Remove(provenance.DocumentRowId, out var compiler) &&
                    compiler.DocumentName == provenance.DocumentName &&
                    compiler.SnapshotRelativePath == provenance.SnapshotRelativePath &&
                    compiler.SourceSha256 == provenance.Sha256, "pe-source-document-mismatch");
                _work.Text(provenance.DocumentName);
                _work.Text(provenance.SnapshotRelativePath);
                _work.Text(provenance.Sha256);
                _work.Text(provenance.GeneratedRoot);
                _work.Text(compiler.Checksum);
                Require(_documentRows.Add(compiler.RowId), "pe-source-document-mismatch");
                _documents.Add(new(provenance, compiler));
            }
            Require(compilerDocuments.Count == 0, "pe-source-document-mismatch");
        }

        internal CoverageSourceCandidatesEvidence Build(CaseCoverage session, string digest, CoverageContainerRootSummary root)
        {
            var observations = new List<CoverageObservationCandidates>();
            var raw = session.Aggregate.Members;
            for (var index = 0; index < raw.Count; index++)
            {
                var observation = raw[index];
                _work.Text(observation.AssemblyName);
                _work.Text(observation.Name);
                _work.Text(observation.Kind);
                _work.Text(observation.RawPath);
                foreach (var segment in observation.SymbolSegments) { _work.Text(segment.Kind); _work.Text(segment.Name); }
                foreach (var reason in observation.UnresolvedReasons) _work.Text(reason);
                Require(observation.AssemblyName == _pe.Assembly.AssemblyName, "aggregate-assembly-mismatch");
                var hasChildren = index + 1 < raw.Count && raw[index + 1].RawPath.StartsWith(
                    observation.RawPath + "/Children/", StringComparison.Ordinal);
                if (Container(observation.Kind) ||
                    hasChildren && (observation.Kind is "Property" or "AutoProperty" or "Indexer" or "Event"))
                {
                    _work.Text("container-summary");
                    observations.Add(new(observation, CoverageObservationRole.ContainerSummary, 0, [],
                        CoverageCandidateFallback.None, Array.AsReadOnly(new[] { "container-summary" })));
                    continue;
                }
                var type = ResolveType(observation);
                var scope = type is null ? _scopes[0] : Scope(type);
                var reasons = new List<string>(observation.UnresolvedReasons);
                int[] candidates = type is null ? [] : Select(observation, type, reasons);
                var role = candidates.Length == 0 ? CoverageObservationRole.UnknownMember : CoverageObservationRole.SupportedMemberHint;
                if (type is null) AddReason(reasons, "type-scope-unresolved");
                if (candidates.Length == 0) AddReason(reasons, "member-candidates-unresolved");
                else
                {
                    AddReason(reasons, "signature-not-resolved");
                    if (candidates.Length > 1) AddReason(reasons, "multiple-metadata-candidates");
                    Associate(candidates);
                    foreach (var candidate in candidates)
                        foreach (var reason in _methods[candidate].UnresolvedReasons) AddReason(reasons, reason);
                }
                var needsFallback = observation.CoveredStatements > 0 &&
                    (role == CoverageObservationRole.UnknownMember || reasons.Contains("no-nonhidden-sequence-points", StringComparer.Ordinal));
                var fallback = !needsFallback ? CoverageCandidateFallback.None :
                    type is null ? CoverageCandidateFallback.Assembly : CoverageCandidateFallback.Type;
                foreach (var reason in reasons) _work.Text(reason);
                observations.Add(new(observation, role, scope.Id, Array.AsReadOnly(candidates), fallback, reasons.AsReadOnly()));
            }
            EmptyProductCoverage? empty = null;
            if (session.EmptyProduct is { } explanation)
            {
                foreach (var dependency in explanation.Dependencies)
                { _work.Text(dependency.RelativePath); _work.Text(dependency.Sha256); }
                empty = new(explanation.Reason, Array.AsReadOnly(explanation.Dependencies.ToArray()));
            }
            return new(1, session.Identity, _pe.Assembly, digest, root, _scopes.AsReadOnly(),
                _documents.AsReadOnly(), Array.AsReadOnly(_methods.Values.OrderBy(item => item.MetadataToken).ToArray()),
                observations.AsReadOnly(), empty);
        }

        private TypeMethods? ResolveType(DotCoverMemberRecord observation)
        {
            var segments = observation.SymbolSegments;
            if (segments.Count == 0 || segments[0].Kind != "Assembly" || segments[0].Name != _pe.Assembly.AssemblyName) return null;
            var namespaceFragments = new List<string>();
            var names = new List<string>();
            for (var index = 1; index < segments.Count; index++)
            {
                var segment = segments[index];
                if (segment.Kind == "Namespace")
                {
                    if (names.Count != 0 || segment.Name is null ||
                        !segment.Name.Split('.').All(Identifier)) return null;
                    namespaceFragments.Add(segment.Name);
                }
                else if (segment.Kind == "Type")
                {
                    if (!Identifier(segment.Name)) return null;
                    names.Add(segment.Name!);
                }
                else break;
            }
            if (names.Count == 0) return null;
            var name = (namespaceFragments.Count == 0 ? "" : string.Join(".", namespaceFragments) + ".") + string.Join("+", names);
            return _typeNames.TryGetValue(name, out var matches) && matches.Count == 1 ? matches[0] : null;
        }

        private static int[] Select(DotCoverMemberRecord observation, TypeMethods type, List<string> reasons)
        {
            if (observation.SymbolSegments.Take(observation.SymbolSegments.Count - 1).Any(segment => !Container(segment.Kind)))
            {
                // Property/event accessor nodes have one explicit owner segment;
                // a method or compiler parent must not become a guessed CLR name.
                if (observation.Kind is not ("PropertyGetter" or "PropertySetter" or "PropertyInitOnlySetter" or "EventAdder" or "EventRemover"))
                { AddReason(reasons, "nested-member-observation"); return []; }
            }
            if (observation.UnresolvedReasons.Contains("compiler-generated-member", StringComparer.Ordinal) ||
                observation.Kind is "AnonymousMethod" or "LocalFunction" or "InternalCompiledMethod" or "OwnCoverage" or "UnresolvedStateMachineMethod")
            { AddReason(reasons, "compiler-member-unresolved"); return []; }
            if (observation.Kind == "Method")
            {
                if (!MethodName(observation.Name, constructor: false, out var name))
                { AddReason(reasons, "unsupported-member-name"); return []; }
                return type.ByName.TryGetValue(name, out var methods) ? methods.ToArray() : [];
            }
            if (observation.Kind == "Constructor")
            {
                if (!MethodName(observation.Name, constructor: true, out var name) ||
                    name != type.Name[(type.Name.LastIndexOfAny(['.', '+']) + 1)..] && name is not (".ctor" or ".cctor"))
                { AddReason(reasons, "unsupported-member-name"); return []; }
                return type.Constructors.ToArray();
            }
            PeAccessorOwnerKind ownerKind;
            PeAccessorKind? accessorKind = null;
            string? ownerName = observation.Name;
            switch (observation.Kind)
            {
                case "Property": case "AutoProperty": case "Indexer": ownerKind = PeAccessorOwnerKind.Property; break;
                case "Event": ownerKind = PeAccessorOwnerKind.Event; break;
                case "PropertyGetter": ownerKind = PeAccessorOwnerKind.Property; accessorKind = PeAccessorKind.Getter; break;
                case "PropertySetter": case "PropertyInitOnlySetter": ownerKind = PeAccessorOwnerKind.Property; accessorKind = PeAccessorKind.Setter; break;
                case "EventAdder": ownerKind = PeAccessorOwnerKind.Event; accessorKind = PeAccessorKind.Add; break;
                case "EventRemover": ownerKind = PeAccessorOwnerKind.Event; accessorKind = PeAccessorKind.Remove; break;
                default: AddReason(reasons, "unsupported-member-kind"); return [];
            }
            if (accessorKind is not null)
            {
                var owner = observation.SymbolSegments.Take(observation.SymbolSegments.Count - 1).LastOrDefault(segment =>
                    ownerKind == PeAccessorOwnerKind.Property ? segment.Kind is "Property" or "AutoProperty" or "Indexer" : segment.Kind == "Event");
                ownerName = owner?.Name;
            }
            var colon = ownerName?.IndexOf(':') ?? -1;
            if (colon >= 0) ownerName = ownerName![..colon];
            if (!Identifier(ownerName)) { AddReason(reasons, "unsupported-member-name"); return []; }
            return type.AccessorMethods.TryGetValue((ownerKind, ownerName!, accessorKind), out var accessors)
                ? accessors.Distinct().ToArray() : [];
        }

        private void Associate(int[] candidates)
        {
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            foreach (var token in candidates) Enqueue(token);
            while (queue.Count != 0)
            {
                var token = queue.Dequeue();
                var method = Method(token);
                _work.Points(method.NonHiddenSequencePoints.Count);
                if (_relations.TryGetValue(token, out var related))
                    foreach (var target in related) Enqueue(target);
            }
            void Enqueue(int token)
            {
                if (!seen.Add(token)) return;
                _work.Reference();
                queue.Enqueue(token);
            }
        }

        private CoverageCandidateMethod Method(int token)
        {
            if (_methods.TryGetValue(token, out var existing)) return existing;
            Require(_metadata.TryGetValue(token, out var metadata), "pe-source-method-mismatch");
            Require(_debug.TryGetValue(token, out var source), "pe-source-method-mismatch");
            var points = source.SequencePoints.Where(point => !point.IsHidden).ToArray();
            foreach (var point in points)
                Require(_documentRows.Contains(point.DocumentRowId), "pe-source-document-mismatch");
            var reasons = new List<string>(metadata.UnresolvedReasons);
            if (points.Length == 0) AddReason(reasons, "no-nonhidden-sequence-points");
            foreach (var reason in reasons) _work.Text(reason);
            _work.Metadata(metadata);
            var method = new CoverageCandidateMethod(token, Scope(_types[metadata.DeclaringTypeToken]).Id, metadata,
                Array.AsReadOnly(points), reasons.AsReadOnly());
            _methods.Add(token, method);
            return method;
        }

        private CoverageCandidateScope Scope(TypeMethods type)
        {
            if (_typeScopes.TryGetValue(type.Token, out var scope)) return scope;
            Require(_scopes.Count < MaximumScopes, "scope-limit");
            _work.Text(type.Name);
            scope = new(_scopes.Count, CoverageCandidateScopeKind.Type, type.Token, type.Name);
            _scopes.Add(scope);
            _typeScopes.Add(type.Token, scope);
            return scope;
        }

        private void Link(int source, int target)
        {
            if (!_relations.TryGetValue(source, out var list)) _relations.Add(source, list = []);
            list.Add(target);
        }
    }

    private sealed class TypeMethods(int token, string name)
    {
        internal int Token { get; } = token;
        internal string Name { get; } = name;
        internal Dictionary<string, List<int>> ByName { get; } = new(StringComparer.Ordinal);
        internal List<int> Constructors { get; } = [];
        internal Dictionary<(PeAccessorOwnerKind OwnerKind, string Name, PeAccessorKind? Kind), List<int>> AccessorMethods { get; } = [];
        internal void Add(PeMethodRecord method)
        {
            if (!ByName.TryGetValue(method.MetadataName, out var list)) ByName.Add(method.MetadataName, list = []);
            list.Add(method.MetadataToken);
            if (method.MetadataName is ".ctor" or ".cctor") Constructors.Add(method.MetadataToken);
            foreach (var binding in method.Accessors)
            {
                AddAccessor(binding.OwnerKind, binding.OwnerName, null, method.MetadataToken);
                AddAccessor(binding.OwnerKind, binding.OwnerName, binding.Kind, method.MetadataToken);
            }
        }
        private void AddAccessor(PeAccessorOwnerKind owner, string name, PeAccessorKind? kind, int token)
        {
            if (!AccessorMethods.TryGetValue((owner, name, kind), out var methods))
                AccessorMethods.Add((owner, name, kind), methods = []);
            methods.Add(token);
        }
    }

    private sealed class Work(CoverageSourceCandidateBudget budget)
    {
        private int _references;
        private int _points;
        private int _text;
        private int _nodes;
        internal void Reference() => Require(++_references <= budget.MaximumCandidateReferences, "candidate-limit");
        internal void Points(int count)
        {
            _points = checked(_points + count);
            Require(_points <= budget.MaximumSequencePointAssociations, "sequence-point-limit");
        }
        internal void Text(string? value)
        {
            _text = checked(_text + (value?.Length ?? 0));
            Require(_text <= MaximumMetadataTextCharacters, "metadata-text-limit");
        }
        internal void Metadata(PeMethodRecord method)
        {
            Text(method.SignatureBlobHex);
            foreach (var parameter in method.ParameterMetadata) Text(parameter.Name);
            foreach (var parameter in method.GenericParameters.Concat(method.DeclaringTypeGenericParameters))
            {
                Text(parameter.Name);
                foreach (var constraint in parameter.Constraints) Type(constraint, 0);
            }
            foreach (var accessor in method.Accessors) Text(accessor.OwnerName);
            foreach (var reason in method.UnresolvedReasons) Text(reason);
            if (method.Signature is { } signature) Signature(signature, 0);
        }
        private void Signature(PeMethodSignature signature, int depth)
        {
            Type(signature.ReturnType, depth);
            foreach (var parameter in signature.ParameterTypes) Type(parameter, depth);
        }
        private void Type(PeSignatureType type, int depth)
        {
            Require(depth <= 128 && ++_nodes <= 1_000_000, "signature-work-limit");
            if (type.NamedType is { } named)
            {
                Text(named.Namespace);
                foreach (var name in named.TypeNames) Text(name);
                Text(named.Scope.ModuleName);
                if (named.Scope.Assembly is { } assembly)
                {
                    Text(assembly.Name); Text(assembly.Culture); Text(assembly.PublicKeyOrTokenHex);
                    Text(assembly.Version.ToString());
                }
            }
            if (type.ElementType is { } element) Type(element, depth + 1);
            if (type.ModifierType is { } modifier) Type(modifier, depth + 1);
            if (type.TypeArguments is { } arguments) foreach (var argument in arguments) Type(argument, depth + 1);
            if (type.FunctionPointerSignature is { } signature) Signature(signature, depth + 1);
        }
    }

    private static bool Container(string kind) => kind is "Root" or "Assembly" or "Namespace" or "Type";

    private static bool Identifier(string? value) => !string.IsNullOrEmpty(value) &&
        (value[0] == '_' || char.IsLetter(value[0])) && value.All(character => character == '_' || char.IsLetterOrDigit(character));

    private static bool MethodName(string? display, bool constructor, out string name)
    {
        name = "";
        if (display is null) return false;
        var open = display.IndexOf('(');
        var close = display.LastIndexOf(')');
        if (open <= 0 || close < open) return false;
        var prefix = display[..open];
        if (!Identifier(prefix) && !(constructor && prefix is ".ctor" or ".cctor")) return false;
        var suffix = display[(close + 1)..];
        if (constructor ? suffix is not ("" or ":void") : suffix.Length < 2 || suffix[0] != ':') return false;
        name = prefix;
        return true;
    }

    private static string Root(string path)
    {
        var full = InputPath(path);
        PortablePdbSource.RequireNoReparseAncestors(full, directory: true);
        return full;
    }

    private static string Child(string root, string relative)
    {
        Require(relative is not null && PortablePdbSource.RelativePath(relative), "invalid-path");
        return PortablePdbSource.ResolveSource(root, relative);
    }

    private static string InputPath(string path)
    {
        Require(path is not null && path.Length <= 32768 && Path.IsPathFullyQualified(path), "invalid-path");
        var rawRoot = Path.GetPathRoot(path);
        Require(rawRoot is { Length: 3 } && char.IsAsciiLetter(rawRoot[0]) && rawRoot[1] == ':' &&
            rawRoot[2] is '/' or '\\', "invalid-path");
        var relative = path[rawRoot.Length..].Replace('\\', '/').TrimEnd('/');
        Require(relative.Length == 0 || PortablePdbSource.RelativePath(relative), "invalid-path");
        return PortablePdbSource.AbsolutePath(path);
    }

    private static void AddReason(List<string> reasons, string reason)
    {
        if (!reasons.Contains(reason, StringComparer.Ordinal)) reasons.Add(reason);
    }

    private static CoverageSourceCandidatesException Rejected(string reason) => new("coverage-source-candidates-" + reason);
    private static void Require([DoesNotReturnIf(false)] bool condition, string reason)
    {
        if (!condition) throw Rejected(reason);
    }
}
