using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FishAudio.Testing.Evidence;

public sealed record DiscoveredNUnitCase(string CanonicalId, string FullName, string PlatformId,
    string ClassName, string MethodName);
public sealed record NUnitCaseMetadata(string CanonicalId,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Properties);

public sealed record NUnitCaseResult(string CanonicalId, string FullName, string Outcome, TimeSpan Duration)
{
    // NUnit and TRX report their own durations. Neither is reconstructed from
    // rounded start/end timestamps or required to equal the other observation.
    public TimeSpan PlatformDuration { get; init; }
}

public sealed record NUnitEvidenceResult(IReadOnlyList<NUnitCaseResult> Cases)
{
    public int PassedCount => Cases.Count(item => item.Outcome == "passed");
    public TimeSpan TotalDuration => TimeSpan.FromTicks(Cases.Sum(item => item.Duration.Ticks));
}

// Only fixed admission codes leave the parser. Raw XML, filesystem errors and
// provider/framework diagnostic text remain in the caller's private artifacts.
public sealed class NUnitEvidenceException : Exception
{
    internal NUnitEvidenceException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Reads NUnit XML and the observed NUnit Adapter 6.3.1 discovery/debug dump
/// envelopes, including the separately observed VSTest display-name and MTP
/// source-method TRX definitions. Admission describes those raw artifacts, not
/// product coverage or an established collector completeness contract.
/// </summary>
public static class NUnitEvidence
{
    private const long MaximumBytes = 64 * 1024 * 1024;
    private const int MaximumCases = 100_000;
    private const int MaximumElements = 500_000;
    private const int MaximumValueLength = 16_384;
    private static readonly XNamespace TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private static readonly string[] TrxZeroCounters =
    [
        "failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
        "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending",
    ];

    public static IReadOnlyList<DiscoveredNUnitCase> ReadCatalog(string absoluteDumpPath)
    {
        var document = ReadXml(absoluteDumpPath, adapterDump: true);
        var root = document.Root!;
        var run = root.Name == "test-run" ? root : Only(root.Elements("test-run"), "invalid-discovery-root");
        Require(root.Name == "test-run" || root.Name == "NUnitXml", "invalid-discovery-root");
        Require(run.Attribute("result") is null, "discovery-has-results");
        foreach (var owner in run.DescendantsAndSelf().Where(element => element.Name == "test-run" ||
                     element.Name == "test-suite" || element.Name == "test-case"))
            Require(Required(owner, "runstate") == "Runnable", "non-runnable-discovery");

        var leaves = run.Descendants("test-case").ToArray();
        Require(leaves.Length is > 0 and <= MaximumCases, "empty-or-oversized-discovery");
        Require(Count(run, "testcasecount") == leaves.Length, "discovery-count-mismatch");
        var catalog = new List<DiscoveredNUnitCase>(leaves.Length);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var fullNames = new HashSet<string>(StringComparer.Ordinal);
        var platformIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var leaf in leaves)
        {
            Require(!leaf.Elements("test-case").Any(), "nested-discovery-case");
            var name = Required(leaf, "name");
            var fullName = Required(leaf, "fullname");
            var className = Required(leaf, "classname");
            var methodName = Required(leaf, "methodname");
            var platformId = Required(leaf, "id");
            var explicitId = CanonicalProperty(leaf);
            if (explicitId is null)
            {
                Require(fullName == className + "." + name, "unstable-fallback-name");
                if (leaf.Ancestors("test-suite").Any(owner => (string?)owner.Attribute("type") == "ParameterizedMethod"))
                    Require(name != methodName && fullName != className + "." + methodName, "unnamed-parameterized-case");
            }
            var canonicalId = explicitId ?? fullName;
            Require(ids.Add(canonicalId) && fullNames.Add(fullName) && platformIds.Add(platformId), "duplicate-discovery-case");
            catalog.Add(new(canonicalId, fullName, platformId, className, methodName));
        }
        return catalog.AsReadOnly();
    }

    public static NUnitCaseMetadata ReadCaseMetadata(string absoluteDumpPath, string canonicalId)
    {
        // Keep one immutable dump open across catalog admission and metadata
        // extraction; this does not load the test assembly or construct fixtures.
        Require(Path.IsPathFullyQualified(absoluteDumpPath), "path-must-be-absolute");
        using var guard = new FileStream(absoluteDumpPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var selected = ReadCatalog(absoluteDumpPath).Where(item => item.CanonicalId == canonicalId).ToArray();
        Require(selected.Length == 1, "unknown-selected-case");
        var document = ReadXml(absoluteDumpPath, adapterDump: true);
        var leaf = document.Descendants("test-case").Single(item => (string?)item.Attribute("id") == selected[0].PlatformId);
        Require(leaf.Elements("properties").Count() <= 1, "duplicate-properties-container");
        var values = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var property in leaf.Elements("properties").Elements("property"))
        {
            var name = Required(property, "name");
            var value = Required(property, "value");
            if (!values.TryGetValue(name, out var entries)) values.Add(name, entries = []);
            entries.Add(value);
        }
        return new(canonicalId, values.ToDictionary(item => item.Key,
            item => (IReadOnlyList<string>)item.Value.AsReadOnly(), StringComparer.Ordinal));
    }

