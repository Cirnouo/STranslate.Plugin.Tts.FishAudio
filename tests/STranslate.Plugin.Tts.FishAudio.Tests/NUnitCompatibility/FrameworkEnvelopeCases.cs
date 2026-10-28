using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using NUnitAssert = NUnit.Framework.Assert;

namespace NUnitCompatibility;

// These are ordinary NUnit leaves. Only an explicitly configured external
// acceptance process enables fault injection or writes private phase evidence.
[TestFixture]
[NonParallelizable]
public sealed class FrameworkEnvelopeCases
{
    private const string FixtureId = "compat.envelope";
    private const string SyncId = "compat.envelope/owned-sync";
    private const string AsyncId = "compat.envelope/owned-async";
    private const string SuffixId = "compat.envelope/remaining-suffix";
    private readonly string? _evidenceRoot;
    private readonly Fault _fault;
    private readonly int _pid;
    private int _sequence;
    private int _staThreadId;
    private string? _caseId;
    private bool _oneTimeSetupCompleted;
    private bool _setupCompleted;
    private Exception? _primaryFailure;
    private Exception? _fixtureFailure;

    public FrameworkEnvelopeCases()
    {
        // NUnit constructs the fixture during execution, not metadata discovery.
        // No static initializer, case source or attribute performs this I/O.
        (_evidenceRoot, _fault) = ReadAcceptanceContext();
        _pid = Environment.ProcessId;
        Record(FixtureId, "fixture", "constructor");
        if (_fault == Fault.ConstructorFailure)
            throw new InvalidOperationException("framework-envelope-constructor-failure");
    }

    [OneTimeSetUp]
    public void ObserveOneTimeSetup()
    {
        Record(FixtureId, "fixture", "one-time-setup-entered");
        if (_fault == Fault.OneTimeSetupFailure)
            throw new InvalidOperationException("framework-envelope-one-time-setup-failure");
        _oneTimeSetupCompleted = true;
        Record(FixtureId, "fixture", "one-time-setup-completed");
    }

    [SetUp]
    public async Task ObserveOwnedAsyncSetup()
    {
        var caseId = ReadActualCaseId();
        _caseId = caseId;
        _setupCompleted = false;
        _primaryFailure = null;
        try
        {
            NUnitAssert.That(_oneTimeSetupCompleted, Is.True);
            await StaTestHost.RunOnStaThreadAsync(async () =>
            {
                var dispatcher = AssertStaContext();
                Record(caseId, "case", "setup-entered");
                await Task.Yield();
                AssertSameStaContext(dispatcher);
                NUnitAssert.That(SettingsValidation.IsValidApiKeyFormat("0123456789abcdef0123456789abcdef"), Is.True);
                Record(caseId, "case", "setup-after-await");
            });
            _setupCompleted = true;
        }
        catch (Exception error)
        {
            RememberFailure(error);
            throw;
        }
    }

    [Test]
    [Order(10)]
    [Property("FishAudioCaseId", SyncId)]
    public void OwnedSynchronousBody()
    {
        try
        {
            AssertCurrentCase(SyncId);
            StaTestHost.RunOnStaThreadAsync(() =>
            {
                AssertStaContext();
                Record(SyncId, "case", "body-entered");
                NUnitAssert.That(SettingsValidation.IsValidApiKeyFormat("0123456789abcdef0123456789abcdef\n"), Is.False);
                Record(SyncId, "case", "body-completed");
            }).GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            RememberFailure(error);
            throw;
        }
    }

    [Test]
    [Order(20)]
    [Property("FishAudioCaseId", AsyncId)]
    public async Task OwnedAsynchronousBody()
    {
        try
        {
            AssertCurrentCase(AsyncId);
            await StaTestHost.RunOnStaThreadAsync(async () =>
            {
                var dispatcher = AssertStaContext();
                Record(AsyncId, "case", "body-entered");
                await Task.Yield();
                AssertSameStaContext(dispatcher);
                NUnitAssert.That(SettingsValidation.IsValidVoiceIdFormat("0123456789abcdef0123456789abcdef"), Is.True);
                Record(AsyncId, "case", "body-after-await");
                if (_fault == Fault.BodyHang)
                {
                    Record(AsyncId, "case", "body-hang-entered");
                    // Keep the existing STA action open. Only the existing
                    // guard and external process owner decide its deadline.
                    await Task.Delay(Timeout.InfiniteTimeSpan);
                }
                Record(AsyncId, "case", "body-completed");
            });
        }
        catch (Exception error)
        {
            RememberFailure(error);
            throw;
        }
    }

