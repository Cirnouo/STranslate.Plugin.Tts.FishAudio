using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FishAudio.Testing.Evidence;

public sealed record DotCoverNativeObservation(int LineNumber, string Kind, string Payload);
public sealed record DotCoverSnapshotStatistic(int LineNumber, string CoreId, int? ProcessId,
    long ValidModules, long TotalModules);
public sealed record DotCoverProfiledProcess(int ProcessId, string Executable, string? CommandLine,
    bool AuxiliaryPdbServer, bool Initialized, bool OnEndShutdownObserved, bool? ShutdownSucceeded,
    Guid? CoverageProfilerClsid, bool CoverageProfilerCreated, IReadOnlyList<DotCoverNativeObservation> Observations);
public sealed record CoverProcessDiagnosticReview(int SchemaVersion, string RuleId, string ToolVersion,
    IReadOnlyList<DiagnosticArtifact> Evidence, IReadOnlyList<DotCoverProfiledProcess> Processes,
    IReadOnlyList<DotCoverSnapshotStatistic> SnapshotStatistics)
{
    // A successful console-log terminator is not an observed OS process exit.
    public bool ConsoleRunnerReportedSuccess => true;
    public bool JobOwnershipClaim => false;
    public bool BaselineClaim => false;
}

public sealed class DotCoverProcessDiagnosticException : Exception
{
    internal DotCoverProcessDiagnosticException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Reads the observed CLI 2026.2.3.1 managed cover log and downloaded native
/// process-log blocks. Required PIDs come from independent supervision/markers.
/// Native/thread prefixes never establish PID, OS exit, pipe or Job completion.
/// </summary>
public static partial class DotCoverProcessDiagnostics
{
    public const string RuleId = "dotcover-2026.2.3.1-cover-process-observations-v1";
    private const int MaximumBytes = 64 * 1024 * 1024;
    private const int MaximumLine = 128 * 1024;
    private const int MaximumProcesses = 1024;

