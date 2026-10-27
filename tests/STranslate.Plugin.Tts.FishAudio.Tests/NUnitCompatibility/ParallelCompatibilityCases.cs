using System.Diagnostics;
using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using NUnitAssert = NUnit.Framework.Assert;

namespace NUnitCompatibility;

[TestFixture]
[Category("CompatibilityParallel")]
[Parallelizable(ParallelScope.All)]
public sealed class ParallelCompatibilityCases
{
    private const string ApiKeyCase = "compat.parallel/api-key-format";
    private const string VoiceIdCase = "compat.parallel/voice-id-format";
    private readonly Observation?[] _observations = new Observation?[2];
    private Barrier? _rendezvous;

    [OneTimeSetUp]
    public void ConfigureOptionalParallelProof()
    {
        // Discovery constructs no fixture, Dispatcher or I/O. Normal serial/profiler
        // execution still checks each product contract without waiting for a peer.
        if (Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_PARALLEL_PROOF") == "1")
            _rendezvous = new Barrier(2);
    }

    [Test]
    [Property("FishAudioCaseId", ApiKeyCase)]
    public void ApiKeyValidationOwnsAnIndependentParallelCase()
    {
        RunCase(0, ApiKeyCase, () =>
        {
            NUnitAssert.That(SettingsValidation.IsValidApiKeyFormat("0123456789abcdef0123456789abcdef"), Is.True);
            NUnitAssert.That(SettingsValidation.IsValidApiKeyFormat("0123456789abcdef0123456789abcdef\n"), Is.False);
        });
    }

    [Test]
    [Property("FishAudioCaseId", VoiceIdCase)]
    public void VoiceIdValidationOwnsAnIndependentParallelCase()
    {
        RunCase(1, VoiceIdCase, () =>
        {
            NUnitAssert.That(SettingsValidation.IsValidVoiceIdFormat("0123456789abcdef0123456789abcdef"), Is.True);
            NUnitAssert.That(SettingsValidation.IsValidVoiceIdFormat("0123456789ABCDEF0123456789ABCDEF"), Is.False);
        });
    }

    [OneTimeTearDown]
    public void ObserveAndDisposeOptionalParallelProof()
    {
        try
        {
            if (_rendezvous is null) return;
            NUnitAssert.That(_observations[0], Is.Not.Null);
            NUnitAssert.That(_observations[1], Is.Not.Null);
            var first = _observations[0]!;
            var second = _observations[1]!;
            NUnitAssert.That(first.ProcessId, Is.EqualTo(second.ProcessId));
            NUnitAssert.That(first.ThreadId, Is.Not.EqualTo(second.ThreadId));
            NUnitAssert.That(first.Started, Is.LessThan(first.Completed));
            NUnitAssert.That(second.Started, Is.LessThan(second.Completed));
            NUnitAssert.That(first.Started, Is.LessThan(second.Completed));
            NUnitAssert.That(second.Started, Is.LessThan(first.Completed));
            TestContext.Progress.WriteLine("compatibility-parallel-proof overlapping=true distinct-threads=true");
        }
        finally
        {
            _rendezvous?.Dispose();
            _rendezvous = null;
        }
    }

    private void RunCase(int slot, string caseId, Action assertProductContract)
    {
        var started = Stopwatch.GetTimestamp();
        var processId = Environment.ProcessId;
        var threadId = Environment.CurrentManagedThreadId;
        try
        {
            assertProductContract();
            if (_rendezvous is not null)
            {
                // This is an explicit scheduler capability proof, excluded from profiler
                // collection. A mistakenly serial proof run fails after this bound.
                NUnitAssert.That(_rendezvous.SignalAndWait(TimeSpan.FromSeconds(15)), Is.True,
                    "compat-parallel-peer-did-not-arrive");
            }
        }
        finally
        {
            var completed = Stopwatch.GetTimestamp();
            _observations[slot] = new(processId, threadId, started, completed);
            TestContext.Progress.WriteLine($"compatibility-parallel case={caseId} proof={(_rendezvous is not null ? "enabled" : "disabled")} process={processId} thread={threadId} started={started} completed={completed} frequency={Stopwatch.Frequency}");
        }
    }

    private sealed record Observation(int ProcessId, int ThreadId, long Started, long Completed);
}
