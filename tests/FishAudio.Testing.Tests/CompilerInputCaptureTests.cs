using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class CompilerInputCaptureTests
{
    private const string Product = "STranslate.Plugin.Tts.FishAudio/STranslate.Plugin.Tts.FishAudio.csproj";
    private const string Entry = "tests/STranslate.Plugin.Tts.FishAudio.Tests/STranslate.Plugin.Tts.FishAudio.Tests.csproj";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly DateTimeOffset Started = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private string _root = "";
    private string _snapshot = "";
    private CompilerInputCaptureInput _input = null!;
    private CompilerInputCaptureAdmission _expected = null!;
    private readonly string[] _arguments = ["/optimize+", "/define:TRACE;RELEASE;NET10_0", "/reference:C:\\PrivateOwner\\Unicode 文本\\a%3b.dll"];

    // Hand-declared protocol records, not a real Csc/MSBuild capture or proof
    // that the fixture's text SDK files executed. The DLL/PDB pair is actual.
    [SetUp]
    public void CreateOwnedProtocolFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "fa-compiler-capture-tests-" + Guid.NewGuid().ToString("N"));
        _snapshot = Path.Combine(_root, "snapshot");
        _input = new(Path.Combine(_root, "capture"), Path.Combine(_root, "build-owner"));
        Directory.CreateDirectory(_snapshot);
        Directory.CreateDirectory(_input.CaptureDirectory);
        Directory.CreateDirectory(_input.BuildEvidenceDirectory);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["global.json"] = Utf8.GetBytes("{\"sdk\":{\"version\":\"10.0.403\",\"rollForward\":\"disable\",\"allowPrerelease\":false}}"),
            [Product] = Utf8.GetBytes("<Project />"), [Entry] = Utf8.GetBytes("<Project />"),
            ["src/Input.cs"] = Utf8.GetBytes("class Input {}"),
        };
        foreach (var item in files) Write(Path.Combine(_snapshot, item.Key), item.Value);
        using var fingerprintBytes = new MemoryStream();
        using (var writer = new BinaryWriter(fingerprintBytes, Utf8, leaveOpen: true))
            foreach (var item in files.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var name = Utf8.GetBytes(item.Key); writer.Write((long)name.Length); writer.Write(name);
                writer.Write((long)item.Value.Length); writer.Write(item.Value);
            }
        var source = new CoverageSourceIdentity(new string('a', 40), new string('b', 40), Hash(fingerprintBytes.ToArray()));
        var context = Path.Combine(_root, "snapshot-context.json");
        Write(context, JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, root = _snapshot,
            tree = source.Tree, sourceCommit = source.Commit, files = files.Select(item => new
            { path = item.Key, sha256 = Hash(item.Value), blob = new string('c', 40), mode = "100644" }).ToArray() }));
        const string dll = "STranslate.Plugin.Tts.FishAudio/obj/coverage/Release/product.dll";
        const string pdb = "STranslate.Plugin.Tts.FishAudio/obj/coverage/Release/product.pdb";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_snapshot, dll))!);
        File.Copy(typeof(BinaryIdentity).Assembly.Location, Path.Combine(_snapshot, dll));
        File.Copy(Path.ChangeExtension(typeof(BinaryIdentity).Assembly.Location, ".pdb"), Path.Combine(_snapshot, pdb));
        var binary = BinaryIdentity.ReadValidated(Path.Combine(_snapshot, dll), Path.Combine(_snapshot, pdb));
        var sdk = Path.Combine(_root, "sdk", "10.0.403");
        foreach (var path in new[] { "Roslyn/Microsoft.CSharp.Core.targets", "Roslyn/Microsoft.Managed.Core.targets",
            "Roslyn/Microsoft.Build.Tasks.CodeAnalysis.dll", "Roslyn/bincore/csc.dll" })
            Write(Path.Combine(sdk, path), Utf8.GetBytes("fixture-toolchain-pin:" + path));
        var target = Path.Combine(_root, "CaptureCompilerInputs.targets");
        Write(target, Utf8.GetBytes("<Project /><!-- hand-declared capture fixture -->"));
        var toolchain = new CompilerInputCaptureToolchain(sdk, target, Hash(target),
            Hash(Path.Combine(sdk, "Roslyn/Microsoft.CSharp.Core.targets")), Hash(Path.Combine(sdk, "Roslyn/Microsoft.Managed.Core.targets")),
            Hash(Path.Combine(sdk, "Roslyn/Microsoft.Build.Tasks.CodeAnalysis.dll")), Hash(Path.Combine(sdk, "Roslyn/bincore/csc.dll")));
        var completion = new CoverageProcessCompletion(Started, Started.AddSeconds(10), true, 0, true, true, true, []);
        WriteOwner("argv.json", JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, executable = @"C:\Program Files\dotnet\dotnet.exe",
            arguments = BuildArguments(source, toolchain), workingDirectory = _snapshot, environment = new { } }));
        WriteOwner("process.json", JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, kind = "owned-process", complete = true,
            startedUtc = Started, finishedUtc = Started.AddSeconds(10), elapsedMilliseconds = 10000.0, exitCode = 0,
            outputDrained = true, allChildrenExited = true, ownership = "windows-job-before-target-go", cleanupFailed = false,
            phaseTimeoutMilliseconds = 0, rootProcessId = 1234 }));
        WriteOwner("stdout.log", []); WriteOwner("stderr.log", []);
        var owner = new CompilerInputCaptureOwnedBuild(SealOwner("argv.json"), SealOwner("process.json"),
            SealOwner("stdout.log"), SealOwner("stderr.log"), completion);
        _expected = new(1, new string('e', 32), source, new(1, new string('d', 64), source.Tree, source.Fingerprint, binary),
            new(1, context, Hash(context), "10.0.403", "Release", "AnyCPU", "net10.0-windows", true, Entry, Product),
            files.Select(item => new PortablePdbSourceFile(item.Key, Hash(item.Value))).ToArray(), [], dll, pdb, owner, toolchain);
        Capture("begin.txt", ["fresh-intermediate-output-slot-v1", "invocationId=" + _expected.InvocationId]);
        Capture("arguments.txt", _arguments);
        Capture("context.txt", ContextLines());
        Capture("corecompile-returned.txt", ["corecompile-returned-with-arguments-v1", "invocationId=" + _expected.InvocationId]);
        ResealCapture();
    }

    [TearDown]
    public void ReleaseOwnedProtocolFixture()
    {
        if (_root.Length == 0 || !Directory.Exists(_root)) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
            var name = Path.GetFileName(full);
            const string prefix = "fa-compiler-capture-tests-";
            if (!Path.IsPathFullyQualified(_root) || full != _root ||
                !string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
                    StringComparison.OrdinalIgnoreCase) || !name.StartsWith(prefix, StringComparison.Ordinal) ||
                name.Length != prefix.Length + 32 || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
                throw new InvalidOperationException("compiler-capture-fixture-owner-invalid");
            RequireDirectoryChain(full);
            if (failed)
            {
                var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
                if (string.IsNullOrWhiteSpace(configured))
                {
                    SafeNote("compiler-capture: failed fixture retained in its original owned temporary directory.");
                    return;
                }
                if (!Path.IsPathFullyQualified(configured) || !Directory.Exists(configured))
                    throw new InvalidOperationException("compiler-capture-retention-root-invalid");
                var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
                RequireDirectoryChain(parent);
                if (parent.Equals(full, StringComparison.OrdinalIgnoreCase) ||
                    parent.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("compiler-capture-retention-overlap");
                var destination = Path.Combine(parent, "compiler-input-capture-" + Guid.NewGuid().ToString("N"));
                RequireRegularTree(full);
                CopyEvidence(full, destination);
                SafeNote("compiler-capture: failed fixture copied as " + Path.GetFileName(destination) + ".");
            }
            RequireRegularTree(full);
            Directory.Delete(full, recursive: true);
        }
        catch (Exception) when (failed)
        {
            SafeNote("compiler-capture: retention or cleanup failed; primary failure and retained evidence preserved.");
        }

        static void RequireDirectoryChain(string path)
        {
            for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
                if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("compiler-capture-fixture-directory-invalid");
        }
        static void RequireRegularTree(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count != 0)
            {
                var directory = pending.Pop();
                RequireDirectoryChain(directory);
                foreach (var path in Directory.GetFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                        throw new InvalidOperationException("compiler-capture-fixture-entry-invalid");
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                }
            }
        }
        static void CopyEvidence(string source, string destination)
        {
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new InvalidOperationException("compiler-capture-retention-not-fresh");
            RequireDirectoryChain(source);
            Directory.CreateDirectory(destination);
            RequireDirectoryChain(destination);
            foreach (var path in Directory.GetFileSystemEntries(source))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("compiler-capture-fixture-entry-invalid");
                var target = Path.Combine(destination, Path.GetFileName(path));
                if ((attributes & FileAttributes.Directory) != 0) CopyEvidence(path, target);
                else File.Copy(path, target, overwrite: false);
            }
        }
        static void SafeNote(string message)
        {
            try { TestContext.Out.WriteLine(message); }
            catch { /* Diagnostic output cannot replace the original failure. */ }
        }
    }

    [Test]
    [Property("FishAudioCaseId", "compiler-capture/raw-items-and-owned-bindings-retained-without-compiler-claim")]
    public void RawTaskItemOrderAndUnicodeAreRetainedWithoutInventingACompilation()
    {
        var result = Read();
        Assert.Multiple(() =>
        {
            Assert.That(result.TaskArgumentItems, Is.EqualTo(_arguments));
            Assert.That(result.SuccessfulOwnedBuildAdmitted, Is.True);
            Assert.That(result.ArgumentTransportObserved || result.CompilerExecutionIndependentlyObserved ||
                result.ProducedProductBinaryProven || result.WholeProjectCompilationReconstructed || result.BuildInputClosureProven ||
                result.GeneratorInputClosureProven || result.CompleteBaseline, Is.False);
            Assert.That(JsonSerializer.Serialize(result), Does.Not.Contain("PrivateOwner").And.Not.Contain(_root));
        });
    }

    [TestCase("designTimeBuild", TestName = "compiler-capture/reject-design-time")]
    [TestCase("skipCompilerExecution", TestName = "compiler-capture/reject-skip-compiler")]
    [TestCase("useSharedCompilation", TestName = "compiler-capture/reject-shared-override")]
    public void SelfDeclaredNonRealModesCannotImpersonateAnActualBuild(string field)
    {
        var text = File.ReadAllText(Path.Combine(_input.CaptureDirectory, "context.txt"));
        File.WriteAllText(Path.Combine(_input.CaptureDirectory, "context.txt"), text.Replace(field + "=false", field + "=true", StringComparison.Ordinal), Utf8);
        ResealCapture(); Reject();
    }

    [TestCase("missing-marker", TestName = "compiler-capture/reject-partial-target-capture")]
    [TestCase("empty-arguments", TestName = "compiler-capture/reject-incremental-empty-output")]
    [TestCase("blank-item", TestName = "compiler-capture/reject-ambiguous-line-transport")]
    [TestCase("invalid-utf8", TestName = "compiler-capture/reject-invalid-argument-encoding")]
    [TestCase("wrong-count", TestName = "compiler-capture/reject-item-count-drift")]
    public void PartialOrAmbiguousTransportIsNeverGuessedIntoAReceipt(string fault)
    {
        if (fault == "missing-marker") File.Delete(Path.Combine(_input.CaptureDirectory, "corecompile-returned.txt"));
        if (fault == "empty-arguments") Capture("arguments.txt", []);
        if (fault == "blank-item") Capture("arguments.txt", ["/optimize+", "", "src/Input.cs"]);
        if (fault == "invalid-utf8") File.WriteAllBytes(Path.Combine(_input.CaptureDirectory, "arguments.txt"), [0xff, 0xfe, 0xff]);
        if (fault == "wrong-count") Capture("arguments.txt", ["/optimize+"]);
        if (fault != "missing-marker") ResealCapture();
        Reject();
    }

    [TestCase("raw-drift", TestName = "compiler-capture/reject-persisted-raw-drift")]
    [TestCase("owner-failure", TestName = "compiler-capture/reject-later-owned-build-failure")]
    [TestCase("undrained", TestName = "compiler-capture/reject-undrained-owner")]
    [TestCase("binary", TestName = "compiler-capture/reject-different-actual-output")]
    [TestCase("invocation", TestName = "compiler-capture/reject-other-invocation-slot")]
    [TestCase("snapshot", TestName = "compiler-capture/reject-changed-tracked-input")]
    [TestCase("sdk", TestName = "compiler-capture/reject-changed-explicit-toolchain-file")]
    [TestCase("raw-owner-exit", TestName = "compiler-capture/reject-raw-owned-process-nonzero")]
    [TestCase("skipped-command", TestName = "compiler-capture/reject-raw-skip-compiler-command")]
    [TestCase("oversized-item", TestName = "compiler-capture/reject-oversized-task-item")]
    public void CaptureFilesCannotOverrideIndependentBuildAndByteAuthority(string fault)
    {
        switch (fault)
        {
            case "raw-drift": File.AppendAllText(Path.Combine(_input.CaptureDirectory, "arguments.txt"), "/unsafe+\r\n", Utf8); break;
            case "owner-failure": _expected = _expected with { Owner = _expected.Owner with { Completion = _expected.Owner.Completion with { ExitCode = 1 } } }; break;
            case "undrained": _expected = _expected with { Owner = _expected.Owner with { Completion = _expected.Owner.Completion with { StderrDrained = false } } }; break;
            case "binary": _expected = _expected with { Build = _expected.Build with { ExpectedAssembly = _expected.Build.ExpectedAssembly with { DllSha256 = new string('f', 64) } } }; break;
            case "invocation": _expected = _expected with { InvocationId = new string('f', 32) }; break;
            case "snapshot": File.AppendAllText(Path.Combine(_snapshot, "src/Input.cs"), " changed", Utf8); break;
            case "sdk": File.AppendAllText(Path.Combine(_expected.Toolchain.SdkDirectory, "Roslyn/bincore/csc.dll"), " changed", Utf8); break;
            case "raw-owner-exit":
                var processPath = Path.Combine(_input.BuildEvidenceDirectory, "process.json");
                File.WriteAllText(processPath, File.ReadAllText(processPath).Replace("\"exitCode\":0", "\"exitCode\":1", StringComparison.Ordinal), Utf8);
                _expected = _expected with { Owner = _expected.Owner with { Process = SealOwner("process.json") } }; break;
            case "skipped-command":
                var invocationPath = Path.Combine(_input.BuildEvidenceDirectory, "argv.json");
                File.WriteAllText(invocationPath, File.ReadAllText(invocationPath).Replace("-p:UseSharedCompilation=false", "-p:SkipCompilerExecution=true", StringComparison.Ordinal), Utf8);
                _expected = _expected with { Owner = _expected.Owner with { Invocation = SealOwner("argv.json") } }; break;
            case "oversized-item": Capture("arguments.txt", [new string('x', 32769), "/optimize+", "src/Input.cs"]); ResealCapture(); break;
        }
        Reject();
    }

    [Test]
    [Property("FishAudioCaseId", "compiler-capture/bom-preserved-raw-digest-distinct")]
    public void AnOptionalUtf8BomChangesTheBoundRawBytesButNotTheCapturedItemValues()
    {
        var bytes = File.ReadAllBytes(Path.Combine(_input.CaptureDirectory, "arguments.txt"));
        File.WriteAllBytes(Path.Combine(_input.CaptureDirectory, "arguments.txt"), [0xef, 0xbb, 0xbf, .. bytes]);
        ResealCapture(); Assert.That(Read().TaskArgumentItems, Is.EqualTo(_arguments));
    }

    private CompilerInputCaptureEvidence Read() => CompilerInputCapture.ReadValidated(_input, _expected);
    private void Reject() => Assert.Throws<CompilerInputCaptureException>(() => Read());
    private string[] ContextLines() =>
    [
        "format=fish-audio-csc-task-items-v1", "invocationId=" + _expected.InvocationId,
        "sourceTree=" + _expected.Source.Tree, "sourceFingerprint=" + _expected.Source.Fingerprint,
        "project=" + Path.GetFullPath(Path.Combine(_snapshot, Product)), "projectDirectory=" + Path.GetDirectoryName(Path.GetFullPath(Path.Combine(_snapshot, Product))),
        "configuration=Release", "targetFramework=net10.0-windows", "platform=AnyCPU", "sdkVersion=10.0.403",
        "optimize=true", "debugType=portable", "designTimeBuild=false", "skipCompilerExecution=false", "useSharedCompilation=false",
        "compilerToolPath=", "compilerToolExe=", "roslynTargetsPath=" + Path.Combine(_expected.Toolchain.SdkDirectory, "Roslyn"),
        "intermediateDll=" + Path.GetFullPath(Path.Combine(_snapshot, _expected.DllRelativePath)),
        "intermediatePdb=" + Path.GetFullPath(Path.Combine(_snapshot, _expected.PdbRelativePath)), "argumentItemCount=3", "buildingProject=true",
    ];

    private string[] BuildArguments(CoverageSourceIdentity source, CompilerInputCaptureToolchain toolchain) =>
    [
        "build", Path.GetFullPath(Path.Combine(_snapshot, Entry)), "--configuration", "Release", "--disable-build-servers",
        "-p:RestoreLockedMode=true", "-p:FishAudioCoverage=true", "-p:UseSharedCompilation=false", "-nodeReuse:false",
        "-p:CustomAfterMicrosoftCommonTargets=" + toolchain.CaptureTargetsPath, "-p:FishAudioCompilerReceipt=true",
        "-p:FishAudioCompilerReceiptProjectFullPath=" + Path.GetFullPath(Path.Combine(_snapshot, Product)),
        "-p:FishAudioCompilerReceiptOutputDirectory=" + _input.CaptureDirectory, "-p:FishAudioCompilerReceiptInvocationId=" + new string('e', 32),
        "-p:FishAudioCompilerReceiptSourceTree=" + source.Tree, "-p:FishAudioCompilerReceiptSourceFingerprint=" + source.Fingerprint,
    ];

    private void ResealCapture()
    {
        var names = new[] { "begin.txt", "context.txt", "arguments.txt", "corecompile-returned.txt" };
        foreach (var name in names) File.SetLastWriteTimeUtc(Path.Combine(_input.CaptureDirectory, name), Started.AddSeconds(5).UtcDateTime);
        _expected = _expected with { CaptureFiles = names.Select(name => Seal(_input.CaptureDirectory, name)).ToArray() };
    }
    private static CoverageOwnedProcessEvidence Seal(string root, string name) => new(name, Hash(Path.Combine(root, name)),
        new FileInfo(Path.Combine(root, name)).Length, new(File.GetLastWriteTimeUtc(Path.Combine(root, name))));
    private CoverageOwnedProcessEvidence SealOwner(string name) => Seal(_input.BuildEvidenceDirectory, name);
    private void Capture(string name, string[] lines)
    {
        File.WriteAllText(Path.Combine(_input.CaptureDirectory, name), string.Join("\r\n", lines) + (lines.Length == 0 ? "" : "\r\n"), Utf8);
        File.SetLastWriteTimeUtc(Path.Combine(_input.CaptureDirectory, name), Started.AddSeconds(5).UtcDateTime);
    }
    private void WriteOwner(string name, byte[] bytes) => Write(Path.Combine(_input.BuildEvidenceDirectory, name), bytes);
    private static void Write(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
    private static string Hash(string path) => Hash(File.ReadAllBytes(path));
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
