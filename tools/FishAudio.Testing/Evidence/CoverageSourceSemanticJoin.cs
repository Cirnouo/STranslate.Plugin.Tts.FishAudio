using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace FishAudio.Testing.Evidence;

public enum CoverageSemanticJoinStatus { Unresolved, ExactDeclaration, RelatedKickoffOnly }

/// <summary>A declaration in one checksum-verified compiler document, not an executed source-line observation.</summary>
public sealed record CoverageSemanticDeclaration(int DocumentRowId, string RelativePath, int Start, int Length,
    string DeclaringType, string MetadataName, string MethodKind, int GenericArity, string? AccessorOwnerName);

public sealed record CoverageSemanticMethodJoin(int MetadataToken, int ScopeId, CoverageSemanticJoinStatus Status,
    CoverageSemanticDeclaration? Declaration, int? RelatedKickoffToken,
    CoverageSemanticDeclaration? RelatedKickoffDeclaration, CoverageCandidateFallback Fallback,
    IReadOnlyList<string> UnresolvedReasons);

public sealed record CoverageSemanticDocument(int DocumentRowId, string RelativePath,
    BuildSourceFileProvenance Provenance, string SourceSha256, bool CompilationTextMatched,
    string? LanguageVersion, string? SourceKind, IReadOnlyList<string> PreprocessorSymbols,
    IReadOnlyList<string> UnresolvedReasons);

/// <summary>
/// Records observed options and resolved reference identities of the actual supplied
/// compilation. ReferenceIdentitySha256 covers identities/aliases, not reference bytes.
/// No project evaluation, compiler invocation or Product-build equivalence is authenticated.
/// </summary>
public sealed record CoverageSemanticCompilation(string AssemblyName, string RoslynVersion, string Optimization,
    string OutputKind, string Platform, bool AllowUnsafe, bool CheckOverflow, string NullableContext,
    int SyntaxTreeCount, int ReferenceCount, string ReferenceIdentitySha256, bool HasCompilationErrors)
{
    public string Authority => "caller-supplied-compilation";
    public bool ProducedProductBinaryProven => false;
    public bool ReferenceBytesAuthenticated => false;
}

public sealed record CoverageSourceSemanticJoinEvidence(int SchemaVersion, BinaryIdentityRecord Product,
    string AggregateSha256, CoverageSemanticCompilation Compilation,
    IReadOnlyList<CoverageSemanticDocument> Documents, IReadOnlyList<CoverageSemanticMethodJoin> Methods)
{
    public string Scope => "candidate-token-declarations-only";
    public bool SourceMappingsResolved => false;
    public bool DependencyClosureProven => false;
    public bool BuildInputClosureProven => false;
    public bool GeneratorInputClosureProven => false;
    public bool CompleteBaseline => false;
    public bool BaselineClaim => false;
}

public sealed class CoverageSourceSemanticJoinException : Exception
{
    internal CoverageSourceSemanticJoinException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Bounded semantic construction after independent graph admission. This is not a
/// raw coverage/session admission overload: the caller owns the graph's prior
/// DLL/PDB/source validation and the supplied compilation's project provenance.
/// Uses that compilation directly, preserving its evaluated references, options,
/// defines and generated trees. Reads only exact graph paths beneath the explicit
/// snapshot; no workspace loading, reference search, assembly execution or fallback
/// to another checkout occurs. All global admission/closure flags stay false.
/// </summary>
public static class CoverageSourceSemanticJoin
{
    private const int MaximumDocuments = 65_536;
    private const int MaximumMethods = 100_000;
    private const int MaximumSequencePoints = 2_000_000;
    private const long MaximumSourceBytes = 16 * 1024 * 1024;
    private const long MaximumTotalSourceBytes = 256 * 1024 * 1024;
    private static readonly Guid Sha1Algorithm = new("ff1816ec-aa5e-4d10-87f7-6f4963833460");
    private static readonly Guid Sha256Algorithm = new("8829d00f-11b8-4213-878b-770e8597ac16");

