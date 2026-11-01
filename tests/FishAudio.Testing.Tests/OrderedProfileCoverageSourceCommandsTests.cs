using System.Text.Json;
using System.Text.Json.Serialization;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

public sealed partial class CoverageSourceCandidatesTests
{
    private static readonly JsonSerializerOptions GroupCommandJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [TestCase("unknown", TestName = "coverage-group-source-cli/reject-unknown-option")]
    [TestCase("duplicate", TestName = "coverage-group-source-cli/reject-duplicate-option")]
    public void GroupSourceCommandRejectsUnknownOrRepeatedOptionsBeforeReadingEvidence(string fault)
    {
        var files = GroupSourceCommandInputs();
        var key = fault == "unknown" ? "--unexpected" : "--input";
        var args = files.Arguments.Concat(new[] { key, files.Inputs[0] }).ToArray();
        Assert.That(OrderedProfileCoverageCommands.IsInvocation(args), Is.True);
        var result = InvokeGroupSourceCommand(args);
        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Error, Is.EqualTo("ordered-profile-coverage: invalid-input-or-output" + Environment.NewLine));
        Assert.That(File.Exists(files.Output), Is.False);
    }

    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [TestCase("unknown", TestName = "coverage-group-source-cli/reject-unknown-json-field")]
    [TestCase("duplicate", TestName = "coverage-group-source-cli/reject-duplicate-json-field")]
    public void EveryActualInputRecordUsesTheExistingStrictJsonReader(string fault)
    {
        var files = GroupSourceCommandInputs();
        for (var index = 0; index < files.Inputs.Length; index++)
        {
            var original = File.ReadAllText(files.Inputs[index], Utf8);
            var field = fault == "unknown" ? "\"unexpected\":true," : "\"schemaVersion\":1,";
            var malformed = Path.Combine(_root, $"group-command-{fault}-{index}.json");
            WriteNew(malformed, Utf8.GetBytes("{" + field + original[1..]));
            var args = (string[])files.Arguments.Clone();
            args[2 + index * 2] = malformed;
            var result = InvokeGroupSourceCommand(args);
            Assert.That(result.ExitCode, Is.EqualTo(2),
                "Each of the three actual records must reject unknown/duplicate fields before typed group admission.");
            Assert.That(result.Error, Is.EqualTo("ordered-profile-coverage: invalid-input-or-output" + Environment.NewLine));
            Assert.That(File.Exists(files.Output), Is.False);
        }
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [Property("FishAudioCaseId", "coverage-group-source-cli/reject-isolated-raw-as-group")]
    public void IsolatedRawEvidenceCannotProduceACompleteGroupSourceResultThroughTheCli()
    {
        var before = JsonSerializer.Serialize(IsolatedCoverageSession.ReadValidated(_session, _expected));
        var files = GroupSourceCommandInputs();
        var result = InvokeGroupSourceCommand(files.Arguments);
        Assert.That(result.ExitCode, Is.EqualTo(1),
            "The bridge's typed rejection must remain distinct from malformed CLI/JSON rejection.");
        Assert.That(result.Error, Is.EqualTo("ordered-profile-coverage: ordered-profile-source-candidates-group-evidence-rejected" + Environment.NewLine));
        Assert.That(File.Exists(files.Output), Is.False);
        var after = JsonSerializer.Serialize(IsolatedCoverageSession.ReadValidated(_session, _expected));
        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [Property("FishAudioCaseId", "coverage-group-source-cli/shared-output-does-not-overwrite")]
    public void TheSharedGroupCommandWriterCreatesOnceAndCannotReplaceExistingOutput()
    {
        // A real admitted metadata plan reaches the unchanged writer shared by
        // plan/import/source-candidates. Do not invent a successful raw group.
        var discovery = Path.Combine(_root, "group-command-discovery.xml");
        var request = Path.Combine(_root, "group-command-plan.json");
        var output = Path.Combine(_root, "group-command-output.json");
        WriteNew(discovery, Utf8.GetBytes(GroupCommandDiscovery));
        WriteNew(request, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, discovery, profile = "native-input",
        }, GroupCommandJson));
        var args = new[] { "coverage-profile-plan", "--input", request, "--output", output };
        var first = InvokeGroupSourceCommand(args);
        Assert.That(first.ExitCode, Is.Zero);
        Assert.That(first.Error, Is.Empty);
        var original = File.ReadAllBytes(output);
        using (var result = JsonDocument.Parse(original))
        {
            Assert.That(result.RootElement.GetProperty("complete").GetBoolean(), Is.True);
            Assert.That(result.RootElement.GetProperty("baselineClaim").GetBoolean(), Is.False);
        }
        var repeated = InvokeGroupSourceCommand(args);
        Assert.That(repeated.ExitCode, Is.EqualTo(2));
        Assert.That(repeated.Error, Is.EqualTo("ordered-profile-coverage: invalid-input-or-output" + Environment.NewLine));
        Assert.That(File.ReadAllBytes(output), Is.EqualTo(original));
    }

    private static (int ExitCode, string Error) InvokeGroupSourceCommand(string[] args)
    {
        var previousError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            var exitCode = OrderedProfileCoverageCommands.Run(args);
            return (exitCode, error.ToString());
        }
        finally { Console.SetError(previousError); }
    }

    private (string[] Arguments, string[] Inputs, string Output) GroupSourceCommandInputs()
    {
        // Reuse the existing real PE/PDB/source and isolated raw fixture. Its
        // absent independent group envelope must never become group authority.
        var identity = new CoverageProfileGroupIdentity(_expected.Identity.RunId, _expected.Identity.SessionId,
            "native-input", _expected.Identity.Source, _expected.Identity.BuildId);
        var supervisor = new OrderedProfileCoverageSupervisorReceipt(1, identity, _expected.Supervisor.StartedUtc,
            _expected.Supervisor.SealedUtc, _expected.Supervisor.Cover, _expected.Supervisor.Export,
            _expected.Supervisor.Diagnostics, _session.Artifacts, null!);
        var input = new OrderedProfileCoverageGroupInput(1, identity, _session.ArtifactDirectory, _session.Artifacts);
        var expected = new OrderedProfileCoverageGroupAdmission(1, identity, SourceRoot, BuildRoot,
            _expected.Binaries, _expected.DiscoverySha256, _expected.DotCoverVersion, _expected.ToolFile,
            _expected.ToolSha256, supervisor);
        var paths = new[]
        {
            Path.Combine(_root, "group-command-input.json"),
            Path.Combine(_root, "group-command-expectation.json"),
            Path.Combine(_root, "group-command-source-expectation.json"),
        };
        WriteNew(paths[0], JsonSerializer.SerializeToUtf8Bytes(input, GroupCommandJson));
        WriteNew(paths[1], JsonSerializer.SerializeToUtf8Bytes(expected, GroupCommandJson));
        WriteNew(paths[2], JsonSerializer.SerializeToUtf8Bytes(_source, GroupCommandJson));
        var output = Path.Combine(_root, "group-command-source-result.json");
        return (new[] { "coverage-group-source-candidates", "--input", paths[0], "--expectation", paths[1],
            "--source-expectation", paths[2], "--output", output }, paths, output);
    }

    private const string GroupCommandDiscovery = """
        <NUnitXml><test-run id="run" name="fixture.dll" runstate="Runnable" testcasecount="1">
          <test-suite type="Assembly" id="assembly" name="fixture.dll" runstate="Runnable" testcasecount="1">
            <test-suite type="TestFixture" id="fixture" name="Cases" fullname="GroupCliFixture.Cases" runstate="Runnable" testcasecount="1">
              <properties><property name="FishAudioExecutionProfile" value="native-input"/><property name="FishAudioExclusiveResource" value="desktop"/></properties>
              <test-case id="one" name="One" fullname="GroupCliFixture.Cases.One" classname="GroupCliFixture.Cases" methodname="One" runstate="Runnable">
                <properties><property name="FishAudioCaseId" value="group-cli/one"/><property name="FishAudioProfileOrdinal" value="0"/></properties>
              </test-case>
            </test-suite>
          </test-suite>
        </test-run></NUnitXml>
        """;
}
