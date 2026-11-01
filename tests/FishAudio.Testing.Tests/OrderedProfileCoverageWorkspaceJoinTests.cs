using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FishAudio.Testing.Evidence;
using Microsoft.Build.Locator;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
public sealed class CoverageWorkspaceDiagnosticFactsTests
{
    private const string Root = @"C:\PrivateMachineOwner\snapshot";
    private const string Generated = "STranslate.Plugin.Tts.FishAudio/obj/coverage/Release/net10.0-windows/Presentation/View/InlineNumericEditor.g.cs";
    private const string Tracked = "STranslate.Plugin.Tts.FishAudio/Presentation/View/InlineNumericEditor.xaml.cs";

    [Test]
    [Property("FishAudioCaseId", "coverage-group-workspace/diagnostic-sharing-facts-are-safe-lexical-mentions")]
    public void AQuotedGeneratedFileSurvivesTheNestedWpfErrorWrapperWithoutExposingTheMessage()
    {
        // Representative WPF UnknownBuildError wrapping the English IOException
        // wording; the previous actual workspace discarded its raw message.
        var path = Root + "/" + Generated;
        var message = "Unknown build error, 'System.IO.IOException: The process cannot access the file '" +
            path + "' because it is being used by another process. (0x80070020)'";
        var facts = Index().Describe(message);
        var diagnostic = new CoverageWorkspaceDiagnostic("workspace", "Failure", "workspace-diagnostic") { Facts = facts };
        var json = JsonSerializer.Serialize(diagnostic);
        Assert.Multiple(() =>
        {
            Assert.That(facts.MessageSha256, Is.EqualTo(Digest(message)));
            Assert.That(facts.KnownRelativePaths, Is.EqualTo(new[] { Generated }));
            Assert.That(facts.Hints, Is.EqualTo(new[] { "sharing-violation-text", "ioexception-type", "hresult-sharing-violation" }));
            Assert.That(json, Does.Not.Contain("PrivateMachineOwner").And.Not.Contain("cannot access").And.Not.Contain("Unknown build error"));
            Assert.That(diagnostic.Kind, Is.EqualTo("Failure"), "Facts cannot downgrade a workspace rejection.");
        });
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-group-workspace/diagnostic-allowlist-uses-admitted-generated-documents")]
    public void TheProductionAllowlistUsesTheAdmittedGeneratedFileWithoutAuthorizingItsWholeDirectory()
    {
        var hash = new string('a', 64);
        const string compilerName = "D:/ExternalCompilerOwner/InlineNumericEditor.g.cs";
        var document = new CoverageCandidateDocument(
            new(1, compilerName, Generated, hash, BuildSourceFileProvenance.BuildGenerated, "STranslate.Plugin.Tts.FishAudio/obj/coverage"),
            new(1, compilerName, Generated, new Guid("8829d00f-11b8-4213-878b-770e8597ac16"), hash, hash));
        var index = CoverageWorkspaceDiagnosticFactsIndex.Create(Root, new Dictionary<string, string> { [Tracked] = hash }, new[] { document });
        var message = "'" + Root + "/" + Generated + "' '" + Root + "/" + Tracked + "'";
        Assert.That(index.Describe(message).KnownRelativePaths, Is.EquivalentTo(new[] { Generated, Tracked }));
        var sibling = "'" + Root + "/" + Generated.Replace("InlineNumericEditor.g.cs", "UnlistedSibling.g.cs", StringComparison.Ordinal) + "'";
        AssertUnknown(index.Describe(sibling), sibling);
        AssertUnknown(index.Describe("'" + compilerName + "'"), "'" + compilerName + "'");
    }

    [TestCase("https://private.example/signed?secret=credential", TestName = "WorkspaceDiagnosticUnknownUrlIsHashOnly")]
    [TestCase("Cannot read 'C:\\ExternalPrivateOwner\\secret.txt'", TestName = "WorkspaceDiagnosticExternalPathIsHashOnly")]
    [TestCase("无法访问文件，资源暂时不可用", TestName = "WorkspaceDiagnosticUnrecognizedLanguageIsHashOnly")]
    public void UnknownMessagesDoNotExportTextOrAnUnlistedPath(string message) => AssertUnknown(Index().Describe(message), message);

    [TestCase("suffix", TestName = "WorkspaceDiagnosticKnownPathSuffixDoesNotMatch")]
    [TestCase("sibling", TestName = "WorkspaceDiagnosticSiblingRootDoesNotMatch")]
    [TestCase("parent", TestName = "WorkspaceDiagnosticParentSegmentDoesNotRemap")]
    [TestCase("unquoted", TestName = "WorkspaceDiagnosticUnquotedPathDoesNotMatch")]
    public void AFileMentionRequiresTheWholeQuotedAdmittedAbsolutePath(string fault)
    {
        var path = Root + "/" + Tracked;
        var message = fault switch
        {
            "suffix" => "'" + path + ".private'",
            "sibling" => "'" + Root + "-other/" + Tracked + "'",
            "parent" => "'" + Root + "/elsewhere/../" + Tracked + "'",
            _ => path,
        };
        AssertUnknown(Index().Describe(message), message);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-group-workspace/diagnostic-tokens-and-path-normalization")]
    public void OnlyCompleteTokensClassifyAndKnownPathsUseWindowsCaseAndSeparators()
    {
        const string unknown = "NotSystem.IO.IOExceptionSuffix 0x800700200 0x800700210";
        AssertUnknown(Index().Describe(unknown), unknown);
        var message = "System.IO.IOException (0x80070021): \"" + (Root + "/" + Tracked).ToUpperInvariant().Replace('/', '\\') + "\"";
        var facts = Index().Describe(message);
        Assert.Multiple(() =>
        {
            Assert.That(facts.MessageSha256, Is.EqualTo(Digest(message)));
            Assert.That(facts.Hints, Is.EqualTo(new[] { "ioexception-type", "hresult-lock-violation" }));
            Assert.That(facts.KnownRelativePaths, Is.EqualTo(new[] { Tracked }));
        });
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-group-workspace/diagnostic-oversize-keeps-full-unicode-digest")]
    public void OversizedLexicalFactsKeepTheCompleteMessageDigestAcrossASurrogateBoundary()
    {
        var message = new string('x', 1023) + "😀" + new string('y', CoverageWorkspaceDiagnosticFactsIndex.MaximumMessageCharacters) +
            " System.IO.IOException '" + Root + "/" + Generated + "'";
        AssertUnknown(Index().Describe(message), message);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-group-workspace/diagnostic-budgets-never-export-partial-facts")]
    public void AllowlistAndReportedPathOverflowDiscardEveryPartialHintAndPath()
    {
        var tooMany = Enumerable.Repeat(Tracked, CoverageWorkspaceDiagnosticFactsIndex.MaximumKnownPaths + 1).ToArray();
        var message = "System.IO.IOException '" + Root + "/" + Tracked + "'";
        AssertUnknown(CoverageWorkspaceDiagnosticFactsIndex.Create(Root, tooMany).Describe(message), message);
        var paths = Enumerable.Range(0, CoverageWorkspaceDiagnosticFactsIndex.MaximumReportedPaths + 1)
            .Select(index => "src/known-" + index + ".cs").ToArray();
        var crowded = "System.IO.IOException " + string.Join(" ", paths.Select(path => "'" + Root + "/" + path + "'"));
        AssertUnknown(CoverageWorkspaceDiagnosticFactsIndex.Create(Root, paths).Describe(crowded), crowded);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-group-workspace/diagnostic-total-budget-does-not-renew")]
    public void ManyMessagesShareOneLexicalBudgetWithoutRenewingIt()
    {
        var index = Index();
        var chunk = new string('x', CoverageWorkspaceDiagnosticFactsIndex.MaximumMessageCharacters);
        for (var offset = 0; offset < CoverageWorkspaceDiagnosticFactsIndex.MaximumScanCharacters; offset += chunk.Length)
            AssertUnknown(index.Describe(chunk), chunk);
        var message = "System.IO.IOException '" + Root + "/" + Generated + "'";
        AssertUnknown(index.Describe(message), message);
        Assert.That(Index().Describe(message).KnownRelativePaths, Is.EqualTo(new[] { Generated }));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-group-workspace/compilation-diagnostic-shape-is-preserved")]
    public void CompilationDiagnosticsDoNotAcquireAnEmptyFactsProperty()
    {
        var diagnostic = new CoverageWorkspaceDiagnostic("compilation", "Error", "CS0103", Tracked, 1, 1);
        Assert.That(JsonSerializer.Serialize(diagnostic), Does.Not.Contain("Facts").And.Not.Contain("facts"));
    }

    private static CoverageWorkspaceDiagnosticFactsIndex Index() =>
        CoverageWorkspaceDiagnosticFactsIndex.Create(Root, new[] { Tracked, Generated });

    private static string Digest(string message) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(message)));

    private static void AssertUnknown(CoverageWorkspaceDiagnosticFacts facts, string message)
    {
        Assert.Multiple(() =>
        {
            Assert.That(facts.MessageSha256, Is.EqualTo(Digest(message)), "Hash the whole original UTF-8 message.");
            Assert.That(facts.Hints, Is.EqualTo(new[] { "unknown" }));
            Assert.That(facts.KnownRelativePaths, Is.Empty);
        });
    }
}

public sealed partial class CoverageSourceCandidatesTests
{
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [TestCase("unknown", TestName = "coverage-group-workspace-cli/reject-unknown-option")]
    [TestCase("duplicate", TestName = "coverage-group-workspace-cli/reject-duplicate-option")]
    public void WorkspaceCommandRejectsUnknownAndDuplicateOptionsBeforeInputReads(string fault)
    {
        var files = WorkspaceCommandInputs();
        var key = fault == "unknown" ? "--unexpected" : "--workspace-expectation";
        var args = files.Arguments.Concat(new[] { key, files.Workspace }).ToArray();
        var result = InvokeGroupSourceCommand(args);
        Assert.That(OrderedProfileCoverageCommands.IsInvocation(args), Is.True);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Is.EqualTo("ordered-profile-coverage: invalid-input-or-output" + Environment.NewLine));
        Assert.That(File.Exists(files.Output), Is.False);
    }

    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [TestCase("unknown", TestName = "coverage-group-workspace-cli/reject-unknown-workspace-json")]
    [TestCase("duplicate", TestName = "coverage-group-workspace-cli/reject-duplicate-workspace-json")]
    public void WorkspaceExpectationUsesTheSameStrictJsonBoundary(string fault)
    {
        var files = WorkspaceCommandInputs();
        var original = File.ReadAllText(files.Workspace, Utf8);
        var field = fault == "unknown" ? "\"unexpected\":true," : "\"schemaVersion\":1,";
        File.WriteAllText(files.Workspace, "{" + field + original[1..], Utf8);
        var result = InvokeGroupSourceCommand(files.Arguments);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Is.EqualTo("ordered-profile-coverage: invalid-input-or-output" + Environment.NewLine));
        Assert.That(File.Exists(files.Output), Is.False);
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [Property("FishAudioCaseId", "coverage-group-workspace-cli/missing-snapshot-rejects-before-msbuild")]
    public void AWorkspaceRequestCannotBorrowAnUnvalidatedLiveCheckoutOrRegisterAnSdk()
    {
        var files = WorkspaceCommandInputs();
        var registered = MSBuildLocator.IsRegistered;
        var result = InvokeGroupSourceCommand(files.Arguments);
        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Error, Is.EqualTo("ordered-profile-coverage: ordered-profile-workspace-snapshot-read-failed" + Environment.NewLine));
        Assert.That(File.Exists(files.Output), Is.False);
        Assert.That(MSBuildLocator.IsRegistered, Is.EqualTo(registered));
        Assert.That(IsolatedCoverageSession.ReadValidated(_session, _expected).Identity, Is.EqualTo(_expected.Identity));
    }

    private (string[] Arguments, string Workspace, string Output) WorkspaceCommandInputs()
    {
        var source = GroupSourceCommandInputs();
        var workspace = Path.Combine(_root, "group-command-workspace-expectation.json");
        var request = new OrderedProfileCoverageWorkspaceRequest(1, Path.Combine(_root, "missing-context.json"),
            new string('a', 64), "10.0.403", "Release", "AnyCPU", "net10.0-windows", true,
            "tests/STranslate.Plugin.Tts.FishAudio.Tests/STranslate.Plugin.Tts.FishAudio.Tests.csproj",
            "STranslate.Plugin.Tts.FishAudio/STranslate.Plugin.Tts.FishAudio.csproj");
        WriteNew(workspace, JsonSerializer.SerializeToUtf8Bytes(request, GroupCommandJson));
        var args = new[] { "coverage-group-workspace-candidates", "--input", source.Inputs[0], "--expectation", source.Inputs[1],
            "--source-expectation", source.Inputs[2], "--workspace-expectation", workspace, "--output", source.Output };
        return (args, workspace, source.Output);
    }
}

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class OrderedProfileCoverageWorkspaceSnapshotTests
{
    private const string Prefix = "fa-workspace-snapshot-";
    private const string EntryProject = "tests/STranslate.Plugin.Tts.FishAudio.Tests/STranslate.Plugin.Tts.FishAudio.Tests.csproj";
    private const string ProductProject = "STranslate.Plugin.Tts.FishAudio/STranslate.Plugin.Tts.FishAudio.csproj";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private string _root = "";
    private string Snapshot => Path.Combine(_root, "snapshot");
    private PortablePdbSourceFile[] _tracked = [];
    private CoverageSourceIdentity _source = null!;
    private OrderedProfileCoverageWorkspaceRequest _request = null!;

    [SetUp]
    public void CreateAnActualByteBoundSnapshotContext()
    {
        _root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        Assert.That(File.Exists(_root) || Directory.Exists(_root), Is.False);
        Directory.CreateDirectory(Snapshot);
        RequireOwnedRoot();
        var inputs = new Dictionary<string, byte[]>
        {
            ["global.json"] = Utf8.GetBytes("{\"sdk\":{\"version\":\"10.0.403\",\"rollForward\":\"disable\",\"allowPrerelease\":false},\"test\":{\"runner\":\"VSTest\"}}"),
            [EntryProject] = Utf8.GetBytes("<Project Sdk=\"Microsoft.NET.Sdk\" />"),
            [ProductProject] = Utf8.GetBytes("<Project Sdk=\"Microsoft.NET.Sdk\" />"),
            ["src/λ.cs"] = new UnicodeEncoding(false, true, true).GetPreamble().Concat(Encoding.Unicode.GetBytes("// UTF-16 原始字节\r\n")).ToArray(),
        };
        foreach (var input in inputs)
        {
            var path = Path.Combine(Snapshot, input.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, input.Value);
        }
        _tracked = inputs.Select(item => new PortablePdbSourceFile(item.Key, BytesHash(item.Value))).ToArray();
        _source = new(new string('a', 40), new string('b', 40), IndependentFingerprint(inputs));
        _request = new(1, Path.Combine(_root, "context.json"), "", "10.0.403", "Release", "AnyCPU", "net10.0-windows", true,
            EntryProject, ProductProject);
        WriteContext(_tracked.Select(item => new { path = item.RelativePath, sha256 = item.Sha256, blob = new string('c', 40), mode = "100644" }));
    }

    [TearDown]
    public void RetainFailureOrDeleteOnlyOwnedFixture()
    {
        if (_root.Length == 0 || !Directory.Exists(_root)) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            var full = RequireOwnedRoot();
            RequireRegularTree(full);
            if (failed) { TestContext.Progress.WriteLine("workspace snapshot: failed owned fixture retained."); return; }
            Directory.Delete(full, recursive: true);
        }
        catch when (failed) { TestContext.Progress.WriteLine("workspace snapshot: cleanup failed; primary failure retained."); }
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-group-workspace/snapshot-raw-fingerprint-and-held-inputs")]
    public void TheCompleteInventoryUsesRawBytesAndRetainsItsWriteGuards()
    {
        var product = Path.Combine(Snapshot, ProductProject);
        var before = File.ReadAllBytes(product);
        using (var lease = Open())
        {
            Assert.That(lease.SourceFingerprint, Is.EqualTo(_source.Fingerprint));
            Assert.That(lease.ContextSha256, Is.EqualTo(_request.SnapshotContextSha256));
            Assert.That(lease.Root, Is.EqualTo(Snapshot));
            Assert.Throws<IOException>(() => File.WriteAllText(product, "changed", Utf8));
            Assert.That(File.ReadAllBytes(product), Is.EqualTo(before));
        }
        File.WriteAllText(product, "guard released", Utf8);
        Assert.That(File.ReadAllText(product, Utf8), Is.EqualTo("guard released"));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-group-workspace/changed-tracked-bytes")]
    public void ACurrentContextCannotAuthorizeChangedSourceBytes()
    {
        File.AppendAllText(Path.Combine(Snapshot, "src/λ.cs"), "changed", Utf8);
        var error = Assert.Throws<OrderedProfileCoverageWorkspaceJoinException>(() => { using var lease = Open(); });
        Assert.That(error!.Code, Is.EqualTo("ordered-profile-workspace-snapshot-file-mismatch"));
        Assert.That(error.Message, Does.Not.Contain(_root));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-group-workspace/full-inventory-not-subset")]
    public void ACompilerDocumentSubsetCannotReplaceTheCompleteSnapshotInventory()
    {
        WriteContext(_tracked.Take(3).Select(item => new { path = item.RelativePath, sha256 = item.Sha256, blob = new string('c', 40), mode = "100644" }));
        var error = Assert.Throws<OrderedProfileCoverageWorkspaceJoinException>(() => { using var lease = Open(); });
        Assert.That(error!.Code, Is.EqualTo("ordered-profile-workspace-snapshot-inventory-mismatch"));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-group-workspace/group-fingerprint-must-match")]
    public void ValidPerFileHashesCannotBorrowAnotherGroupsFingerprint()
    {
        var wrong = _source with { Fingerprint = new string('d', 64) };
        var error = Assert.Throws<OrderedProfileCoverageWorkspaceJoinException>(() =>
        { using var lease = OrderedProfileCoverageWorkspaceJoin.OpenSnapshot(wrong, _tracked, _request, CancellationToken.None); });
        Assert.That(error!.Code, Is.EqualTo("ordered-profile-workspace-snapshot-fingerprint-mismatch"));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-group-workspace/context-digest-required")]
    public void ContextBytesRemainBoundToTheIndependentExpectedDigest()
    {
        File.AppendAllText(_request.SnapshotContext, " ", Utf8);
        var error = Assert.Throws<OrderedProfileCoverageWorkspaceJoinException>(() => { using var lease = Open(); });
        Assert.That(error!.Code, Is.EqualTo("ordered-profile-workspace-snapshot-context-mismatch"));
    }

    [TestCase("../escape.cs", TestName = "WorkspaceSnapshotRejectsParentPath")]
    [TestCase("src/ALIAS~1.cs", TestName = "WorkspaceSnapshotRejectsDosAlias")]
    public void SnapshotPathsAreRejectedBeforeTheyCanBecomeFileLookupAuthority(string path)
    {
        var changed = _tracked.ToArray();
        changed[^1] = changed[^1] with { RelativePath = path };
        WriteContext(changed.Select(item => new { path = item.RelativePath, sha256 = item.Sha256, blob = new string('c', 40), mode = "100644" }));
        var error = Assert.Throws<OrderedProfileCoverageWorkspaceJoinException>(() =>
        { using var lease = OrderedProfileCoverageWorkspaceJoin.OpenSnapshot(_source, changed, _request, CancellationToken.None); });
        Assert.That(error!.Code, Is.EqualTo("ordered-profile-workspace-snapshot-inventory-mismatch"));
    }

    private OrderedProfileCoverageWorkspaceJoin.SnapshotLease Open() =>
        OrderedProfileCoverageWorkspaceJoin.OpenSnapshot(_source, _tracked, _request, CancellationToken.None);

    private void WriteContext<T>(IEnumerable<T> files)
    {
        File.WriteAllBytes(_request.SnapshotContext, JsonSerializer.SerializeToUtf8Bytes(new
        { schemaVersion = 1, root = Snapshot, tree = _source.Tree, sourceCommit = _source.Commit, files = files.ToArray() }, Json));
        _request = _request with { SnapshotContextSha256 = BytesHash(File.ReadAllBytes(_request.SnapshotContext)) };
    }

    private static string IndependentFingerprint(Dictionary<string, byte[]> files)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Utf8, leaveOpen: true);
        foreach (var input in files.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var path = Utf8.GetBytes(input.Key);
            writer.Write((long)path.Length); writer.Write(path); writer.Write((long)input.Value.Length); writer.Write(input.Value);
        }
        writer.Flush();
        return BytesHash(bytes.ToArray());
    }

    private static string BytesHash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private string RequireOwnedRoot()
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        if (Path.GetDirectoryName(full) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) ||
            !Path.GetFileName(full).StartsWith(Prefix, StringComparison.Ordinal)) throw new InvalidOperationException("workspace-fixture-root");
        for (var current = new DirectoryInfo(full); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("workspace-fixture-reparse");
        return full;
    }

    private static void RequireRegularTree(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("workspace-fixture-reparse");
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("workspace-fixture-reparse");
            if ((attributes & FileAttributes.Directory) != 0) RequireRegularTree(path);
        }
    }
}