    public static CoverageSourceSemanticJoinEvidence Inspect(CoverageSourceCandidateGraph graph,
        CSharpCompilation compilation, string absoluteSnapshotRoot, IReadOnlyList<string> generatedSourceRoots,
        CancellationToken cancellationToken = default)
    {
        var guards = new List<FileStream>();
        var completed = false;
        try
        {
            Require(graph is not null && graph.SchemaVersion == 1 && graph.Product is not null &&
                graph.Documents is not null && graph.Methods is not null && graph.Scopes is not null &&
                graph.Documents.Count <= MaximumDocuments && graph.Methods.Count <= MaximumMethods && graph.Scopes.Count <= MaximumMethods + 1 &&
                compilation is not null && compilation.SyntaxTrees.Count() <= 200_000 &&
                generatedSourceRoots is not null && generatedSourceRoots.Count <= 128, "invalid-input");
            cancellationToken.ThrowIfCancellationRequested();
            // This is a construction seam over already admitted records. Copy its
            // outer collections, and validate all coordinates/paths again before use.
            var documents = graph.Documents.ToArray();
            var methods = graph.Methods.ToArray();
            Require(documents.All(item => item is not null && item.Provenance is not null && item.CompilerDocument is not null) &&
                methods.All(item => item is not null) && graph.Scopes.All(item => item is not null), "invalid-input");
            var scopes = graph.Scopes.ToDictionary(item => item.Id);
            var byToken = methods.ToDictionary(item => item.MetadataToken);
            Require(methods.All(item => item.Metadata is not null && item.MetadataToken == item.Metadata.MetadataToken &&
                item.NonHiddenSequencePoints is not null && scopes.ContainsKey(item.ScopeId)), "invalid-input");
            long points = 0;
            foreach (var method in methods)
            {
                points += method.NonHiddenSequencePoints.Count;
                Require(points <= MaximumSequencePoints, "sequence-point-limit");
            }
            PreflightCompilation(compilation, cancellationToken);
            var root = SnapshotRoot(absoluteSnapshotRoot);
            var generatedRoots = GeneratedRoots(generatedSourceRoots, documents);
            var source = ReadDocuments(documents, compilation, root, generatedRoots, guards, cancellationToken);
            var context = CompilationContext(compilation, cancellationToken);
            var resolver = new Resolver(compilation, source, byToken, scopes, cancellationToken);
            var results = new Dictionary<int, CoverageSemanticMethodJoin>();
            var compilationReason = context.AssemblyName != graph.Product.AssemblyName ? "compilation-product-name-mismatch" :
                context.HasCompilationErrors ? "compilation-errors" : null;
            foreach (var method in methods.OrderBy(item => item.MetadataToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (compilationReason is not null) results.Add(method.MetadataToken, resolver.Unknown(method, compilationReason));
                else if (method.Metadata.StateMachineKickoffMethodToken is null)
                    results.Add(method.MetadataToken, resolver.Resolve(method));
            }
            if (compilationReason is null)
                foreach (var method in methods.Where(item => item.Metadata.StateMachineKickoffMethodToken is not null))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var kickoff = method.Metadata.StateMachineKickoffMethodToken!.Value;
                    var related = results.TryGetValue(kickoff, out var target) ? target.Declaration : null;
                    var unknown = resolver.Unknown(method, related is null ? "kickoff-declaration-unresolved" : "state-machine-body-related-only");
                    results.Add(method.MetadataToken, unknown with
                    {
                        Status = related is null ? CoverageSemanticJoinStatus.Unresolved : CoverageSemanticJoinStatus.RelatedKickoffOnly,
                        RelatedKickoffToken = kickoff,
                        RelatedKickoffDeclaration = related,
                    });
                }
            var result = new CoverageSourceSemanticJoinEvidence(1, graph.Product with { }, graph.AggregateSha256,
                context, Array.AsReadOnly(source.Values.OrderBy(item => item.Evidence.DocumentRowId).Select(item => item.Evidence).ToArray()),
                Array.AsReadOnly(results.Values.OrderBy(item => item.MetadataToken).ToArray()));
            completed = true;
            return result;
        }
        catch (CoverageSourceSemanticJoinException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is PortablePdbSourceException or IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException or NotSupportedException or CryptographicException or
            OverflowException or System.Security.SecurityException)
        {
            throw Rejected("invalid-input-or-source");
        }
        finally
        {
            var failed = false;
            foreach (var guard in guards)
            {
                try { guard.Dispose(); }
                catch { failed = true; }
            }
            if (failed && completed) throw Rejected("source-release-failed");
        }
    }