    public static NUnitEvidenceResult VerifyResults(IReadOnlyList<DiscoveredNUnitCase> catalog,
        IReadOnlyCollection<string> expectedIds, string absoluteResultDumpPath, string absoluteTrxPath)
    {
        ValidateCatalog(catalog);
        Require(expectedIds is not null && expectedIds.Count is > 0 and <= MaximumCases, "empty-or-oversized-selection");
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in expectedIds!)
            Require(IsValue(id) && selectedIds.Add(id), "duplicate-or-invalid-selection");
        var selected = catalog.Where(item => selectedIds.Contains(item.CanonicalId)).ToArray();
        Require(selected.Length == selectedIds.Count, "unknown-selected-case");
        var expectedByName = selected.ToDictionary(item => item.FullName, StringComparer.Ordinal);
        var run = ReadTerminalRun(ReadXml(absoluteResultDumpPath, adapterDump: true));
        var results = ReadTerminalCases(run, expectedByName);
        var platformDurations = VerifyTrx(ReadXml(absoluteTrxPath, adapterDump: false), expectedByName);
        var ordered = selected.Select(item => results[item.FullName] with
        {
            PlatformDuration = platformDurations[item.FullName],
        }).ToArray();
        // Overflow cannot be deferred to a caller's first read of TotalDuration.
        try { _ = ordered.Sum(item => item.Duration.Ticks); }
        catch (OverflowException) { throw Rejected("duration-total-overflow"); }
        return new(Array.AsReadOnly(ordered));
    }

    private static void ValidateCatalog(IReadOnlyList<DiscoveredNUnitCase> catalog)
    {
        Require(catalog is not null && catalog.Count is > 0 and <= MaximumCases, "invalid-catalog");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var platformIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in catalog!)
            Require(item is not null && IsValue(item.CanonicalId) && IsValue(item.FullName) &&
                IsValue(item.PlatformId) && IsValue(item.ClassName) && IsValue(item.MethodName) &&
                ids.Add(item.CanonicalId) && names.Add(item.FullName) && platformIds.Add(item.PlatformId), "invalid-catalog");
    }

    private static XElement ReadTerminalRun(XDocument document)
    {
        var root = document.Root!;
        if (root.Name == "test-run")
        {
            Require(root.Attribute("result") is not null, "missing-terminal-run");
            return root;
        }
        Require(root.Name == "NUnitXml", "invalid-result-root");
        var terminals = root.Elements("test-run").Where(element => element.Attribute("result") is not null).ToList();
        foreach (var entry in root.Elements("NUnitTestEvent"))
        {
            Require(!entry.Elements().Any(), "invalid-adapter-event");
            var payload = ParseXml(entry.Value);
            var eventRoot = payload.Root!;
            if (eventRoot.Name == "test-run" && eventRoot.Attribute("result") is not null) terminals.Add(eventRoot);
            if (eventRoot.Name == "test-case" || eventRoot.Name == "test-suite")
                Require((string?)eventRoot.Attribute("result") is null or "Passed", "non-passing-adapter-event");
        }
        return Only(terminals, "missing-or-duplicate-terminal-run");
    }

    private static Dictionary<string, NUnitCaseResult> ReadTerminalCases(XElement run,
        IReadOnlyDictionary<string, DiscoveredNUnitCase> expected)
    {
        Require(Required(run, "result") == "Passed", "non-passing-run");
        Require(!run.Descendants("failure").Any(), "run-has-failure");
        var leaves = run.Descendants("test-case").ToArray();
        Require(leaves.Length == expected.Count, "executed-case-set-mismatch");
        var results = new Dictionary<string, NUnitCaseResult>(StringComparer.Ordinal);
        var runtimeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var leaf in leaves)
        {
            var fullName = Required(leaf, "fullname");
            Require(expected.TryGetValue(fullName, out var discovered), "unknown-executed-case");
            var explicitId = CanonicalProperty(leaf);
            Require(explicitId is null || explicitId == discovered!.CanonicalId, "executed-canonical-id-mismatch");
            OptionalMatch(leaf, "classname", discovered!.ClassName);
            OptionalMatch(leaf, "methodname", discovered.MethodName);
            Require((string?)leaf.Attribute("runstate") is null or "Runnable", "non-runnable-result");
            Require(Required(leaf, "result") == "Passed", "non-passing-case");
            if (leaf.Attribute("id") is not null)
                Require(runtimeIds.Add(Required(leaf, "id")), "duplicate-result-runtime-id");
            var result = new NUnitCaseResult(discovered.CanonicalId, fullName, "passed", Seconds(leaf, "duration"));
            Require(results.TryAdd(fullName, result), "duplicate-executed-case");
        }
        Require(results.Count == expected.Count, "executed-case-set-mismatch");
        foreach (var suite in run.DescendantsAndSelf().Where(element => element.Name == "test-run" || element.Name == "test-suite"))
        {
            Require(Required(suite, "result") == "Passed", "non-passing-suite");
            Require((string?)suite.Attribute("runstate") is null or "Runnable", "non-runnable-suite");
            var actual = suite.Descendants("test-case").Count();
            Require(Count(suite, "total") == actual && Count(suite, "passed") == actual &&
                Count(suite, "failed") == 0 && Count(suite, "warnings") == 0 &&
                Count(suite, "inconclusive") == 0 && Count(suite, "skipped") == 0, "suite-counter-mismatch");
            foreach (var optional in new[] { "initiated", "completed" })
                if (suite.Attribute(optional) is not null) Require(Count(suite, optional) == actual, "suite-counter-mismatch");
        }
        return results;
    }

    private static IReadOnlyDictionary<string, TimeSpan> VerifyTrx(XDocument document,
        IReadOnlyDictionary<string, DiscoveredNUnitCase> expected)
    {
        var root = document.Root!;
        Require(root.Name == TrxNamespace + "TestRun", "invalid-trx-root");
        var summary = Only(root.Elements(TrxNamespace + "ResultSummary"), "missing-or-duplicate-trx-summary");
        Require(Required(summary, "outcome") == "Completed", "non-passing-trx-run");
        Require(!summary.Descendants(TrxNamespace + "RunInfo").Any(element =>
            (string?)element.Attribute("outcome") is not (null or "Passed" or "Completed")), "trx-run-error");
        Require(!summary.Descendants(TrxNamespace + "ErrorInfo").Any(), "trx-run-error");
        var counters = Only(summary.Elements(TrxNamespace + "Counters"), "missing-or-duplicate-trx-counters");
        Require(Count(counters, "total") == expected.Count && Count(counters, "executed") == expected.Count &&
            Count(counters, "passed") == expected.Count && TrxZeroCounters.All(name => Count(counters, name) == 0), "trx-counter-mismatch");
        var definitionsRoot = Only(root.Elements(TrxNamespace + "TestDefinitions"), "missing-or-duplicate-trx-definitions");
        var definitions = definitionsRoot.Elements(TrxNamespace + "UnitTest").ToArray();
        Require(definitions.Length == expected.Count, "trx-definition-set-mismatch");
        var definitionsById = new Dictionary<string, (string FullName, string Name, string ExecutionId)>(StringComparer.Ordinal);
        var definedNames = new HashSet<string>(StringComparer.Ordinal);
        var executionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var method = Only(definition.Elements(TrxNamespace + "TestMethod"), "invalid-trx-method");
            var name = Required(definition, "name");
            var fullName = Required(method, "className") + "." + name;
            Require(expected.TryGetValue(fullName, out var discovered), "foreign-trx-definition");
            var methodName = Required(method, "name");
            // Adapter/VSTest names this field with the display row; the observed
            // MTP report retains the source method. The display row still owns
            // the full-name join in either format.
            Require(methodName == name || methodName == discovered.MethodName, "trx-definition-name-mismatch");
            var execution = Only(definition.Elements(TrxNamespace + "Execution"), "invalid-trx-execution");
            var executionId = Required(execution, "id");
            Require(expected.ContainsKey(fullName) && definedNames.Add(fullName) && executionIds.Add(executionId) &&
                definitionsById.TryAdd(Required(definition, "id"), (fullName, name, executionId)), "foreign-or-duplicate-trx-definition");
        }
        var resultsRoot = Only(root.Elements(TrxNamespace + "Results"), "missing-or-duplicate-trx-results");
        var rows = resultsRoot.Elements(TrxNamespace + "UnitTestResult").ToArray();
        Require(rows.Length == expected.Count && rows.Length == resultsRoot.Elements().Count(), "trx-result-set-mismatch");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var durations = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            Require(definitionsById.TryGetValue(Required(row, "testId"), out var owner), "unknown-trx-result");
            Require(seen.Add(owner.FullName) && Required(row, "executionId") == owner.ExecutionId &&
                Required(row, "testName") == owner.Name, "duplicate-or-foreign-trx-result");
            Require(Required(row, "outcome") == "Passed", "non-passing-trx-case");
            Require(TimeSpan.TryParseExact(Required(row, "duration"), "c", CultureInfo.InvariantCulture, out var duration) &&
                duration >= TimeSpan.Zero, "invalid-trx-duration");
            Require(!row.Descendants(TrxNamespace + "ErrorInfo").Any(), "trx-case-error");
            durations.Add(owner.FullName, duration);
        }
        return durations;
    }

    private static XDocument ReadXml(string absolutePath, bool adapterDump)
    {
        Require(!string.IsNullOrWhiteSpace(absolutePath) && Path.IsPathFullyQualified(absolutePath), "evidence-path-must-be-absolute");
        string text;
        try
        {
            using var input = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(input.Length is > 0 and <= MaximumBytes, "evidence-size-limit");
            using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (NUnitEvidenceException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw Rejected("evidence-read-failed");
        }
        try { return ParseXml(text); }
        catch (NUnitEvidenceException error) when (adapterDump && error.Code == "invalid-evidence-xml")
        {
            // Adapter 6.3.1's observed debug envelope has these two invalid XML
            // debug tags. Interpret one exact pair only after normal XML parsing
            // failed, so matching text inside valid CDATA is never transformed.
            // Raw bytes are never rewritten; malformed terminal XML still fails.
            const string opening = "<VSTest input/>";
            const string closing = "</VSTest input/>";
            var start = text.IndexOf(opening, StringComparison.Ordinal);
            var end = text.IndexOf(closing, StringComparison.Ordinal);
            Require(start >= 0 && end > start && text.IndexOf(opening, start + opening.Length, StringComparison.Ordinal) < 0 &&
                text.IndexOf(closing, end + closing.Length, StringComparison.Ordinal) < 0, "invalid-evidence-xml");
            var interpreted = text[..start] + "<VSTestInput>" + text[(start + opening.Length)..end] + "</VSTestInput>"
                + text[(end + closing.Length)..];
            return ParseXml(interpreted);
        }
    }

    private static XDocument ParseXml(string text)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumBytes,
                MaxCharactersFromEntities = 1024,
            };
            // Enforce depth and node limits before allocating the element tree.
            var elements = 0;
            using (var validationInput = new StringReader(text))
            using (var validationReader = XmlReader.Create(validationInput, settings))
            {
                while (validationReader.Read())
                    if (validationReader.NodeType == XmlNodeType.Element)
                        Require(++elements <= MaximumElements && validationReader.Depth <= 128, "xml-structure-limit");
            }
            using var input = new StringReader(text);
            using var reader = XmlReader.Create(input, settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            Require(document.Root is not null, "missing-xml-root");
            return document;
        }
        catch (XmlException) { throw Rejected("invalid-evidence-xml"); }
    }

    private static string? CanonicalProperty(XElement leaf)
    {
        var properties = leaf.Elements("properties").Elements("property")
            .Where(property => (string?)property.Attribute("name") == "FishAudioCaseId").ToArray();
        Require(properties.Length <= 1, "duplicate-canonical-property");
        return properties.Length == 0 ? null : Required(properties[0], "value");
    }

    private static void OptionalMatch(XElement element, string attribute, string expected)
    {
        if (element.Attribute(attribute) is not null)
            Require(Required(element, attribute) == expected, "result-method-identity-mismatch");
    }

    private static TimeSpan Seconds(XElement element, string attribute)
    {
        Require(decimal.TryParse(Required(element, attribute), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
            out var seconds) && seconds >= 0 && seconds <= (decimal)long.MaxValue / TimeSpan.TicksPerSecond, "invalid-duration");
        return TimeSpan.FromTicks((long)decimal.Round(seconds * TimeSpan.TicksPerSecond, 0, MidpointRounding.AwayFromZero));
    }

    private static int Count(XElement element, string attribute)
    {
        Require(int.TryParse(Required(element, attribute), NumberStyles.None, CultureInfo.InvariantCulture, out var count) &&
            count >= 0, "invalid-evidence-counter");
        return count;
    }

    private static string Required(XElement element, string attribute)
    {
        var value = (string?)element.Attribute(attribute);
        Require(IsValue(value), "missing-or-invalid-evidence-field");
        return value!;
    }

    private static bool IsValue(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumValueLength &&
        !value.Any(char.IsControl);

    private static XElement Only(IEnumerable<XElement> elements, string code)
    {
        var values = elements.Take(2).ToArray();
        Require(values.Length == 1, code);
        return values[0];
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw Rejected(code);
    }

    private static NUnitEvidenceException Rejected(string code) => new(code);
}
