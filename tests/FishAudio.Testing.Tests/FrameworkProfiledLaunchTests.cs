using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FishAudio.Testing.Evidence;
using FishAudio.Testing.Execution;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[NonParallelizable]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class FrameworkProfiledLaunchTests
{
    private const string OwnedPrefix = "fa-profiled-launch-";
    private const string FixtureCaseId = "framework-profiled-fixture/probe";
    private const string FixtureFullName = "ProfiledFixture.Cases.Probe";
    private const string IdentityValidationMarker = "framework-profiled-launch-identity-validation-required";
    private const string OutputValidationMarker = "framework-profiled-output-preflight-required";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private string _parent = "";
    private string _root = "";
    private string _workingDirectory = "";
    private string? _sourceTree;
    private string _sourceFingerprint = "";
    private string OutputDirectory => Path.Combine(_root, "output");
    private string LauncherMarker => Path.Combine(_root, "launcher-executed.marker");

    [SetUp]
    public void CreateOwnedPreflightInputs()
    {
        var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
        _parent = RequireDirectory(string.IsNullOrEmpty(configured) ? Path.GetTempPath() : configured);
        _root = Path.Combine(_parent, OwnedPrefix + Guid.NewGuid().ToString("N"));
        Require(!File.Exists(_root) && !Directory.Exists(_root), "framework-profiled-fixture-already-exists");
        Directory.CreateDirectory(_root);
        RequireOwnedRoot();
        WriteNew("dotCover.dll", "opaque owned dotCover identity fixture; never executable");
        WriteNew("vstest.console.dll", "opaque owned SDK VSTest identity fixture; never executable");
        WriteNew("execution.runsettings", "<RunSettings><NUnit><NumberOfTestWorkers>0</NumberOfTestWorkers><StopOnError>false</StopOnError></NUnit></RunSettings>");
        WriteNew("discovery.dump", Discovery);
        WriteNew("launcher.ps1", Launcher);
        var catalog = NUnitEvidence.ReadCatalog(Path.Combine(_root, "discovery.dump"));
        Assert.That(catalog, Has.Count.EqualTo(1), "The owned literal discovery must be admitted before exercising profiled-launch validation.");
        Assert.That(catalog[0].CanonicalId, Is.EqualTo(FixtureCaseId));
        Assert.That(catalog[0].FullName, Is.EqualTo(FixtureFullName));
        (_workingDirectory, _sourceTree, _sourceFingerprint) = ReadSourceIdentity();
    }

    [TearDown]
    public void RetainFailedPreflightOrDeleteOnlyTheOwnedFixture()
    {
        if (_root.Length == 0 || !Directory.Exists(_root)) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            RequireOwnedRoot();
            if (failed)
            {
                SafeNote("framework-profiled-launch: failed owned fixture retained.");
                return;
            }
            RequireRegularTree(_root);
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception) when (failed)
        {
            SafeNote("framework-profiled-launch: cleanup validation failed; original failure retained.");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "infra.framework-profiled-launch/tool-sha-preflight")]
    public async Task WrongProfilerToolHashIsRejectedBeforeOutputOrLaunch()
        => await RejectProfileMismatchAsync(corruptTool: true, expectedMessage: "framework-profiled-tool-mismatch");

    [Test]
    [Property("FishAudioCaseId", "infra.framework-profiled-launch/vstest-sha-preflight")]
    public async Task WrongSdkVstestHashIsRejectedBeforeOutputOrLaunch()
        => await RejectProfileMismatchAsync(corruptTool: false, expectedMessage: "framework-profiled-vstest-mismatch");

    [Test]
    [Property("FishAudioCaseId", "infra.framework-profiled-launch/reserved-final-report")]
    public async Task ProfilerSnapshotCannotOccupyTheFinalSupervisorReport()
        => await RejectProfileMismatchAsync(corruptTool: null, expectedMessage: "framework-profiled-output-not-fresh",
            outputCollision: "reserved-final-report");

    [Test]
    [Property("FishAudioCaseId", "infra.framework-profiled-launch/reserved-invocation-parent")]
    public async Task ProfilerLogCannotTurnTheInvocationFileIntoAParentDirectory()
        => await RejectProfileMismatchAsync(corruptTool: null, expectedMessage: "framework-profiled-output-not-fresh",
            outputCollision: "reserved-invocation-parent");

    private async Task RejectProfileMismatchAsync(bool? corruptTool, string expectedMessage, string? outputCollision = null)
    {
        var assembly = RequireFile(typeof(FrameworkProfiledLaunchTests).Assembly.Location);
        var runId = Guid.NewGuid().ToString("N");
        var request = new FrameworkRunRequest(
            SchemaVersion: 1, RunId: runId, SourceTree: _sourceTree,
            WorkingDirectory: _workingDirectory,
            ShellExecutable: FindExecutable("pwsh.exe", "FISHAUDIO_PWSH_EXECUTABLE"),
            DotnetExecutable: FindExecutable("dotnet.exe", "FISHAUDIO_DOTNET_EXECUTABLE", "DOTNET_HOST_PATH"),
            LauncherScript: Path.Combine(_root, "launcher.ps1"),
            TestAssembly: assembly, TestAssemblySha256: Hash(assembly),
            RunSettings: Path.Combine(_root, "execution.runsettings"), DiscoveryPath: Path.Combine(_root, "discovery.dump"),
            ResultXmlPath: Path.Combine(OutputDirectory, "nunit.xml"), TrxPath: Path.Combine(OutputDirectory, "results.trx"),
            OutputDirectory: OutputDirectory, Cases: [new(FixtureCaseId, FixtureFullName)],
            MaximumConcurrency: 1, RequireOrder: false, Environment: null, SourceFingerprint: _sourceFingerprint);
        var tool = Path.Combine(_root, "dotCover.dll");
        var vstest = Path.Combine(_root, "vstest.console.dll");
        var toolHash = Hash(tool);
        var vstestHash = Hash(vstest);
        var snapshotPath = Path.Combine(OutputDirectory, "case.dcvr");
        var logPath = Path.Combine(OutputDirectory, "dotcover.log");
        if (outputCollision == "reserved-final-report") snapshotPath = Path.Combine(OutputDirectory, "supervisor.json");
        else if (outputCollision == "reserved-invocation-parent") logPath = Path.Combine(OutputDirectory, "invocation.json", "cover.log");
        else if (outputCollision is not null) throw new InvalidOperationException("framework-profiled-fixture-output-collision");
        var validationMarker = outputCollision is null ? IdentityValidationMarker : OutputValidationMarker;
        var payload = JsonSerializer.SerializeToNode(request, Json) as JsonObject
            ?? throw new InvalidOperationException("framework-profiled-fixture-request-json");
        // Hash cases corrupt only the selected pin; output cases keep both pins
        // accurate and vary only one reserved path. If preflight is missed, the
        // owned launcher writes its marker and exits 71 without reading or
        // executing the real target. No profiler or VSTest is launched by it.
        payload["profiledLaunch"] = JsonSerializer.SerializeToNode(new
        {
            sessionId = runId, toolAssembly = tool, toolSha256 = corruptTool is true ? CorruptHash(toolHash) : toolHash,
            vstestConsoleAssembly = vstest, vstestConsoleSha256 = corruptTool is false ? CorruptHash(vstestHash) : vstestHash,
            snapshotPath, logPath,
        }, Json);
        WriteNew("request.json", payload.ToJsonString(Json));

        Exception? failure = null;
        try { _ = await FrameworkExecutionDriver.RunAsync(Path.Combine(_root, "request.json")); }
        catch (Exception error) { failure = error; }
        Assert.That(Directory.Exists(OutputDirectory) || File.Exists(OutputDirectory), Is.False,
            outputCollision is null ? "A profiled identity mismatch must be rejected before the driver creates its output envelope." : OutputValidationMarker);
        Assert.That(File.Exists(LauncherMarker), Is.False,
            outputCollision is null ? "The launcher must not execute for an invalid pinned identity." : OutputValidationMarker);
        Assert.That(failure, Is.TypeOf<InvalidDataException>(), validationMarker);
        Assert.That(failure?.Message, Is.EqualTo(expectedMessage));
    }

    private (string Root, string? Tree, string Fingerprint) ReadSourceIdentity()
    {
        var contextPath = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        if (contextPath is null) return (_root, null, new string('c', 64));
        contextPath = RequireFile(contextPath);
        Require(new FileInfo(contextPath).Length is > 0 and <= 16 * 1024 * 1024, "framework-profiled-fixture-context-size");
        using var context = JsonDocument.Parse(File.ReadAllBytes(contextPath));
        var value = context.RootElement;
        var tree = value.GetProperty("tree").GetString();
        Require(value.GetProperty("schemaVersion").GetInt32() == 1 && tree is not null &&
            (IsHash(tree, 40) || IsHash(tree, 64)), "framework-profiled-fixture-context-identity");
        var root = RequireDirectory(value.GetProperty("root").GetString() ?? "");
        var files = value.GetProperty("files").EnumerateArray().ToArray();
        Require(files.Length is > 0 and <= 100_000, "framework-profiled-fixture-context-files");
        var unique = new HashSet<string>(StringComparer.Ordinal);
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[sizeof(long)];
        // Match tools/Test.ps1's sha256-source-path-and-bytes-ordinal-v1, using
        // the inherited context's actual listed bytes rather than a dummy hash.
        foreach (var entry in files.OrderBy(item => item.GetProperty("path").GetString() ?? "", StringComparer.Ordinal))
        {
            var relative = entry.GetProperty("path").GetString() ?? "";
            var parts = relative.Replace('\\', '/').Split('/');
            Require(relative.Length is > 0 and <= 32_768 && !Path.IsPathRooted(relative) && unique.Add(relative) &&
                parts.All(part => part.Length > 0 && part is not "." and not ".."), "framework-profiled-fixture-source-path");
            var path = Path.GetFullPath(Path.Combine(root, relative));
            Require(path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "framework-profiled-fixture-source-outside-root");
            RequireFile(path);
            var bytes = File.ReadAllBytes(path);
            var expected = entry.GetProperty("sha256").GetString();
            Require(IsHash(expected, 64) && Convert.ToHexStringLower(SHA256.HashData(bytes)) == expected,
                "framework-profiled-fixture-source-changed");
            var name = Utf8.GetBytes(relative.Replace('\\', '/'));
            BinaryPrimitives.WriteInt64LittleEndian(length, name.LongLength);
            fingerprint.AppendData(length); fingerprint.AppendData(name);
            BinaryPrimitives.WriteInt64LittleEndian(length, bytes.LongLength);
            fingerprint.AppendData(length); fingerprint.AppendData(bytes);
        }
        return (root, tree, Convert.ToHexStringLower(fingerprint.GetHashAndReset()));
    }

    private static string FindExecutable(string name, params string[] configuredKeys)
    {
        foreach (var key in configuredKeys)
            if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } selected) return RequireFile(selected);
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(directory.Trim('"'), name);
            if (Path.IsPathFullyQualified(path) && File.Exists(path)) return RequireFile(path);
        }
        throw new InvalidOperationException("framework-profiled-fixture-executable-missing");
    }

    private void RequireOwnedRoot()
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var name = Path.GetFileName(full);
        Require(Path.GetDirectoryName(full)?.Equals(_parent, StringComparison.OrdinalIgnoreCase) == true &&
            name.StartsWith(OwnedPrefix, StringComparison.Ordinal) && Guid.TryParseExact(name[OwnedPrefix.Length..], "N", out _),
            "framework-profiled-fixture-owner");
        RequireDirectory(full);
    }

    private static string RequireDirectory(string path)
    {
        Require(Path.IsPathFullyQualified(path), "framework-profiled-fixture-directory");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        for (DirectoryInfo? directory = new(full); directory is not null; directory = directory.Parent)
            Require(directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0,
                "framework-profiled-fixture-directory");
        return full;
    }

    private static string RequireFile(string path)
    {
        Require(Path.IsPathFullyQualified(path), "framework-profiled-fixture-file");
        var full = Path.GetFullPath(path);
        RequireDirectory(Path.GetDirectoryName(full) ?? "");
        Require(File.Exists(full) && (File.GetAttributes(full) &
            (FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.Device)) == 0, "framework-profiled-fixture-file");
        return full;
    }

    private static void RequireRegularTree(string root)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes = File.GetAttributes(path);
            Require((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0, "framework-profiled-fixture-cleanup-kind");
            if ((attributes & FileAttributes.Directory) != 0) RequireRegularTree(path);
        }
    }

    private void WriteNew(string name, string text)
    {
        using var stream = new FileStream(Path.Combine(_root, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        stream.Write(Utf8.GetBytes(text));
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string CorruptHash(string actual) => (actual[0] == '0' ? "1" : "0") + actual[1..];
    private static bool IsHash(string? value, int length) => value is not null && value.Length == length &&
        value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    private static void Require(bool condition, string code) { if (!condition) throw new InvalidOperationException(code); }
    private static void SafeNote(string text) { try { TestContext.Out.WriteLine(text); } catch { } }

    private const string Launcher = """
        param([Parameter(Mandatory)][string]$ArgumentsPath)
        [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'launcher-executed.marker'), 'unexpected-launch')
        exit 71
        """;

    // Owned literal NUnit adapter discovery, not an executed result or a claim
    // that this sentinel profiler fixture can run a real VSTest session.
    private const string Discovery = """
        <NUnitXml><test-run id="0" name="fixture.dll" runstate="Runnable" testcasecount="1">
          <test-suite type="Assembly" id="0-10" name="fixture.dll" runstate="Runnable" testcasecount="1">
            <test-suite type="TestFixture" id="0-11" name="Cases" fullname="ProfiledFixture.Cases" runstate="Runnable" testcasecount="1">
              <test-case id="0-1" name="Probe" fullname="ProfiledFixture.Cases.Probe" methodname="Probe" classname="ProfiledFixture.Cases" runstate="Runnable">
                <properties><property name="FishAudioCaseId" value="framework-profiled-fixture/probe" /></properties>
              </test-case>
            </test-suite>
          </test-suite>
        </test-run></NUnitXml>
        """;
}