    private static Dictionary<int, SourceDocument> ReadDocuments(CoverageCandidateDocument[] documents,
        CSharpCompilation compilation, string root, string[] generatedRoots, List<FileStream> guards, CancellationToken token)
    {
        var result = new Dictionary<int, SourceDocument>();
        var physical = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trees = compilation.SyntaxTrees.Where(tree => !string.IsNullOrEmpty(tree.FilePath) && Path.IsPathFullyQualified(tree.FilePath))
            .GroupBy(tree => Path.GetFullPath(tree.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var entry in documents)
        {
            token.ThrowIfCancellationRequested();
            var source = entry.Provenance;
            var compiler = entry.CompilerDocument;
            Require(source is not null && compiler is not null && source.DocumentRowId > 0 &&
                source.DocumentRowId == compiler.RowId && source.DocumentName == compiler.DocumentName &&
                source.SnapshotRelativePath == compiler.SnapshotRelativePath && source.Sha256 == compiler.SourceSha256 &&
                PortablePdbSource.RelativePath(source.SnapshotRelativePath) && physical.Add(source.SnapshotRelativePath), "invalid-document");
            if (source.Provenance == BuildSourceFileProvenance.BuildGenerated)
                Require(source.GeneratedRoot is not null && generatedRoots.Count(item => item == source.GeneratedRoot &&
                    source.SnapshotRelativePath.StartsWith(item + "/", StringComparison.Ordinal)) == 1, "generated-document-outside-root");
            else Require(source.Provenance == BuildSourceFileProvenance.Tracked && source.GeneratedRoot is null, "invalid-document-provenance");
            var path = PortablePdbSource.ResolveSource(root, source.SnapshotRelativePath);
            var guard = PortablePdbSource.OpenRead(path, MaximumSourceBytes, allowEmpty: true);
            guards.Add(guard);
            totalBytes += guard.Length;
            Require(totalBytes <= MaximumTotalSourceBytes, "source-size-limit");
            var bytes = new byte[checked((int)guard.Length)];
            guard.ReadExactly(bytes);
            Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == source.Sha256, "source-bytes-mismatch");
            Require(compiler.HashAlgorithm == Sha1Algorithm || compiler.HashAlgorithm == Sha256Algorithm, "source-checksum-algorithm");
            var checksum = compiler.HashAlgorithm == Sha1Algorithm ? SHA1.HashData(bytes) : SHA256.HashData(bytes);
            Require(Convert.ToHexStringLower(checksum) == compiler.Checksum, "compiler-checksum-mismatch");
            SyntaxTree? tree = null;
            string? reason = "compilation-document-unavailable";
            if (trees.TryGetValue(path, out var matches))
            {
                if (matches.Length != 1) reason = "compilation-document-ambiguous";
                else
                {
                    tree = matches[0];
                    var text = tree.GetText(token);
                    using var raw = new MemoryStream(bytes, writable: false);
                    // SourceText created from the original stream retains its raw
                    // checksum, including BOM/encoding. Re-encoding ToString() would
                    // erase this identity and is deliberately never used here.
                    var exact = SourceText.From(raw, encoding: null, checksumAlgorithm: text.ChecksumAlgorithm,
                        throwIfBinaryDetected: true);
                    if (text.Encoding is not null && text.ContentEquals(exact) && text.GetChecksum().SequenceEqual(exact.GetChecksum())) reason = null;
                    else reason = "compilation-document-mismatch";
                }
            }
            var options = tree?.Options as CSharpParseOptions;
            var evidence = new CoverageSemanticDocument(source.DocumentRowId, source.SnapshotRelativePath,
                source.Provenance, source.Sha256, reason is null, options?.LanguageVersion.ToString(), options?.Kind.ToString(),
                Array.AsReadOnly(options?.PreprocessorSymbolNames.Order(StringComparer.Ordinal).ToArray() ?? []),
                reason is null ? Array.Empty<string>() : Array.AsReadOnly(new[] { reason }));
            Require(result.TryAdd(source.DocumentRowId, new(evidence, reason is null ? tree : null)), "duplicate-document-row");
        }
        return result;
    }

    private static string SnapshotRoot(string path)
    {
        Require(path is not null && path.Length <= 32_768 && Path.IsPathFullyQualified(path), "snapshot-root-invalid");
        var rawRoot = Path.GetPathRoot(path);
        Require(rawRoot is { Length: 3 } && char.IsAsciiLetter(rawRoot[0]) && rawRoot[1] == ':' && rawRoot[2] is '/' or '\\',
            "snapshot-root-invalid");
        var rawRelative = path[rawRoot.Length..].Replace('\\', '/').TrimEnd('/');
        Require(rawRelative.Length == 0 || PortablePdbSource.RelativePath(rawRelative), "snapshot-root-invalid");
        var full = PortablePdbSource.AbsolutePath(path);
        Require(Directory.Exists(full), "snapshot-root-invalid");
        PortablePdbSource.RequireNoReparseAncestors(full, directory: true);
        return full;
    }

