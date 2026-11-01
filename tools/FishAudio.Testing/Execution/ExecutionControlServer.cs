using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;

namespace FishAudio.Testing.Execution;

internal sealed record ExecutionControlRecord(int SchemaVersion, string Role, string Nonce,
    string RunId, string? SourceTree, int Pid, string Kind, long Timestamp, long Frequency,
    string? AssemblySha256 = null, string? CaseId = null, string? FullName = null,
    int Ordinal = 0, long QueuedTimestamp = 0, string? Outcome = null,
    int? TargetPid = null, int? ExitCode = null);

/// <summary>
/// An independent pipe for a single owned process. Neither test Console capture
/// nor stdout forwarding participates in message delivery or authentication.
/// </summary>
internal sealed class ExecutionControlServer : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly ChannelWriter<ExecutionControlRecord> _events;
    private readonly WindowsJobOwner _job;
    private readonly FrameworkRunRequest _request;
    private readonly string _nonce;
    private readonly string _role;
    private readonly Func<int>? _expectedPid;

    internal ExecutionControlServer(string pipeName, string role, string nonce,
        WindowsJobOwner job, FrameworkRunRequest request,
        ChannelWriter<ExecutionControlRecord> events, Func<int>? expectedPid = null)
    {
        _pipe = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
        _events = events;
        _job = job;
        _request = request;
        _nonce = nonce;
        _role = role;
        _expectedPid = expectedPid;
    }

    internal async Task ReadAsync(CancellationToken cancellation)
    {
        await _pipe.WaitForConnectionAsync(cancellation);
        var actualPid = checked((int)WindowsJobOwner.GetPipeClientProcessId(_pipe.SafePipeHandle));
        using var process = Process.GetProcessById(actualPid);
        if ((_expectedPid is not null && actualPid != _expectedPid()) || !_job.IsMember(process.Handle))
            throw new InvalidDataException("framework-control-unowned-process");
        // Keep a fast test host alive until its real pipe PID and membership in
        // THIS Job have been checked. A PID copied into JSON is insufficient.
        await _pipe.WriteAsync(System.Text.Encoding.UTF8.GetBytes("framework-owner-ready:" + _nonce + "\n"), cancellation);
        await _pipe.FlushAsync(cancellation);
        var pending = new byte[65_535]; // The terminating newline is byte 65,536.
        var pendingCount = 0;
        var buffer = new byte[4096];
        while (true)
        {
            var count = await _pipe.ReadAsync(buffer.AsMemory(), cancellation);
            if (count == 0)
            {
                if (pendingCount != 0) throw new InvalidDataException("framework-control-truncated-record");
                return;
            }
            for (var index = 0; index < count; index++)
            {
                var character = buffer[index];
                if (character != (byte)'\n')
                {
                    if (pendingCount >= pending.Length) throw new InvalidDataException("framework-control-record-limit");
                    pending[pendingCount++] = character;
                    continue;
                }
                using var document = JsonDocument.Parse(pending.AsMemory(0, pendingCount));
                ValidateShape(document.RootElement);
                var value = document.RootElement.Deserialize<ExecutionControlRecord>(
                    FrameworkExecutionDriver.JsonOptions) ?? throw new InvalidDataException("framework-control-null-record");
                pendingCount = 0;
                if (value.SchemaVersion != 1 || value.Role != _role || value.Nonce != _nonce ||
                    value.RunId != _request.RunId || value.SourceTree != _request.SourceTree ||
                    value.Pid != actualPid || value.Frequency != Stopwatch.Frequency ||
                    value.Timestamp < 0 || value.Timestamp > Stopwatch.GetTimestamp() ||
                    _role == "nunit-host" && value.AssemblySha256 != _request.TestAssemblySha256)
                    throw new InvalidDataException("framework-control-identity-mismatch");
                // A blocked writer cannot freeze the independent deadline loop.
                // Overflow is an explicit failed envelope rather than lost data.
                if (!_events.TryWrite(value)) throw new InvalidDataException("framework-control-queue-overflow");
            }
        }
    }

    private void ValidateShape(JsonElement root)
    {
        string[] fields = _role == "nunit-host"
            ? ["schemaVersion", "role", "nonce", "runId", "sourceTree", "pid", "assemblySha256", "kind",
                "caseId", "fullName", "ordinal", "timestamp", "frequency", "queuedTimestamp", "outcome"]
            : ["schemaVersion", "role", "nonce", "runId", "sourceTree", "pid", "kind", "targetPid",
                "timestamp", "frequency", "exitCode"];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("framework-control-shape");
        foreach (var property in root.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw new InvalidDataException("framework-control-shape");
        if (seen.Count != fields.Length) throw new InvalidDataException("framework-control-shape");
        var kind = root.GetProperty("kind").GetString();
        if (_role == "nunit-host")
        {
            if (kind is not ("ready" or "case-start" or "dispatcher-starting" or "dispatcher-ready" or
                "queued" or "entered" or "completed" or "case-finished" or "cleanup-complete"))
                throw new InvalidDataException("framework-control-host-kind");
        }
        else if (kind is not ("target-started" or "target-exited") ||
            root.GetProperty("targetPid").GetInt32() <= 0 ||
            kind == "target-started" && root.GetProperty("exitCode").ValueKind != JsonValueKind.Null ||
            kind == "target-exited" && !root.GetProperty("exitCode").TryGetInt32(out _))
            throw new InvalidDataException("framework-control-launcher-kind");
    }

    public void Dispose() => _pipe.Dispose();
}