    [Test]
    [Order(30)]
    [Property("FishAudioCaseId", SuffixId)]
    public async Task RemainingSuffixHasItsOwnLifecycle()
    {
        try
        {
            AssertCurrentCase(SuffixId);
            await StaTestHost.RunOnStaThreadAsync(() =>
            {
                AssertStaContext();
                Record(SuffixId, "case", "body-entered");
                NUnitAssert.That(SettingsValidation.IsValidVoiceIdFormat("0123456789ABCDEF0123456789ABCDEF"), Is.False);
                Record(SuffixId, "case", "body-completed");
            });
        }
        catch (Exception error)
        {
            RememberFailure(error);
            throw;
        }
    }

    [TearDown]
    public async Task ObserveOwnedAsyncTeardown()
    {
        var caseId = _caseId ?? throw new InvalidOperationException("framework-envelope-case-missing");
        var preserveFailure = _primaryFailure is not null ||
            TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            await StaTestHost.RunOnStaThreadAsync(async () =>
            {
                var dispatcher = AssertStaContext();
                Record(caseId, "case", "teardown-entered");
                await Task.Yield();
                AssertSameStaContext(dispatcher);
                NUnitAssert.That(SettingsValidation.IsValidApiKeyFormat(""), Is.False);
                Record(caseId, "case", "teardown-after-await");
                if (_fault == Fault.TeardownHang && caseId == AsyncId)
                {
                    Record(caseId, "case", "teardown-hang-entered");
                    await Task.Delay(Timeout.InfiniteTimeSpan);
                }
                Record(caseId, "case", "teardown-completed");
            });
        }
        catch (Exception error)
        {
            if (!preserveFailure)
            {
                RememberFailure(error);
                throw;
            }
            TryRecordSecondary(caseId, "case", "teardown-secondary-failure");
        }
        finally
        {
            _setupCompleted = false;
            _caseId = null;
        }
    }

    [OneTimeTearDown]
    public void ObserveOneTimeCleanup()
    {
        var preserveFailure = _fixtureFailure is not null ||
            TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            Record(FixtureId, "fixture", "one-time-cleanup-entered");
            if (_fault == Fault.OneTimeCleanupFailure)
                throw new InvalidOperationException("framework-envelope-one-time-cleanup-failure");
            Record(FixtureId, "fixture", "one-time-cleanup-completed");
        }
        catch when (preserveFailure)
        {
            TryRecordSecondary(FixtureId, "fixture", "one-time-cleanup-secondary-failure");
        }
        finally { _oneTimeSetupCompleted = false; }
    }

    private void AssertCurrentCase(string expected)
    {
        NUnitAssert.That(_setupCompleted, Is.True);
        NUnitAssert.That(_caseId, Is.EqualTo(expected));
        NUnitAssert.That(ReadActualCaseId(), Is.EqualTo(expected));
        NUnitAssert.That(Environment.ProcessId, Is.EqualTo(_pid));
    }

    private Dispatcher AssertStaContext()
    {
        NUnitAssert.That(Thread.CurrentThread.GetApartmentState(), Is.EqualTo(ApartmentState.STA));
        NUnitAssert.That(SynchronizationContext.Current, Is.InstanceOf<DispatcherSynchronizationContext>());
        NUnitAssert.That(Dispatcher.CurrentDispatcher.CheckAccess(), Is.True);
        var threadId = Environment.CurrentManagedThreadId;
        if (_staThreadId == 0) _staThreadId = threadId;
        NUnitAssert.That(threadId, Is.EqualTo(_staThreadId));
        return Dispatcher.CurrentDispatcher;
    }

    private void AssertSameStaContext(Dispatcher dispatcher)
    {
        // WPF may copy its SynchronizationContext while retaining the owning
        // Dispatcher. Observe actual dispatcher/thread affinity across await.
        NUnitAssert.That(AssertStaContext(), Is.SameAs(dispatcher));
    }

    private void RememberFailure(Exception error)
    {
        _primaryFailure ??= error;
        _fixtureFailure ??= error;
    }

    private static string ReadActualCaseId()
    {
        var values = TestContext.CurrentContext.Test.Properties["FishAudioCaseId"].ToArray();
        if (values.Length != 1 || values[0] is not string id || id is not (SyncId or AsyncId or SuffixId))
            throw new InvalidOperationException("framework-envelope-case-identity-invalid");
        return id;
    }

    private static (string? Root, Fault Fault) ReadAcceptanceContext()
    {
        try
        {
            var fault = Environment.GetEnvironmentVariable("FISHAUDIO_FRAMEWORK_ACCEPTANCE_FAULT") switch
            {
                null or "" or "none" => Fault.None,
                "body-hang" => Fault.BodyHang,
                "teardown-hang" => Fault.TeardownHang,
                "constructor-failure" => Fault.ConstructorFailure,
                "one-time-setup-failure" => Fault.OneTimeSetupFailure,
                "one-time-cleanup-failure" => Fault.OneTimeCleanupFailure,
                _ => throw new InvalidDataException("framework-envelope-fault-invalid")
            };
            var root = Environment.GetEnvironmentVariable("FISHAUDIO_FRAMEWORK_ACCEPTANCE_EVIDENCE");
            if (root is null)
            {
                if (fault != Fault.None) throw new InvalidDataException("framework-envelope-fault-owner-missing");
                return (null, fault);
            }
            if (!Path.IsPathFullyQualified(root)) throw new InvalidDataException("framework-envelope-root-invalid");
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            RequireRegularDirectory(root);
            if (Directory.EnumerateFileSystemEntries(root).Any())
                throw new InvalidDataException("framework-envelope-root-not-fresh");
            if (fault != Fault.None)
            {
                var contextPath = Environment.GetEnvironmentVariable("FISHAUDIO_FRAMEWORK_CONTROL_CONTEXT");
                if (string.IsNullOrEmpty(contextPath) || !Path.IsPathFullyQualified(contextPath))
                    throw new InvalidDataException("framework-envelope-fault-owner-missing");
                var file = new FileInfo(contextPath);
                if (!file.Exists || (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    throw new InvalidDataException("framework-envelope-fault-owner-missing");
            }
            return (root, fault);
        }
        catch
        {
            // Context and filesystem exception messages may contain private paths.
            throw new InvalidDataException("framework-envelope-context-invalid");
        }
    }

    private void Record(string caseId, string scope, string phase)
    {
        if (_evidenceRoot is null) return;
        try
        {
            RequireRegularDirectory(_evidenceRoot);
            var sequence = Interlocked.Increment(ref _sequence);
            if (sequence > 100) throw new InvalidDataException("framework-envelope-phase-limit");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                caseId,
                scope,
                phase,
                pid = Environment.ProcessId,
                timestamp = Stopwatch.GetTimestamp(),
                frequency = Stopwatch.Frequency,
                sequence,
                threadId = Environment.CurrentManagedThreadId,
                apartment = Thread.CurrentThread.GetApartmentState().ToString()
            });
            var path = Path.Combine(_evidenceRoot, $"phase-{sequence:D6}-{phase}.json");
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch { throw new InvalidDataException("framework-envelope-evidence-failed"); }
    }

    private void TryRecordSecondary(string caseId, string scope, string phase)
    {
        try { Record(caseId, scope, phase); }
        catch { /* The original setup/body/cleanup failure remains primary. */ }
    }

    private static void RequireRegularDirectory(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("framework-envelope-root-missing-or-reparse");
    }

    private enum Fault { None, BodyHang, TeardownHang, ConstructorFailure, OneTimeSetupFailure, OneTimeCleanupFailure }
}