    private static string[] GeneratedRoots(IReadOnlyList<string> roots, CoverageCandidateDocument[] documents)
    {
        var copy = roots.ToArray();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in copy)
        {
            Require(PortablePdbSource.RelativePath(root) && unique.Add(root), "generated-root-invalid");
            foreach (var entry in documents)
                if (entry.Provenance.Provenance == BuildSourceFileProvenance.Tracked)
                    Require(!Overlap(root, entry.Provenance.SnapshotRelativePath), "generated-root-overlaps-tracked");
        }
        for (var index = 0; index < copy.Length; index++)
            for (var other = index + 1; other < copy.Length; other++)
                Require(!Overlap(copy[index], copy[other]), "generated-root-ambiguous");
        return copy;
    }

    private static bool Overlap(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase) ||
        left.StartsWith(right + "/", StringComparison.OrdinalIgnoreCase) || right.StartsWith(left + "/", StringComparison.OrdinalIgnoreCase);

    private static CoverageSemanticCompilation CompilationContext(CSharpCompilation compilation, CancellationToken token)
    {
        long identityCharacters = 0;
        var references = compilation.References.Select(reference =>
        {
            var symbol = compilation.GetAssemblyOrModuleSymbol(reference);
            var identity = symbol is IAssemblySymbol assembly ? assembly.Identity.ToString() : "[unsupported-reference-kind]";
            identityCharacters += identity.Length;
            Require(identity.Length <= 32_768 && identityCharacters <= 16 * 1024 * 1024, "compilation-reference-identity-limit");
            return identity + "|" + reference.Properties.EmbedInteropTypes + "|" + string.Join(",", reference.Properties.Aliases.Order(StringComparer.Ordinal));
        }).Order(StringComparer.Ordinal);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", references))));
        var options = compilation.Options;
        return new(compilation.AssemblyName ?? "", typeof(CSharpCompilation).Assembly.GetName().Version?.ToString() ?? "[unavailable]",
            options.OptimizationLevel.ToString(), options.OutputKind.ToString(), options.Platform.ToString(), options.AllowUnsafe,
            options.CheckOverflow, options.NullableContextOptions.ToString(), compilation.SyntaxTrees.Count(), compilation.References.Count(),
            fingerprint, compilation.GetDiagnostics(token).Any(item => item.Severity == DiagnosticSeverity.Error));
    }

    private static void PreflightCompilation(CSharpCompilation compilation, CancellationToken token)
    {
        Require(compilation.References.Count() <= 4096 && compilation.Options.SpecificDiagnosticOptions.Count <= 4096,
            "compilation-input-limit");
        long textCharacters = 0;
        foreach (var tree in compilation.SyntaxTrees)
        {
            token.ThrowIfCancellationRequested();
            var text = tree.GetText(token);
            textCharacters += text.Length;
            Require(text.Length <= 16 * 1024 * 1024 && textCharacters <= 128 * 1024 * 1024,
                "compilation-text-limit");
        }
        foreach (var reference in compilation.References)
        {
            token.ThrowIfCancellationRequested();
            Require(reference.Properties.Aliases.Length <= 128 && reference.Properties.Aliases.All(alias => alias.Length <= 1024),
                "compilation-reference-alias-limit");
        }
    }

    private sealed record SourceDocument(CoverageSemanticDocument Evidence, SyntaxTree? Tree);

    private sealed class Resolver(CSharpCompilation compilation, Dictionary<int, SourceDocument> documents,
        Dictionary<int, CoverageCandidateMethod> methods, Dictionary<int, CoverageCandidateScope> scopes, CancellationToken token)
    {
        private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];
        private readonly SignatureMatcher _matcher = new(compilation);
        private readonly Dictionary<int, PortablePdbSequencePoint[]> _kickoffAnchors = methods.Values
            .Where(item => item.Metadata.StateMachineKickoffMethodToken is not null)
            .GroupBy(item => item.Metadata.StateMachineKickoffMethodToken!.Value)
            .ToDictionary(group => group.Key, group => group.SelectMany(item => item.NonHiddenSequencePoints).ToArray());