    public static CoverProcessDiagnosticReview ReadValidated(string absoluteCoverLog, string absoluteErrorLog,
        IReadOnlyCollection<int> requiredProfiledProcessIds, string dotCoverVersion)
    {
        try
        {
            Require(dotCoverVersion == DotCoverAggregate.SupportedToolVersion, "unsupported-tool-version");
            Require(requiredProfiledProcessIds is { Count: > 0 and <= MaximumProcesses } &&
                requiredProfiledProcessIds.All(pid => pid > 0) &&
                requiredProfiledProcessIds.Distinct().Count() == requiredProfiledProcessIds.Count, "invalid-required-pids");
            Require(Path.IsPathFullyQualified(absoluteCoverLog) && Path.IsPathFullyQualified(absoluteErrorLog) &&
                !Path.GetFullPath(absoluteCoverLog).Equals(Path.GetFullPath(absoluteErrorLog), StringComparison.OrdinalIgnoreCase), "invalid-log-paths");
            using var cover = new FileStream(absoluteCoverLog, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var errors = new FileStream(absoluteErrorLog, FileMode.Open, FileAccess.Read, FileShare.Read);
            var primary = Read(cover, absoluteCoverLog);
            var secondary = Read(errors, absoluteErrorLog);
            ValidateStderr(secondary.Text);
            var parsed = Parse(primary.Text, dotCoverVersion);
            foreach (var pid in requiredProfiledProcessIds)
            {
                var found = parsed.Processes.SingleOrDefault(process => process.ProcessId == pid);
                Require(found is not null && !found.AuxiliaryPdbServer, "required-profiled-pid-missing");
                Require(found.Initialized && found.OnEndShutdownObserved && found.ShutdownSucceeded is not false &&
                    found.CoverageProfilerClsid is not null && found.CoverageProfilerCreated &&
                    found.Observations.Single(item => item.Kind == "Initialize").LineNumber <
                        found.Observations.Single(item => item.Kind == "OnEndShutdown").LineNumber, "required-profiled-pid-incomplete");
            }
            return new(1, RuleId, dotCoverVersion, Array.AsReadOnly(new[] { primary.Artifact, secondary.Artifact }),
                Array.AsReadOnly(parsed.Processes.ToArray()), Array.AsReadOnly(parsed.Statistics.ToArray()));
        }
        catch (DotCoverProcessDiagnosticException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or DecoderFallbackException or OverflowException or FormatException)
        {
            throw Rejected("invalid-log-or-input");
        }
    }

    private static (List<DotCoverProfiledProcess> Processes, List<DotCoverSnapshotStatistic> Statistics) Parse(string text, string version)
    {
        var blocks = new Dictionary<string, NativeBlock>(StringComparer.Ordinal);
        var processes = new List<DotCoverProfiledProcess>();
        var connections = new Dictionary<string, int>(StringComparer.Ordinal);
        var statistics = new List<DotCoverSnapshotStatistic>();
        var continuation = new List<(int Line, string Text)>();
        var section = "";
        var banner = false;
        var completed = false;
        var lineNumber = 0;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            Require(line.Length <= MaximumLine, "line-size-limit");
            Require(!completed || string.IsNullOrWhiteSpace(line), "content-after-completion");
            var match = ManagedLine().Match(line);
            if (!match.Success)
            {
                Require(!LooksStructured(line), "unparsed-diagnostic");
                if (string.IsNullOrWhiteSpace(line)) continue;
                Require(section.Length > 0, "unparsed-content");
                Require(continuation.Count < 100_000, "continuation-size-limit");
                continuation.Add((lineNumber, line));
                continue;
            }
            FlushContinuation();
            var level = match.Groups["level"].Value;
            Require(level is "I" or "V" or "D", "warning-or-error");
            var component = match.Groups["component"].Value.Trim();
            var thread = match.Groups["thread"].Value.Trim();
            var message = match.Groups["message"].Value;
            RejectAlert(message);
            section = ContinuationSection(component, message);
            if (message.StartsWith("JetBrains dotCover Console Runner ", StringComparison.Ordinal))
            {
                Require(!banner && message.StartsWith("JetBrains dotCover Console Runner " + version + ". Build ", StringComparison.Ordinal), "tool-banner-mismatch");
                banner = true;
            }
            if (message.StartsWith("Console Runner exit code:", StringComparison.Ordinal))
            {
                Require(component == "Main" && level == "I" && message == "Console Runner exit code: 0" &&
                    blocks.Count == 0, "cover-not-completed");
                completed = true;
            }
            if (component != "ResultsHandler") continue;
            if (message == "Log file content:")
            {
                Require(blocks.Count < MaximumProcesses && !blocks.ContainsKey(thread), "overlapping-native-block");
                blocks.Add(thread, new NativeBlock());
                continue;
            }
            if (!blocks.TryGetValue(thread, out var block))
            {
                Require(!NativePid().IsMatch(message.Trim()) && !message.Contains("BProf::Initialize", StringComparison.Ordinal) &&
                    !message.Contains("CProf::OnEndShutdown", StringComparison.Ordinal), "native-observation-outside-block");
                continue;
            }
            if (Separator().IsMatch(message))
            {
                if (!block.Opened) { block.Opened = true; continue; }
                Require(block.Pid is not null && block.Executable is not null && processes.All(item => item.ProcessId != block.Pid),
                    "missing-or-duplicate-native-process");
                Require(processes.Count < MaximumProcesses, "process-count-limit");
                processes.Add(block.Finish());
                blocks.Remove(thread);
                continue;
            }
            Require(block.Opened, "native-block-opening-missing");
            block.Observe(message, lineNumber);
        }
        FlushContinuation();
        Require(banner && completed && blocks.Count == 0, "cover-not-completed");
        return (processes, statistics.Select(statistic => statistic with
        {
            ProcessId = connections.TryGetValue(statistic.CoreId, out var pid) ? pid : null,
        }).ToList());

        void FlushContinuation()
        {
            if (continuation.Count == 0) return;
            if (section == "connection")
            {
                var core = Field("CoreId:");
                var pid = Field("ProcessId:");
                var number = 0;
                Require(Guid.TryParseExact(core, "D", out _) && int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0 &&
                    !connections.ContainsKey(core), "invalid-core-connection");
                connections.Add(core, number);
            }
            if (section == "snapshot")
            {
                var core = Field("CoreId:");
                Require(Guid.TryParseExact(core, "D", out _), "invalid-snapshot-core");
                foreach (var item in continuation)
                {
                    var counts = ModuleCounts().Match(item.Text);
                    if (!counts.Success) continue;
                    var valid = long.Parse(counts.Groups["valid"].Value, CultureInfo.InvariantCulture);
                    var total = long.Parse(counts.Groups["total"].Value, CultureInfo.InvariantCulture);
                    Require(valid <= total, "invalid-snapshot-statistics");
                    statistics.Add(new(item.Line, core, null, valid, total));
                }
            }
            continuation.Clear();
            string Field(string name)
            {
                var fields = continuation.Where(item => item.Text.StartsWith(name, StringComparison.Ordinal)).ToArray();
                Require(fields.Length == 1, "missing-or-duplicate-section-field");
                return fields[0].Text[name.Length..].Trim();
            }
        }
    }

