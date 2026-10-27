using System.Text;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class NUnitEvidenceTests
{
    // Sanitized Adapter 6.3.1 D_/E_ and TRX shapes, with hand-declared identities.
    // These fixtures describe evidence admission, not product coverage.
    private const string Discovery = """
        <NUnitXml>
          <test-run id="0" name="example.dll" runstate="Runnable" testcasecount="2">
            <test-suite type="Assembly" id="0-10" name="example.dll" runstate="Runnable" testcasecount="2">
              <test-suite type="TestFixture" id="0-11" name="Cases" fullname="Example.Cases" runstate="Runnable" testcasecount="2">
                <test-case id="0-1" name="Sync" fullname="Example.Cases.Sync" methodname="Sync" classname="Example.Cases" runstate="Runnable">
                  <properties><property name="FishAudioCaseId" value="compat.sync/example" /></properties>
                </test-case>
                <test-suite type="ParameterizedMethod" id="0-12" name="Rows" fullname="Example.Cases.Rows" runstate="Runnable" testcasecount="1">
                  <test-case id="0-2" name="row &quot;quoted&quot; \ &lt;xml&gt;&amp; 中文" fullname="Example.Cases.row &quot;quoted&quot; \ &lt;xml&gt;&amp; 中文" methodname="Rows" classname="Example.Cases" runstate="Runnable">
                    <properties><property name="FishAudioCaseId" value="compat.source/row" /></properties>
                  </test-case>
                </test-suite>
              </test-suite>
            </test-suite>
          </test-run>
        </NUnitXml>
        """;

    private const string Terminal = """
        <test-run id="7" name="example.dll" runstate="Runnable" testcasecount="2" result="Passed" total="2" passed="2" failed="0" warnings="0" inconclusive="0" skipped="0" duration="0.75">
          <test-suite type="Assembly" id="7-10" name="example.dll" runstate="Runnable" testcasecount="2" result="Passed" total="2" passed="2" failed="0" warnings="0" inconclusive="0" skipped="0" duration="0.75">
            <test-suite type="TestFixture" id="7-11" name="Cases" fullname="Example.Cases" runstate="Runnable" testcasecount="2" result="Passed" total="2" passed="2" failed="0" warnings="0" inconclusive="0" skipped="0" duration="0.75">
              <test-case id="7-1" name="Sync" fullname="Example.Cases.Sync" methodname="Sync" classname="Example.Cases" runstate="Runnable" result="Passed" duration="0.25">
                <properties><property name="FishAudioCaseId" value="compat.sync/example" /></properties>
              </test-case>
              <test-suite type="ParameterizedMethod" id="7-12" name="Rows" fullname="Example.Cases.Rows" runstate="Runnable" testcasecount="1" result="Passed" total="1" passed="1" failed="0" warnings="0" inconclusive="0" skipped="0" duration="0.5">
                <test-case id="7-2" name="row &quot;quoted&quot; \ &lt;xml&gt;&amp; 中文" fullname="Example.Cases.row &quot;quoted&quot; \ &lt;xml&gt;&amp; 中文" methodname="Rows" classname="Example.Cases" runstate="Runnable" result="Passed" duration="0.5">
                  <properties><property name="FishAudioCaseId" value="compat.source/row" /></properties>
                </test-case>
              </test-suite>
            </test-suite>
          </test-suite>
        </test-run>
        """;

    private const string Trx = """
        <TestRun id="11111111-1111-1111-1111-111111111111" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult executionId="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" testId="11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa" testName="Sync" duration="00:00:00.2500000" outcome="Passed" />
            <UnitTestResult executionId="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb" testId="22222222-bbbb-bbbb-bbbb-bbbbbbbbbbbb" testName="row &quot;quoted&quot; \ &lt;xml&gt;&amp; 中文" duration="00:00:00.5000000" outcome="Passed" />
          </Results>
          <TestDefinitions>
            <UnitTest name="Sync" id="11111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa">
              <Execution id="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" />
              <TestMethod adapterTypeName="executor://nunit3testexecutor/" className="Example.Cases" name="Sync" />
            </UnitTest>
            <UnitTest name="row &quot;quoted&quot; \ &lt;xml&gt;&amp; 中文" id="22222222-bbbb-bbbb-bbbb-bbbbbbbbbbbb">
              <Execution id="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb" />
              <TestMethod adapterTypeName="executor://nunit3testexecutor/" className="Example.Cases" name="row &quot;quoted&quot; \ &lt;xml&gt;&amp; 中文" />
            </UnitTest>
          </TestDefinitions>
          <ResultSummary outcome="Completed">
            <Counters total="2" executed="2" passed="2" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
          </ResultSummary>
        </TestRun>
        """;

    private string _root = "";

    [SetUp]
    public void CreateOwnedDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), "fa-evidence-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void DeleteOwnedDirectory()
    {
        var full = Path.GetFullPath(_root);
        Assert.That(Path.GetDirectoryName(full), Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetTempPath())).IgnoreCase);
        Assert.That(Path.GetFileName(full), Does.StartWith("fa-evidence-tests-"));
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }

    [Test]
    public void DiscoveryPreservesCanonicalPropertiesAndExactEscapedRowIdentity()
    {
        var cases = NUnitEvidence.ReadCatalog(Write("D_example.dump", Discovery));
        Assert.That(cases, Is.EqualTo(ExpectedCatalog()));
    }

    [Test]
    public void MissingCanonicalPropertyUsesFullNameAndNamedParameterizedRow()
    {
        var text = Discovery.Replace("<property name=\"FishAudioCaseId\" value=\"compat.source/row\" />", "", StringComparison.Ordinal);
        var cases = NUnitEvidence.ReadCatalog(Write("D_example.dump", text));
        Assert.That(cases[1].CanonicalId, Is.EqualTo("Example.Cases.row \"quoted\" \\ <xml>& 中文"));
    }

    [Test]
    public void UnnamedParameterizedFallbackCannotAcquireAnIdentity()
    {
        var xml = XDocument.Parse(Discovery);
        var row = xml.Descendants("test-case").Last();
        row.Element("properties")!.Remove();
        row.SetAttributeValue("name", "Rows");
        row.SetAttributeValue("fullname", "Example.Cases.Rows");
        Reject(() => NUnitEvidence.ReadCatalog(Write("D_example.dump", xml.ToString())));
    }

    [TestCase("NotRunnable", TestName = "discovery/runstate-not-runnable")]
    [TestCase("Explicit", TestName = "discovery/runstate-explicit")]
    [TestCase("Ignored", TestName = "discovery/runstate-ignored")]
    public void NonRunnableDiscoveryCannotPassAsAvailableCoverage(string state)
        => Reject(() => NUnitEvidence.ReadCatalog(Write("D_example.dump", Discovery.Replace("runstate=\"Runnable\"", $"runstate=\"{state}\"", StringComparison.Ordinal))));

    [Test]
    public void EmptyDiscoveryIsRejected()
        => Reject(() => NUnitEvidence.ReadCatalog(Write("D_example.dump", "<NUnitXml><test-run runstate='Runnable' testcasecount='0' /></NUnitXml>")));

    [TestCase("FishAudioCaseId", "compat.sync/example", TestName = "discovery/duplicate-canonical-id")]
    [TestCase("fullname", "Example.Cases.Sync", TestName = "discovery/duplicate-full-name")]
    public void DuplicateCanonicalOrFullNameIsRejected(string attribute, string value)
    {
        var xml = XDocument.Parse(Discovery);
        var row = xml.Descendants("test-case").Last();
        if (attribute == "FishAudioCaseId") row.Descendants("property").Single().SetAttributeValue("value", value);
        else row.SetAttributeValue(attribute, value);
        Reject(() => NUnitEvidence.ReadCatalog(Write("D_example.dump", xml.ToString())));
    }

    [Test]
    public void ResultsJoinFullNamesAcrossChangedRuntimeIdsAndRetainActualDurations()
    {
        var result = Verify(Terminal, Trx);
        Assert.That(result.PassedCount, Is.EqualTo(2));
        Assert.That(result.TotalDuration, Is.EqualTo(TimeSpan.FromMilliseconds(750)));
        Assert.That(result.Cases, Is.EqualTo(new[]
        {
            new NUnitCaseResult("compat.sync/example", "Example.Cases.Sync", "passed", TimeSpan.FromMilliseconds(250))
                { PlatformDuration = TimeSpan.FromMilliseconds(250) },
            new NUnitCaseResult("compat.source/row", "Example.Cases.row \"quoted\" \\ <xml>& 中文", "passed", TimeSpan.FromMilliseconds(500))
                { PlatformDuration = TimeSpan.FromMilliseconds(500) },
        }));
        Assert.That(File.ReadAllText(Path.Combine(_root, "result.xml")), Is.EqualTo(Terminal));
    }

    [Test]
    public void TerminalWithoutClassMethodOrCanonicalPropertyJoinsTheDiscoveryFullName()
    {
        var xml = XDocument.Parse(Terminal);
        foreach (var leaf in xml.Descendants("test-case"))
        {
            leaf.Attribute("classname")!.Remove();
            leaf.Attribute("methodname")!.Remove();
            leaf.Element("properties")!.Remove();
        }
        Assert.That(Verify(xml.ToString(), Trx).PassedCount, Is.EqualTo(2));
    }

    [Test]
    public void ObservedAdapterDebugWrapperSelectsOnlyTheEscapedTerminalRun()
    {
        var escapedTerminal = new XElement("NUnitTestEvent", Terminal).ToString(SaveOptions.DisableFormatting);
        var text = "<NUnitXml><VSTest input/><TestPackage>example.dll</TestPackage></VSTest input/>"
            + XDocument.Parse(Discovery).Root!.Element("test-run")
            + escapedTerminal + "</NUnitXml>";
        Assert.That(Verify(text, Trx).PassedCount, Is.EqualTo(2));
    }

    [Test]
    public void WellFormedNUnitWrapperAcceptsItsSingleTerminalRun()
        => Assert.That(Verify("<NUnitXml>" + Terminal + "</NUnitXml>", Trx).PassedCount, Is.EqualTo(2));

    [Test]
    public void ObservedMtpSourceMethodNameRetainsTheNamedRowIdentity()
    {
        // The captured MTP report uses the source method in TestMethod.name,
        // retaining the escaped row display name in UnitTest and UnitTestResult.
        var trx = XDocument.Parse(Trx);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var rowMethod = trx.Descendants(ns + "TestMethod").Single(node =>
            (string?)node.Attribute("name") == "row \"quoted\" \\ <xml>& 中文");
        rowMethod.SetAttributeValue("name", "Rows");
        var result = Verify(Terminal, trx.ToString());
        Assert.That(result.Cases[1].CanonicalId, Is.EqualTo("compat.source/row"));
        Assert.That(result.Cases[1].FullName, Is.EqualTo("Example.Cases.row \"quoted\" \\ <xml>& 中文"));
        Assert.That(result.Cases[1].PlatformDuration, Is.EqualTo(TimeSpan.FromMilliseconds(500)));
    }

    [Test]
    public void UnrelatedSourceMethodCannotUseAValidDisplayRowIdentity()
    {
        var trx = XDocument.Parse(Trx);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        trx.Descendants(ns + "TestMethod").Last().SetAttributeValue("name", "OtherMethod");
        Reject(() => Verify(Terminal, trx.ToString()));
    }

    [Test]
    public void ExactSubsetEvidenceUsesTheSelectionWithoutClaimingUnselectedCoverage()
    {
        var nunit = XDocument.Parse(Terminal);
        nunit.Descendants("test-suite").Single(node => (string?)node.Attribute("type") == "ParameterizedMethod").Remove();
        foreach (var owner in nunit.Root!.DescendantsAndSelf().Where(node => node.Attribute("total") is not null))
        {
            owner.SetAttributeValue("total", "1");
            owner.SetAttributeValue("passed", "1");
        }
        var trx = XDocument.Parse(Trx);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        trx.Descendants(ns + "UnitTestResult").Last().Remove();
        trx.Descendants(ns + "UnitTest").Last().Remove();
        var counters = trx.Descendants(ns + "Counters").Single();
        foreach (var attribute in new[] { "total", "executed", "passed" }) counters.SetAttributeValue(attribute, "1");
        var result = Verify(nunit.ToString(), trx.ToString(), ["compat.sync/example"]);
        Assert.That(result.Cases, Is.EqualTo(new[]
        {
            new NUnitCaseResult("compat.sync/example", "Example.Cases.Sync", "passed", TimeSpan.FromMilliseconds(250))
                { PlatformDuration = TimeSpan.FromMilliseconds(250) },
        }));
    }

    [Test]
    public void UnknownRequestedCanonicalCaseCannotBecomeZeroCaseSuccess()
        => Reject(() => Verify(Terminal, Trx, ["missing/case"]));

    [Test]
    public void EmptyExpectedCaseSelectionIsRejected()
        => Reject(() => Verify(Terminal, Trx, []));

    [Test]
    public void DuplicateExpectedCaseSelectionIsRejected()
        => Reject(() => Verify(Terminal, Trx, ["compat.sync/example", "compat.sync/example"]));

    [Test]
    public void MissingExecutedCaseCannotBorrowItsDiscoveryEntry()
    {
        var xml = XDocument.Parse(Terminal);
        xml.Descendants("test-case").Last().Remove();
        Reject(() => Verify(xml.ToString(), Trx));
    }

    [Test]
    public void ExtraExecutedCaseCannotPassASelection()
        => Reject(() => Verify(Terminal, Trx, ["compat.sync/example"]));

    [Test]
    public void DuplicateExecutedCaseCannotSatisfyMissingCoverage()
    {
        var xml = XDocument.Parse(Terminal);
        xml.Root!.Add(new XElement(xml.Descendants("test-case").First()));
        Reject(() => Verify(xml.ToString(), Trx));
    }

    [Test]
    public void ChangedCanonicalPropertyCannotJoinByFullNameAlone()
        => Reject(() => Verify(Terminal.Replace("compat.sync/example", "other/case", StringComparison.Ordinal), Trx));

    [TestCase("Failed", TestName = "nunit-outcome/failed")]
    [TestCase("Skipped", TestName = "nunit-outcome/skipped")]
    [TestCase("Inconclusive", TestName = "nunit-outcome/inconclusive")]
    [TestCase("Warning", TestName = "nunit-outcome/warning")]
    public void NonPassingNUnitOutcomeIsRejected(string outcome)
    {
        var xml = XDocument.Parse(Terminal);
        xml.Descendants("test-case").First().SetAttributeValue("result", outcome);
        Reject(() => Verify(xml.ToString(), Trx));
    }

    [Test]
    public void FixtureTearDownFailureRejectsOtherwisePassingLeaves()
    {
        var xml = XDocument.Parse(Terminal);
        var fixture = xml.Descendants("test-suite").Single(node => (string?)node.Attribute("type") == "TestFixture");
        fixture.SetAttributeValue("result", "Failed");
        fixture.Add(new XElement("failure", new XElement("message", "private fixture failure")));
        Reject(() => Verify(xml.ToString(), Trx));
    }

    [Test]
    public void TruncatedTerminalXmlNeverFallsBackToEarlierDiscovery()
        => Reject(() => Verify("<NUnitXml>" + XDocument.Parse(Discovery).Root!.Element("test-run") + "<NUnitTestEvent>&lt;test-run result='Passed'", Trx));

    [Test]
    public void MultipleTerminalRunsAreRejected()
        => Reject(() => Verify("<NUnitXml>" + Terminal + Terminal + "</NUnitXml>", Trx));

    [Test]
    public void TrxCounterDisagreementRejectsPassingNUnitLeaves()
        => Reject(() => Verify(Terminal, Trx.Replace("executed=\"2\"", "executed=\"1\"", StringComparison.Ordinal)));

    [Test]
    public void MissingTrxCounterCannotAcquireAZeroDefault()
        => Reject(() => Verify(Terminal, Trx.Replace("error=\"0\"", "", StringComparison.Ordinal)));

    [Test]
    public void TruncatedTrxCannotEstablishSuccess()
        => Reject(() => Verify(Terminal, Trx[..^20]));

    [Test]
    public void IndependentNUnitAndPlatformDurationsRetainTheirOwnPrecision()
    {
        var result = Verify(Terminal, Trx.Replace("00:00:00.2500000", "00:00:00.2500001", StringComparison.Ordinal));
        Assert.That(result.Cases[0].Duration, Is.EqualTo(TimeSpan.FromTicks(2_500_000)));
        Assert.That(result.Cases[0].PlatformDuration, Is.EqualTo(TimeSpan.FromTicks(2_500_001)));
        Assert.That(result.PassedCount, Is.EqualTo(2));
    }

    [Test]
    public void ObservedMtpTimestampIntervalDoesNotReplaceReportedDuration()
    {
        // These duration and timestamp values are from the captured synchronous
        // MTP result. End minus start is 0.0641059 s; reported duration is 0.064149 s.
        var nunit = Terminal.Replace("duration=\"0.25\"", "duration=\"0.064149\"", StringComparison.Ordinal);
        var trx = XDocument.Parse(Trx);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var row = trx.Descendants(ns + "UnitTestResult").First();
        row.SetAttributeValue("duration", "00:00:00.0641490");
        row.SetAttributeValue("startTime", "2026-10-27T11:12:21.9389258+00:00");
        row.SetAttributeValue("endTime", "2026-10-27T11:12:22.0030317+00:00");
        var result = Verify(nunit, trx.ToString());
        Assert.That(result.Cases[0].Duration, Is.EqualTo(TimeSpan.FromTicks(641_490)));
        Assert.That(result.Cases[0].PlatformDuration, Is.EqualTo(TimeSpan.FromTicks(641_490)));
    }

    [TestCase("-00:00:00.2500000", TestName = "platform-duration/negative")]
    [TestCase("NaN", TestName = "platform-duration/nan")]
    [TestCase("Infinity", TestName = "platform-duration/infinity")]
    [TestCase("not-a-duration", TestName = "platform-duration/malformed")]
    public void InvalidPlatformDurationStillRejectsOtherwisePassingEvidence(string duration)
        => Reject(() => Verify(Terminal, Trx.Replace("00:00:00.2500000", duration, StringComparison.Ordinal)));

    [TestCase("-0.25", TestName = "nunit-duration/negative")]
    [TestCase("NaN", TestName = "nunit-duration/nan")]
    [TestCase("Infinity", TestName = "nunit-duration/infinity")]
    public void InvalidNUnitDurationStillRejectsOtherwisePassingEvidence(string duration)
        => Reject(() => Verify(Terminal.Replace("duration=\"0.25\"", $"duration=\"{duration}\"", StringComparison.Ordinal), Trx));

    [TestCase("Failed", TestName = "trx-outcome/failed")]
    [TestCase("NotExecuted", TestName = "trx-outcome/not-executed")]
    public void TrxOutcomeDisagreementIsRejected(string outcome)
        => Reject(() => Verify(Terminal, Trx.Replace("outcome=\"Passed\"", $"outcome=\"{outcome}\"", StringComparison.Ordinal)));

    [Test]
    public void TrxSuiteErrorRejectsOtherwisePassingResults()
        => Reject(() => Verify(Terminal, Trx.Replace("outcome=\"Completed\"", "outcome=\"Failed\"", StringComparison.Ordinal)));

    [Test]
    public void TrxForeignDefinitionCannotJoinByOnlyDisplayName()
        => Reject(() => Verify(Terminal, Trx.Replace("className=\"Example.Cases\"", "className=\"Other.Cases\"", StringComparison.Ordinal)));

    [Test]
    public void TrxDuplicateResultCannotSatisfyMissingCoverage()
    {
        var xml = XDocument.Parse(Trx);
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        xml.Root!.Element(ns + "Results")!.Add(new XElement(xml.Descendants(ns + "UnitTestResult").First()));
        Reject(() => Verify(Terminal, xml.ToString()));
    }

    [Test]
    public void ExternalEntitiesAreProhibitedAndErrorsContainNoInputOrPath()
    {
        const string xml = "<!DOCTYPE NUnitXml [<!ENTITY secret SYSTEM 'file:///private-secret-marker'>]><NUnitXml>&secret;</NUnitXml>";
        var path = Write("private-input-marker.xml", xml);
        var error = Assert.Throws<NUnitEvidenceException>(() => NUnitEvidence.ReadCatalog(path))!;
        Assert.That(error.Message, Does.Not.Contain("private-secret-marker").And.Not.Contain("private-input-marker").And.Not.Contain(_root));
        Assert.That(error.InnerException, Is.Null);
    }

    [Test]
    public void RelativeInputPathsAreRejectedWithoutExposingThem()
        => Reject(() => NUnitEvidence.ReadCatalog("private-relative-input.xml"));

    private NUnitEvidenceResult Verify(string result, string trx, IReadOnlyCollection<string>? selected = null)
        => NUnitEvidence.VerifyResults(ExpectedCatalog(), selected ?? ["compat.sync/example", "compat.source/row"],
            Write("result.xml", result), Write("result.trx", trx));

    private static IReadOnlyList<DiscoveredNUnitCase> ExpectedCatalog() =>
    [
        new("compat.sync/example", "Example.Cases.Sync", "0-1", "Example.Cases", "Sync"),
        new("compat.source/row", "Example.Cases.row \"quoted\" \\ <xml>& 中文", "0-2", "Example.Cases", "Rows"),
    ];

    private string Write(string name, string text)
    {
        var path = Path.Combine(_root, name);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(output, new UTF8Encoding(false));
        writer.Write(text);
        return path;
    }

    private static void Reject(Action action) => Assert.Throws<NUnitEvidenceException>(action);
}