        internal CoverageSemanticMethodJoin Resolve(CoverageCandidateMethod candidate)
        {
            if (candidate.Metadata.UnresolvedReasons.Count != 0 || candidate.Metadata.Signature is null)
                return Unknown(candidate, "metadata-signature-unresolved");
            // Portable PDB explicitly relates a state-machine body to its kickoff.
            // Only the kickoff signature is tested against the source declaration.
            var anchors = candidate.NonHiddenSequencePoints.Concat(_kickoffAnchors.GetValueOrDefault(candidate.MetadataToken) ?? []);
            CoverageSemanticDeclaration? declaration = null;
            IMethodSymbol? symbol = null;
            foreach (var point in anchors)
            {
                token.ThrowIfCancellationRequested();
                if (!documents.TryGetValue(point.DocumentRowId, out var document)) return Unknown(candidate, "sequence-point-document-unavailable");
                if (document.Tree is null) return Unknown(candidate, document.Evidence.UnresolvedReasons[0]);
                var tree = document.Tree;
                if (!TrySpan(tree.GetText(token), point, out var span)) return Unknown(candidate, "sequence-point-span-unavailable");
                if (!_models.TryGetValue(tree, out var model)) _models.Add(tree, model = compilation.GetSemanticModel(tree));
                var syntax = DeclarationNode(tree.GetRoot(token).FindNode(span, findInsideTrivia: false, getInnermostNodeForTie: true));
                if (syntax is null) return Unknown(candidate, "declaration-symbol-or-signature-mismatch");
                var resolved = DeclaredMethod(model, syntax, candidate.Metadata, token);
                if (resolved is null || !_matcher.Matches(candidate.Metadata, resolved))
                    return Unknown(candidate, "declaration-symbol-or-signature-mismatch");
                var current = new CoverageSemanticDeclaration(document.Evidence.DocumentRowId, document.Evidence.RelativePath,
                    syntax.SpanStart, syntax.Span.Length, MetadataTypeName(resolved.ContainingType!), resolved.MetadataName,
                    resolved.MethodKind.ToString(), resolved.Arity, resolved.AssociatedSymbol?.MetadataName);
                if (symbol is not null && (!SymbolEqualityComparer.Default.Equals(symbol, resolved) || declaration != current))
                    return Unknown(candidate, "ambiguous-source-declaration");
                declaration = current;
                symbol = resolved;
            }
            if (declaration is null) return Unknown(candidate, "no-sequence-point-declaration");
            return new(candidate.MetadataToken, candidate.ScopeId, CoverageSemanticJoinStatus.ExactDeclaration, declaration,
                null, null, CoverageCandidateFallback.None, Array.Empty<string>());
        }

        internal CoverageSemanticMethodJoin Unknown(CoverageCandidateMethod candidate, string reason) =>
            new(candidate.MetadataToken, candidate.ScopeId, CoverageSemanticJoinStatus.Unresolved, null, null, null,
                scopes[candidate.ScopeId].Kind == CoverageCandidateScopeKind.Type ? CoverageCandidateFallback.Type : CoverageCandidateFallback.Assembly,
                Array.AsReadOnly(new[] { reason }));