    private sealed class NativeBlock
    {
        internal bool Opened;
        internal int? Pid;
        internal string? Executable;
        private string? _commandLine;
        private bool _initialized;
        private bool _ended;
        private bool? _shutdown;
        private Guid? _coverage;
        private readonly HashSet<Guid> _created = [];
        private readonly List<DotCoverNativeObservation> _observations = [];

        internal void Observe(string message, int line)
        {
            var trimmed = message.Trim();
            var pid = NativePid().Match(trimmed);
            if (pid.Success)
            {
                var hex = 0U;
                var number = 0;
                Require(Pid is null && uint.TryParse(pid.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hex) &&
                    int.TryParse(pid.Groups["decimal"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0 && hex == number,
                    "native-pid-mismatch");
                Pid = number;
                Add("ProcessId", trimmed);
                return;
            }
            if (trimmed.StartsWith("ProcessId=", StringComparison.Ordinal)) throw Rejected("invalid-native-pid");
            if (trimmed.StartsWith("Executable=", StringComparison.Ordinal))
            {
                Require(Executable is null && trimmed.StartsWith("Executable='", StringComparison.Ordinal) && trimmed.EndsWith('\''), "invalid-native-executable");
                Executable = trimmed[12..^1];
                Require(Path.IsPathFullyQualified(Executable), "invalid-native-executable");
                Add("Executable", trimmed);
                return;
            }
            if (trimmed.StartsWith("CommandLine=", StringComparison.Ordinal))
            {
                Require(_commandLine is null && trimmed.StartsWith("CommandLine='", StringComparison.Ordinal) && trimmed.EndsWith('\''), "invalid-native-command-line");
                _commandLine = trimmed[13..^1];
                return;
            }
            var native = NativeEvent().Match(message);
            if (!native.Success) return; // Configuration payload remains in the raw hashed log.
            var payload = native.Groups["event"].Value;
            Require(!payload.StartsWith("WARNING:", StringComparison.Ordinal) && !payload.StartsWith("ERROR:", StringComparison.Ordinal) &&
                !payload.StartsWith("FATAL:", StringComparison.Ordinal), "native-warning-or-error");
            if (payload == "BProf::Initialize") { Require(!_initialized, "duplicate-initialization"); _initialized = true; Add("Initialize", payload); }
            if (payload == "CProf::OnEndShutdown") { Require(!_ended, "duplicate-shutdown-end"); _ended = true; Add("OnEndShutdown", payload); }
            if (payload.StartsWith("BProf::Shutdown res=", StringComparison.Ordinal))
            {
                Require(_shutdown is null && payload is "BProf::Shutdown res=ok" or "BProf::Shutdown res=failed", "invalid-shutdown-result");
                _shutdown = payload.EndsWith("=ok", StringComparison.Ordinal);
                Add("ShutdownResult", payload);
            }
            var coverage = CoverageSelection().Match(payload);
            if (coverage.Success) { Require(_coverage is null, "duplicate-coverage-selection"); _coverage = Guid.Parse(coverage.Groups["clsid"].Value); Add("CoverageProfilerSelected", payload); }
            var created = CreatedProfiler().Match(payload);
            if (created.Success) { _created.Add(Guid.Parse(created.Groups["clsid"].Value)); Add("ProfilerCreated", payload); }
            if (payload.StartsWith("Profiler res=failed pname=", StringComparison.Ordinal)) Add("ProfilerCandidateNotSelected", payload);
            void Add(string kind, string observedPayload)
            {
                Require(_observations.Count < 4096, "native-observation-limit");
                _observations.Add(new(line, kind, observedPayload));
            }
        }

        internal DotCoverProfiledProcess Finish() => new(Pid!.Value, Executable!, _commandLine,
            string.Equals(Path.GetFileName(Executable), "JetBrains.Profiler.PdbServer.exe", StringComparison.OrdinalIgnoreCase),
            _initialized, _ended, _shutdown, _coverage, _coverage is { } clsid && _created.Contains(clsid), _observations.AsReadOnly());
    }

    private static string ContinuationSection(string component, string message) => (component, message.TrimEnd()) switch
    {
        ("Main", "Command line:" or "Parsed arguments:" or "Multiple dotnet runtimes found:") => "information",
        ("LauncherController", "Configuration:") => "information",
        ("ProfilerLauncher", _) when message.StartsWith("Starting coverage analysis. ", StringComparison.Ordinal) && message.EndsWith("Configuration:", StringComparison.Ordinal) => "information",
        ("ProfilingViewer", "Incoming connection from profiler core.") => "connection",
        ("ProfilingViewer", "Core connection terminated.") => "information",
        ("ResultsHandler", "CoreConnected." or "LogDone." or "ProfilingFinished.") => "information",
        ("ResultsHandler", "SnapshotDone.") => "snapshot",
        ("SnapshotMerger", "Merging snapshots.") => "information",
        _ => "",
    };

    private static void ValidateStderr(string text)
    {
        var completed = false;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            Require(!completed || string.IsNullOrWhiteSpace(line), "content-after-completion");
            if (string.IsNullOrWhiteSpace(line)) continue;
            Require(line.Length <= MaximumLine, "line-size-limit");
            var match = ManagedLine().Match(line);
            Require(match.Success, "unparsed-stderr");
            Require(match.Groups["level"].Value is "I" or "V" or "D", "warning-or-error");
            var message = match.Groups["message"].Value;
            RejectAlert(message);
            var native = NativeEvent().Match(message);
            Require(!native.Success || (!native.Groups["event"].Value.StartsWith("WARNING:", StringComparison.Ordinal) &&
                !native.Groups["event"].Value.StartsWith("ERROR:", StringComparison.Ordinal) &&
                !native.Groups["event"].Value.StartsWith("FATAL:", StringComparison.Ordinal)), "native-warning-or-error");
            if (message.StartsWith("Console Runner exit code:", StringComparison.Ordinal))
            {
                Require(match.Groups["component"].Value.Trim() == "Main" && match.Groups["level"].Value == "I" &&
                    message == "Console Runner exit code: 0", "cover-not-completed");
                completed = true;
            }
        }
    }

