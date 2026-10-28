using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class NUnitExecutionPlanTests
{
    private const string Prefix = "fa-execution-plan-tests-";
    private const string OrdinaryA = "plan.ordinary/a", OrdinaryB = "plan.ordinary/b";
    private const string FreshA = "plan.fresh/a", FreshB = "plan.fresh/b";
    private const string ProfileFirst = "plan.profile/first", ProfileSecond = "plan.profile/second";
    private const string ProfileName = "native-profile";
    private const string PlanMarker = "execution-plan-selected-cases-must-produce-phases";
    private const string RejectMarker = "execution-plan-invalid-request-must-not-admit";
    private string _parent = "", _root = "";

    [SetUp]
    public void CreateOwnedRawInputs()
    {
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        RequireDirectoryChain(_parent);
        _root = Path.Combine(_parent, Prefix + Guid.NewGuid().ToString("N"));
        if (File.Exists(_root) || Directory.Exists(_root)) throw new InvalidOperationException("execution-plan-fixture-owner-exists");
        Directory.CreateDirectory(_root);
        RequireOwnedRoot();
    }

    [TearDown]
    public void RetainFailedRawInputOrDeleteOnlyOwnedInputs()
    {
        if (_root.Length == 0) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            if (!Directory.Exists(_root))
            {
                if (File.Exists(_root)) throw new InvalidOperationException("execution-plan-fixture-not-directory");
                return;
            }
            RequireOwnedRoot();
            if (failed) { Note("execution-plan: failed owned raw fixture retained."); return; }
            var pending = new Stack<string>();
            pending.Push(_root);
            while (pending.Count != 0)
            {
                var directory = pending.Pop();
                RequireDirectoryChain(directory);
                foreach (var path in Directory.GetFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                        throw new InvalidOperationException("execution-plan-fixture-entry-invalid");
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                }
            }
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception)
        {
            Note("execution-plan: cleanup failed; owned raw fixture retained.");
            if (!failed) throw new InvalidOperationException("execution-plan-fixture-cleanup-failed");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-plan/ordinary-request-and-catalog-order")]
    public void OrdinaryCasesUseOnePhaseAndRetainBothRequestOrderAndCatalogOrder()
    {
        var plan = Plan(CreateDiscovery(), new[] { OrdinaryB, OrdinaryA }, maximumConcurrency: 3);
        Assert.That(plan.RequestedIds, Is.EqualTo(new[] { OrdinaryB, OrdinaryA }));
        Assert.That(plan.ExpandedIds, Is.EqualTo(new[] { OrdinaryA, OrdinaryB }));
        Assert.That(plan.ExpansionReasons, Is.Empty);
        Assert.That(plan.Phases, Has.Count.EqualTo(1));
        Phase(plan.Phases[0], "phase-0001", "ordinary", null, false, 3, OrdinaryA, OrdinaryB);
        Assert.That(plan.Phases[0].ExclusiveResources, Is.Empty);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-plan/inherited-fresh-each-leaf")]
    public void FixtureInheritedFreshHostCreatesAnIndependentPhaseForEachSelectedLeaf()
    {
        var xml = CreateDiscovery();
        Add(xml.Descendants("test-suite").Single(item => (string?)item.Attribute("name") == "FreshFixture"),
            "FishAudioExclusiveResource", "credentials");
        var plan = Plan(xml, new[] { FreshB, FreshA });
        Assert.That(plan.RequestedIds, Is.EqualTo(new[] { FreshB, FreshA }));
        Assert.That(plan.ExpandedIds, Is.EqualTo(new[] { FreshA, FreshB }));
        Assert.That(plan.ExpansionReasons, Is.Empty);
        Assert.That(plan.Phases, Has.Count.EqualTo(2), "execution-plan-fresh-fixture-must-not-be-one-shared-phase");
        Phase(plan.Phases[0], "phase-0001", "fresh-case", null, false, 1, FreshA);
        Phase(plan.Phases[1], "phase-0002", "fresh-case", null, false, 1, FreshB);
        Assert.That(plan.Phases.All(item => item.ExclusiveResources.SequenceEqual(new[] { "credentials" })), Is.True);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-plan/complete-cross-fixture-profile")]
    public void SelectingOneProfileLeafExpandsTheWholeProfileAndUsesOrdinalInsteadOfCatalogOrder()
    {
        var xml = CreateDiscovery();
        Add(Leaf(xml, ProfileFirst), "FishAudioExclusiveResource", "desktop");
        Add(Leaf(xml, ProfileSecond), "FishAudioExclusiveResource", "keyboard");
        var plan = Plan(xml, new[] { ProfileSecond });
        Assert.That(plan.RequestedIds, Is.EqualTo(new[] { ProfileSecond }));
        Assert.That(plan.ExpandedIds, Is.EqualTo(new[] { ProfileSecond, ProfileFirst }));
        Assert.That(plan.Phases, Has.Count.EqualTo(1));
        Phase(plan.Phases[0], "phase-0001", "ordered-profile", ProfileName, true, 1, ProfileFirst, ProfileSecond);
        Assert.That(plan.Phases[0].ExclusiveResources, Is.EquivalentTo(new[] { "desktop", "keyboard" }));
        Reason(plan, ProfileSecond, ProfileFirst);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-plan/ordinary-resource-serializes-whole-phase")]
    public void AnyOrdinaryExclusiveResourceSerializesTheWholeOrdinaryPhaseWithoutSelectingMoreCases()
    {
        var xml = CreateDiscovery();
        Add(Leaf(xml, OrdinaryA), "FishAudioExclusiveResource", "desktop");
        Add(Leaf(xml, OrdinaryB), "FishAudioExclusiveResource", "keyboard");
        var plan = Plan(xml, new[] { OrdinaryB, OrdinaryA });
        Assert.That(plan.ExpandedIds, Is.EqualTo(new[] { OrdinaryA, OrdinaryB }));
        Assert.That(plan.Phases, Has.Count.EqualTo(1));
        Phase(plan.Phases[0], "phase-0001", "ordinary", null, false, 1, OrdinaryA, OrdinaryB);
        Assert.That(plan.Phases[0].ExclusiveResources, Is.EquivalentTo(new[] { "desktop", "keyboard" }));
        Assert.That(plan.ExpansionReasons, Is.Empty, "execution-plan-resource-is-not-a-case-selector");
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-plan/mixed-disjoint-union")]
    public void MixedPhasesHaveAnExactDisjointExpandedUnionAndLeaveUnselectedProfilesAlone()
    {
        var requested = new[] { OrdinaryB, ProfileSecond, FreshB, OrdinaryA };
        var plan = Plan(CreateDiscovery(), requested);
        Assert.That(plan.RequestedIds, Is.EqualTo(requested));
        Assert.That(plan.ExpandedIds, Is.EqualTo(new[] { OrdinaryA, FreshB, ProfileSecond, ProfileFirst, OrdinaryB }));
        Assert.That(plan.Phases, Has.Count.EqualTo(3));
        Phase(plan.Phases[0], "phase-0001", "ordinary", null, false, 4, OrdinaryA, OrdinaryB);
        Phase(plan.Phases[1], "phase-0002", "fresh-case", null, false, 1, FreshB);
        Phase(plan.Phases[2], "phase-0003", "ordered-profile", ProfileName, true, 1, ProfileFirst, ProfileSecond);
        var owned = plan.Phases.SelectMany(item => item.Cases).Select(item => item.CanonicalId).ToArray();
        Assert.That(owned, Is.Unique);
        Assert.That(owned, Is.EquivalentTo(plan.ExpandedIds));
        Assert.That(owned, Does.Not.Contain(FreshA).And.Not.Contain("plan.unselected/zero").And.Not.Contain("plan.unselected/one"));
        Reason(plan, ProfileSecond, ProfileFirst);
    }

    [TestCase("empty", "execution-plan-empty-selection", TestName = "execution-plan/reject/empty-selection")]
    [TestCase("duplicate", "execution-plan-duplicate-selection", TestName = "execution-plan/reject/duplicate-selection")]
    [TestCase("unknown", "execution-plan-unknown-selection", TestName = "execution-plan/reject/unknown-selection")]
    [TestCase("concurrency-zero", "execution-plan-invalid-concurrency", TestName = "execution-plan/reject/concurrency-zero")]
    [TestCase("concurrency-five", "execution-plan-invalid-concurrency", TestName = "execution-plan/reject/concurrency-five")]
    public void InvalidRequestsCannotBecomeAnEmptyOrPartialSuccessfulPlan(string fault, string code)
    {
        var (path, _) = Admitted(CreateDiscovery());
        IReadOnlyList<string> requested = fault switch
        {
            "empty" => Array.Empty<string>(),
            "duplicate" => new[] { OrdinaryA, OrdinaryA },
            "unknown" => new[] { OrdinaryA, "private-plan-marker/missing" },
            _ => new[] { OrdinaryA },
        };
        var concurrency = fault == "concurrency-zero" ? 0 : fault == "concurrency-five" ? 5 : 4;
        Reject(() => NUnitExecutionPlan.ReadValidated(path, requested, concurrency), code, RejectMarker);
    }

    private NUnitExecutionPlanResult Plan(XDocument xml, IReadOnlyList<string> requested, int maximumConcurrency = 4)
    {
        var (path, catalog) = Admitted(xml);
        var plan = NUnitExecutionPlan.ReadValidated(path, requested, maximumConcurrency);
        Assert.That(plan.Phases.Count, Is.GreaterThan(0), PlanMarker);
        Assert.That(plan.RawDiscoverySha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))));
        var expected = catalog.ToDictionary(item => item.CanonicalId, StringComparer.Ordinal);
        foreach (var item in plan.Phases.SelectMany(phase => phase.Cases))
            Assert.That(item, Is.EqualTo(expected[item.CanonicalId]), "execution-plan-cases-must-retain-current-raw-catalog-identities");
        return plan;
    }

    private static void Phase(NUnitExecutionPhase phase, string id, string kind, string? profile,
        bool requireOrder, int concurrency, params string[] cases)
    {
        Assert.That(phase.Id, Is.EqualTo(id));
        Assert.That(phase.Kind, Is.EqualTo(kind));
        Assert.That(phase.Profile, Is.EqualTo(profile));
        Assert.That(phase.Cases.Select(item => item.CanonicalId), Is.EqualTo(cases));
        Assert.That(phase.RequireOrder, Is.EqualTo(requireOrder));
        Assert.That(phase.MaximumConcurrency, Is.EqualTo(concurrency));
    }

    private static void Reason(NUnitExecutionPlanResult plan, string selected, string added)
    {
        Assert.That(plan.ExpansionReasons, Has.Count.EqualTo(1));
        var reason = plan.ExpansionReasons[0];
        Assert.That(reason.Code, Is.EqualTo("complete-profile"));
        Assert.That(reason.Profile, Is.EqualTo(ProfileName));
        Assert.That(reason.SelectedIds, Is.EqualTo(new[] { selected }));
        Assert.That(reason.AddedIds, Is.EqualTo(new[] { added }));
    }

    private void Reject(TestDelegate action, string code, string marker)
    {
        var error = Assert.Throws<NUnitExecutionPlanException>(action, marker)!;
        Assert.That(error.Code, Is.EqualTo(code));
        Assert.That(error.Message, Is.EqualTo(code));
        Assert.That(error.Message, Does.Not.Contain(_root).And.Not.Contain("private-plan-marker"));
        Assert.That(error.InnerException, Is.Null);
    }

    private (string Path, IReadOnlyList<DiscoveredNUnitCase> Catalog) Admitted(XDocument xml)
    {
        var path = Write(xml);
        var catalog = NUnitEvidence.ReadCatalog(path);
        Assert.That(catalog.Count, Is.EqualTo(8), "execution-plan-fixture-must-first-pass-existing-catalog");
        var policies = NUnitExecutionMetadata.ReadValidated(path);
        Assert.That(policies.Select(item => item.Case), Is.EqualTo(catalog), "execution-plan-fixture-must-first-pass-current-policy-reader");
        return (path, catalog);
    }

    private string Write(XDocument xml)
    {
        var path = Path.Combine(_root, "private-plan-marker-" + Guid.NewGuid().ToString("N") + ".xml");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(file, new UTF8Encoding(false));
        writer.Write(xml.ToString(SaveOptions.DisableFormatting));
        return path;
    }

    private static XDocument CreateDiscovery() => new(new XElement("NUnitXml",
        new XElement("test-run", new XAttribute("id", "run"), new XAttribute("name", "example.dll"),
            new XAttribute("runstate", "Runnable"), new XAttribute("testcasecount", "8"),
            new XElement("test-suite", new XAttribute("type", "Assembly"), new XAttribute("id", "assembly"),
                new XAttribute("name", "example.dll"), new XAttribute("runstate", "Runnable"), new XAttribute("testcasecount", "8"),
                Fixture("OrdinaryFirst", false, null, (OrdinaryA, null)),
                Fixture("FreshFixture", true, null, (FreshA, null), (FreshB, null)),
                Fixture("ProfileEarlier", false, ProfileName, (ProfileSecond, 1)),
                Fixture("ProfileLater", false, ProfileName, (ProfileFirst, 0)),
                Fixture("OrdinaryLast", false, null, (OrdinaryB, null)),
                Fixture("UnselectedProfile", false, "unselected-profile", ("plan.unselected/zero", 0), ("plan.unselected/one", 1))))));

    private static XElement Fixture(string name, bool fresh, string? profile, params (string Id, int? Ordinal)[] rows)
    {
        var full = "Example." + name;
        var fixture = new XElement("test-suite", new XAttribute("type", "TestFixture"), new XAttribute("id", name),
            new XAttribute("name", name), new XAttribute("fullname", full), new XAttribute("runstate", "Runnable"),
            new XAttribute("testcasecount", rows.Length));
        if (fresh) Add(fixture, "FishAudioFreshHost", "true");
        if (profile is not null) Add(fixture, "FishAudioExecutionProfile", profile);
        foreach (var row in rows)
        {
            var leaf = new XElement("test-case", new XAttribute("id", row.Id), new XAttribute("name", row.Id),
                new XAttribute("fullname", full + "." + row.Id), new XAttribute("classname", full),
                new XAttribute("methodname", "Rows"), new XAttribute("runstate", "Runnable"));
            Add(leaf, "FishAudioCaseId", row.Id);
            if (row.Ordinal is { } ordinal) Add(leaf, "FishAudioProfileOrdinal", ordinal.ToString(CultureInfo.InvariantCulture));
            fixture.Add(leaf);
        }
        return fixture;
    }

    private static XElement Leaf(XDocument xml, string id) => xml.Descendants("test-case")
        .Single(item => item.Elements("properties").Elements("property").Any(property =>
            (string?)property.Attribute("name") == "FishAudioCaseId" && (string?)property.Attribute("value") == id));

    private static void Add(XElement owner, string name, string value)
    {
        var properties = owner.Element("properties");
        if (properties is null) { properties = new XElement("properties"); owner.AddFirst(properties); }
        properties.Add(new XElement("property", new XAttribute("name", name), new XAttribute("value", value)));
    }

    private void RequireOwnedRoot()
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(_root) || full != _root ||
            !string.Equals(Path.GetDirectoryName(full), _parent, StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length != Prefix.Length + 32 ||
            !Guid.TryParseExact(name[Prefix.Length..], "N", out _)) throw new InvalidOperationException("execution-plan-fixture-owner-invalid");
        RequireDirectoryChain(full);
    }

    private static void RequireDirectoryChain(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("execution-plan-fixture-directory-invalid");
    }

    private static void Note(string message)
    {
        try { TestContext.Progress.WriteLine(message); }
        catch { /* Diagnostics cannot replace the original failure. */ }
    }
}