        private static SyntaxNode? DeclarationNode(SyntaxNode node)
        {
            foreach (var ancestor in node.AncestorsAndSelf())
            {
                if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax) return null;
                if (ancestor is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or BasePropertyDeclarationSyntax) return ancestor;
                if (ancestor is BaseTypeDeclarationSyntax or CompilationUnitSyntax) return null;
            }
            return null;
        }

        private static IMethodSymbol? DeclaredMethod(SemanticModel model, SyntaxNode node, PeMethodRecord metadata, CancellationToken token)
        {
            var symbol = model.GetDeclaredSymbol(node, token);
            if (symbol is IMethodSymbol method) return method.PartialImplementationPart ?? method;
            if (metadata.Accessors.Count != 1) return null;
            return (symbol, metadata.Accessors[0].Kind) switch
            {
                (IPropertySymbol property, PeAccessorKind.Getter) => property.GetMethod,
                (IPropertySymbol property, PeAccessorKind.Setter) => property.SetMethod,
                (IEventSymbol @event, PeAccessorKind.Add) => @event.AddMethod,
                (IEventSymbol @event, PeAccessorKind.Remove) => @event.RemoveMethod,
                _ => null,
            };
        }

        private static bool TrySpan(SourceText text, PortablePdbSequencePoint point, out TextSpan span)
        {
            span = default;
            if (point.IsHidden || point.StartLine < 1 || point.EndLine < point.StartLine || point.EndLine > text.Lines.Count ||
                point.StartColumn < 1 || point.EndColumn < 1) return false;
            var startLine = text.Lines[point.StartLine - 1];
            var endLine = text.Lines[point.EndLine - 1];
            if (point.StartColumn - 1 > startLine.Span.Length || point.EndColumn - 1 > endLine.Span.Length) return false;
            var start = startLine.Start + point.StartColumn - 1;
            var end = endLine.Start + point.EndColumn - 1;
            if (end <= start) return false;
            span = TextSpan.FromBounds(start, end);
            return true;
        }
    }

    private sealed class SignatureMatcher(CSharpCompilation compilation)
    {
        private int _nodes;

        internal bool Matches(PeMethodRecord metadata, IMethodSymbol symbol)
        {
            _nodes = 0;
            var signature = metadata.Signature;
            if (signature is null || symbol.IsImplicitlyDeclared || symbol.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction ||
                symbol.MetadataName != metadata.MetadataName || symbol.ContainingType is not { } containingType ||
                MetadataTypeName(containingType) != metadata.DeclaringType ||
                !SymbolEqualityComparer.Default.Equals(symbol.ContainingModule, compilation.SourceModule) ||
                symbol.IsStatic == signature.IsInstance || signature.HasExplicitThis || signature.CallingConvention != SignatureCallingConvention.Default ||
                signature.RequiredParameterCount != signature.ParameterTypes.Count || signature.ParameterTypes.Count != symbol.Parameters.Length ||
                signature.ParameterTypes.Count > 128 || metadata.ParameterMetadata.Count > 129 ||
                signature.GenericParameterCount != symbol.Arity || signature.HeaderRawValue != ((signature.IsInstance ? 0x20 : 0) | (symbol.Arity > 0 ? 0x10 : 0)) ||
                symbol.IsVararg || symbol.ReturnsByRefReadonly || !symbol.ReturnTypeCustomModifiers.IsEmpty || !symbol.RefCustomModifiers.IsEmpty ||
                !ReturnType(signature.ReturnType, symbol) || !Accessor(metadata, symbol)) return false;
            var typeParameters = ContainingTypes(containingType).SelectMany(type => type.TypeParameters).ToArray();
            if (!Generics(metadata.GenericParameters, symbol.TypeParameters) ||
                !Generics(metadata.DeclaringTypeGenericParameters, typeParameters)) return false;
            for (var index = 0; index < symbol.Parameters.Length; index++)
            {
                var parameter = symbol.Parameters[index];
                if (!parameter.CustomModifiers.IsEmpty || !parameter.RefCustomModifiers.IsEmpty || parameter.IsOptional || parameter.IsParams ||
                    parameter.HasExplicitDefaultValue) return false;
                var type = signature.ParameterTypes[index];
                var rows = metadata.ParameterMetadata.Where(item => item.Sequence == index + 1).ToArray();
                if (rows.Length > 1) return false;
                var flags = rows.Length == 0 ? (ParameterAttributes)0 : rows[0].Attributes;
                if ((flags & ~(ParameterAttributes.In | ParameterAttributes.Out)) != 0) return false;
                var expectedRef = parameter.RefKind switch
                {
                    RefKind.None => (ByRef: false, Flags: (ParameterAttributes)0),
                    RefKind.Ref => (ByRef: true, Flags: (ParameterAttributes)0),
                    RefKind.Out => (ByRef: true, Flags: ParameterAttributes.Out),
                    RefKind.In => (ByRef: true, Flags: ParameterAttributes.In),
                    _ => (ByRef: false, Flags: (ParameterAttributes)(-1)),
                };
                if (flags != expectedRef.Flags || (type.Kind == PeSignatureTypeKind.ByReference) != expectedRef.ByRef) return false;
                if (!Type(expectedRef.ByRef ? type.ElementType : type, parameter.Type, 0)) return false;
            }
            return true;
        }

        private bool ReturnType(PeSignatureType metadata, IMethodSymbol symbol) =>
            (metadata.Kind == PeSignatureTypeKind.ByReference) == symbol.ReturnsByRef &&
            Type(symbol.ReturnsByRef ? metadata.ElementType : metadata, symbol.ReturnType, 0);

        private static bool Accessor(PeMethodRecord metadata, IMethodSymbol symbol)
        {
            if (symbol.AssociatedSymbol is null) return metadata.Accessors.Count == 0;
            if (metadata.Accessors.Count != 1) return false;
            var binding = metadata.Accessors[0];
            if (binding.OwnerName != symbol.AssociatedSymbol.MetadataName) return false;
            return (symbol.AssociatedSymbol, symbol.MethodKind, binding.OwnerKind, binding.Kind) switch
            {
                (IPropertySymbol, MethodKind.PropertyGet, PeAccessorOwnerKind.Property, PeAccessorKind.Getter) => true,
                (IPropertySymbol, MethodKind.PropertySet, PeAccessorOwnerKind.Property, PeAccessorKind.Setter) => true,
                (IEventSymbol, MethodKind.EventAdd, PeAccessorOwnerKind.Event, PeAccessorKind.Add) => true,
                (IEventSymbol, MethodKind.EventRemove, PeAccessorOwnerKind.Event, PeAccessorKind.Remove) => true,
                _ => false,
            };
        }

        private bool Generics(IReadOnlyList<PeGenericParameterMetadata> metadata, IReadOnlyList<ITypeParameterSymbol> symbols)
        {
            if (metadata.Count != symbols.Count || metadata.Count > 128) return false;
            for (var index = 0; index < symbols.Count; index++)
            {
                var symbol = symbols[index];
                var rows = metadata.Where(item => item.Index == index).ToArray();
                if (rows.Length != 1 || symbol.HasUnmanagedTypeConstraint || symbol.HasNotNullConstraint ||
                    symbol.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated) return false;
                var row = rows[0];
                var flags = symbol.Variance switch
                {
                    VarianceKind.In => GenericParameterAttributes.Contravariant,
                    VarianceKind.Out => GenericParameterAttributes.Covariant,
                    _ => GenericParameterAttributes.None,
                };
                if (symbol.HasReferenceTypeConstraint) flags |= GenericParameterAttributes.ReferenceTypeConstraint;
                if (symbol.HasValueTypeConstraint) flags |= GenericParameterAttributes.NotNullableValueTypeConstraint;
                if (symbol.HasConstructorConstraint || symbol.HasValueTypeConstraint) flags |= GenericParameterAttributes.DefaultConstructorConstraint;
                if (row.Attributes != flags || row.Name != symbol.Name || row.Constraints.Count > 128) return false;
                var expected = symbol.ConstraintTypes.ToList();
                if (symbol.HasValueTypeConstraint) expected.Add(compilation.GetSpecialType(SpecialType.System_ValueType));
                if (row.Constraints.Count != expected.Count) return false;
                var used = new HashSet<int>();
                foreach (var constraint in row.Constraints)
                {
                    var matches = Enumerable.Range(0, expected.Count).Where(item => !used.Contains(item) && Type(constraint, expected[item], 0)).ToArray();
                    if (matches.Length != 1) return false;
                    used.Add(matches[0]);
                }
            }
            return true;
        }

        private bool Type(PeSignatureType? metadata, ITypeSymbol symbol, int depth)
        {
            if (metadata is null || depth > 64 || ++_nodes > 16_384 || symbol.TypeKind == TypeKind.Error) return false;
            switch (metadata.Kind)
            {
                case PeSignatureTypeKind.Primitive:
                    return metadata.Primitive == Primitive(symbol.SpecialType) && symbol.TypeKind != TypeKind.Dynamic;
                case PeSignatureTypeKind.Named:
                    return symbol is INamedTypeSymbol named && !named.IsGenericType && Named(metadata.NamedType, named);
                case PeSignatureTypeKind.GenericInstantiation:
                    if (symbol is not INamedTypeSymbol generic || metadata.ElementType is not { Kind: PeSignatureTypeKind.Named } ||
                        !Named(metadata.ElementType.NamedType, generic.OriginalDefinition) || metadata.TypeArguments is null) return false;
                    var arguments = ContainingTypes(generic).SelectMany(type => type.TypeArguments).ToArray();
                    return arguments.Length == metadata.TypeArguments.Count && arguments.Select((argument, index) =>
                        Type(metadata.TypeArguments[index], argument, depth + 1)).All(item => item);
                case PeSignatureTypeKind.TypeParameter:
                    return symbol is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Type } typeParameter &&
                        metadata.GenericParameterIndex == TypeParameterIndex(typeParameter);
                case PeSignatureTypeKind.MethodParameter:
                    return symbol is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } methodParameter &&
                        metadata.GenericParameterIndex == methodParameter.Ordinal;
                case PeSignatureTypeKind.Pointer:
                    return symbol is IPointerTypeSymbol pointer && Type(metadata.ElementType, pointer.PointedAtType, depth + 1);
                case PeSignatureTypeKind.SzArray:
                    return symbol is IArrayTypeSymbol { IsSZArray: true } array && Type(metadata.ElementType, array.ElementType, depth + 1);
                case PeSignatureTypeKind.Array:
                    return symbol is IArrayTypeSymbol { IsSZArray: false } shaped && metadata.ArrayShape is { } shape &&
                        shape.Rank == shaped.Rank && shape.Sizes.Count == 0 && shape.LowerBounds.All(item => item == 0) &&
                        Type(metadata.ElementType, shaped.ElementType, depth + 1);
                // Custom modifiers, pinned and function-pointer signatures, unresolved
                // types and nested byrefs need a later explicit contract. Never erase them.
                default: return false;
            }
        }

        private bool Named(PeNamedTypeIdentity? metadata, INamedTypeSymbol symbol)
        {
            if (metadata is null || metadata.RawTypeKind is not (0 or 0x11 or 0x12) ||
                metadata.RawTypeKind != 0 && (metadata.RawTypeKind == 0x11) != symbol.IsValueType ||
                metadata.Namespace != Namespace(symbol.ContainingNamespace) ||
                !metadata.TypeNames.SequenceEqual(ContainingTypes(symbol).Select(type => type.MetadataName))) return false;
            if (metadata.Scope.Kind == PeTypeResolutionScopeKind.CurrentModule)
                return SymbolEqualityComparer.Default.Equals(symbol.ContainingModule, compilation.SourceModule) &&
                    metadata.Scope.ModuleName == compilation.SourceModule.Name;
            if (metadata.Scope.Kind != PeTypeResolutionScopeKind.AssemblyReference || metadata.Scope.Assembly is not { } expected) return false;
            var actual = symbol.ContainingAssembly.Identity;
            var flags = (uint)expected.Flags;
            if ((flags & ~0x301u) != 0 || ((flags & 0x100u) != 0) != actual.IsRetargetable ||
                ((flags & 0x200u) != 0) != (actual.ContentType == AssemblyContentType.WindowsRuntime)) return false;
            var key = (flags & 1u) != 0 ? actual.PublicKey : actual.PublicKeyToken;
            return expected.Name == actual.Name && expected.Version == actual.Version && expected.Culture == actual.CultureName &&
                expected.PublicKeyOrTokenHex == Convert.ToHexStringLower(key.AsSpan());
        }

        private static int TypeParameterIndex(ITypeParameterSymbol parameter) => parameter.ContainingType is { } owner
            ? parameter.Ordinal + ContainingTypes(owner).TakeWhile(type => !SymbolEqualityComparer.Default.Equals(type, owner)).Sum(type => type.Arity)
            : -1;

        private static PrimitiveTypeCode? Primitive(SpecialType type) => type switch
        {
            SpecialType.System_Void => PrimitiveTypeCode.Void, SpecialType.System_Boolean => PrimitiveTypeCode.Boolean,
            SpecialType.System_Char => PrimitiveTypeCode.Char, SpecialType.System_SByte => PrimitiveTypeCode.SByte,
            SpecialType.System_Byte => PrimitiveTypeCode.Byte, SpecialType.System_Int16 => PrimitiveTypeCode.Int16,
            SpecialType.System_UInt16 => PrimitiveTypeCode.UInt16, SpecialType.System_Int32 => PrimitiveTypeCode.Int32,
            SpecialType.System_UInt32 => PrimitiveTypeCode.UInt32, SpecialType.System_Int64 => PrimitiveTypeCode.Int64,
            SpecialType.System_UInt64 => PrimitiveTypeCode.UInt64, SpecialType.System_Single => PrimitiveTypeCode.Single,
            SpecialType.System_Double => PrimitiveTypeCode.Double, SpecialType.System_String => PrimitiveTypeCode.String,
            SpecialType.System_Object => PrimitiveTypeCode.Object, SpecialType.System_IntPtr => PrimitiveTypeCode.IntPtr,
            SpecialType.System_UIntPtr => PrimitiveTypeCode.UIntPtr, SpecialType.System_TypedReference => PrimitiveTypeCode.TypedReference,
            _ => null,
        };
    }

    private static IEnumerable<INamedTypeSymbol> ContainingTypes(INamedTypeSymbol symbol)
    {
        var types = new Stack<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.ContainingType) types.Push(current);
        return types;
    }

    private static string MetadataTypeName(INamedTypeSymbol symbol) =>
        (symbol.ContainingNamespace is null || symbol.ContainingNamespace.IsGlobalNamespace ? "" : Namespace(symbol.ContainingNamespace) + ".") +
        string.Join("+", ContainingTypes(symbol).Select(type => type.MetadataName));

    private static string Namespace(INamespaceSymbol? symbol)
    {
        var parts = new Stack<string>();
        for (var current = symbol; current is not null && !current.IsGlobalNamespace; current = current.ContainingNamespace) parts.Push(current.MetadataName);
        return string.Join(".", parts);
    }

    private static CoverageSourceSemanticJoinException Rejected(string reason) => new("source-semantic-join-" + reason);

    private static void Require([DoesNotReturnIf(false)] bool condition, string reason)
    {
        if (!condition) throw Rejected(reason);
    }
}