    private static void RejectAlert(string message) => Require(message != "CoreAlert." &&
        !message.StartsWith("OnAlert ", StringComparison.Ordinal), "core-alert");
    private static bool LooksStructured(string line) => line.Contains("|W|", StringComparison.Ordinal) ||
        line.Contains("|E|", StringComparison.Ordinal) || line.Contains("|F|", StringComparison.Ordinal) || TimestampStart().IsMatch(line);

    private static (DiagnosticArtifact Artifact, string Text) Read(FileStream stream, string path)
    {
        Require(stream.Length <= MaximumBytes, "log-size-limit");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        Require(stream.ReadByte() == -1, "log-size-changed");
        var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
        return (new(path, Convert.ToHexStringLower(SHA256.HashData(bytes))), new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset));
    }

    private static DotCoverProcessDiagnosticException Rejected(string code) => new("cover-process-" + code);
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw Rejected(code);
    }

    [GeneratedRegex(@"^\d{2}:\d{2}:\d{2}\.\d{3} \|(?<level>[A-Z])\|(?<component>[^|]*)\|(?<thread>[^|]*)\| (?<message>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ManagedLine();
    [GeneratedRegex(@"^\d{2}:\d{2}:\d{2}\.\d{3} \|", RegexOptions.CultureInvariant)] private static partial Regex TimestampStart();
    [GeneratedRegex(@"^={20,}$", RegexOptions.CultureInvariant)] private static partial Regex Separator();
    [GeneratedRegex(@"^ProcessId=(?<hex>[0-9A-Fa-f]{8}) \((?<decimal>[0-9]+)\)$", RegexOptions.CultureInvariant)] private static partial Regex NativePid();
    [GeneratedRegex(@"^[0-9A-Fa-f]{8}(?: [0-9]+ [0-9A-Fa-f]{16})? (?<event>.*)$", RegexOptions.CultureInvariant)] private static partial Regex NativeEvent();
    [GeneratedRegex(@"^Profiler res=ok clsid=\{(?<clsid>[0-9A-Fa-f-]{36})\} pname='ReleaseCoverage'$", RegexOptions.CultureInvariant)] private static partial Regex CoverageSelection();
    [GeneratedRegex(@"^CreateComInstance clsid=\{(?<clsid>[0-9A-Fa-f-]{36})\} res=ok$", RegexOptions.CultureInvariant)] private static partial Regex CreatedProfiler();
    [GeneratedRegex(@"^Valid modules: (?<valid>[0-9]+) out of (?<total>[0-9]+)$", RegexOptions.CultureInvariant)] private static partial Regex ModuleCounts();
}
