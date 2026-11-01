using System.Globalization;
using System.Text;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class NUnitExecutionMetadataTests
{
    private const string OwnedPrefix = "fa-execution-metadata-tests-";
    private const string Fresh = "FishAudioFreshHost";
    private const string Profile = "FishAudioExecutionProfile";
    private const string Ordinal = "FishAudioProfileOrdinal";
    private const string Resource = "FishAudioExclusiveResource";
    private const string ProfileName = "native-settings";
    private const string CountMarker = "execution-metadata-must-return-every-catalog-leaf";
    private const string RejectMarker = "execution-metadata-invalid-policy-must-not-admit";
    private string _parent = "";
    private string _root = "";

    // These literals describe admitted discovery metadata, not runnable fixture
    // bodies. No fixture construction, native state, CWD or environment change.
    private const string Discovery = """
        <NUnitXml>
          <test-run id="0" name="example.dll" runstate="Runnable" testcasecount="4">
            <test-suite type="Assembly" id="0-10" name="example.dll" runstate="Runnable" testcasecount="4">
              <test-suite type="TestFixture" id="0-11" name="FirstFixture" fullname="Example.FirstFixture" runstate="Runnable" testcasecount="2">
                <test-suite type="ParameterizedMethod" id="0-12" name="Rows" fullname="Example.FirstFixture.Rows" runstate="Runnable" testcasecount="2">
                  <test-case id="0-1" name="first row zero" fullname="Example.FirstFixture.first row zero" methodname="Rows" classname="Example.FirstFixture" runstate="Runnable">
                    <properties><property name="FishAudioCaseId" value="metadata.first/zero" /></properties>
                  </test-case>
                  <test-case id="0-2" name="first row one" fullname="Example.FirstFixture.first row one" methodname="Rows" classname="Example.FirstFixture" runstate="Runnable">
                    <properties><property name="FishAudioCaseId" value="metadata.first/one" /></properties>
                  </test-case>
                </test-suite>
              </test-suite>
              <test-suite type="TestFixture" id="0-21" name="SecondFixture" fullname="Example.SecondFixture" runstate="Runnable" testcasecount="2">
                <test-suite type="ParameterizedMethod" id="0-22" name="Rows" fullname="Example.SecondFixture.Rows" runstate="Runnable" testcasecount="2">
                  <test-case id="0-3" name="second row two" fullname="Example.SecondFixture.second row two" methodname="Rows" classname="Example.SecondFixture" runstate="Runnable">
                    <properties><property name="FishAudioCaseId" value="metadata.second/two" /></properties>
                  </test-case>
                  <test-case id="0-4" name="second row three" fullname="Example.SecondFixture.second row three" methodname="Rows" classname="Example.SecondFixture" runstate="Runnable">
                    <properties><property name="FishAudioCaseId" value="metadata.second/three" /></properties>
                  </test-case>
                </test-suite>
              </test-suite>
            </test-suite>
          </test-run>
        </NUnitXml>
        """;

    [SetUp]
    public void CreateOwnedDirectory()
    {
        _parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        RequireDirectoryChain(_parent);
        _root = Path.Combine(_parent, OwnedPrefix + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(_root) || File.Exists(_root))
            throw new InvalidOperationException("execution-metadata-fixture-owner-exists");
        Directory.CreateDirectory(_root);
        RequireOwnedRoot();
    }

    [TearDown]
    public void RetainFailedRawInputOrDeleteOnlyTheOwnedFixture()
    {
        if (_root.Length == 0) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            if (!Directory.Exists(_root))
            {
                if (File.Exists(_root)) throw new InvalidOperationException("execution-metadata-fixture-owner-not-directory");
                return;
            }
            RequireOwnedRoot();
            if (failed)
            {
                SafeNote("execution-metadata: failed raw fixture retained in its owned temporary directory.");
                return;
            }
            RequireRegularTree(_root);
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception)
        {
            SafeNote("execution-metadata: cleanup failed; owned raw fixture retained.");
            if (!failed) throw new InvalidOperationException("execution-metadata-fixture-cleanup-failed");
        }
    }

    private void RequireOwnedRoot()
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var name = Path.GetFileName(full);
        if (!Path.IsPathFullyQualified(_root) || full != _root ||
            !string.Equals(Path.GetDirectoryName(full), _parent, StringComparison.OrdinalIgnoreCase) ||
            !name.StartsWith(OwnedPrefix, StringComparison.Ordinal) || name.Length != OwnedPrefix.Length + 32 ||
            !Guid.TryParseExact(name[OwnedPrefix.Length..], "N", out _))
            throw new InvalidOperationException("execution-metadata-fixture-owner-invalid");
        RequireDirectoryChain(full);
    }

    private static void RequireDirectoryChain(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new InvalidOperationException("execution-metadata-fixture-directory-invalid");
    }

    private static void RequireRegularTree(string root)
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
                    throw new InvalidOperationException("execution-metadata-fixture-entry-invalid");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
            }
        }
    }

    private static void SafeNote(string message)
    {
        try { TestContext.Progress.WriteLine(message); }
        catch { /* Diagnostic output cannot replace the primary test failure. */ }
    }

    [TestCase(true, TestName = "execution-metadata/default-policy/nunit-wrapper")]
    [TestCase(false, TestName = "execution-metadata/default-policy/test-run-root")]
    public void OrdinaryLeavesKeepCatalogIdentityOrderAndDefaultExecutionPolicy(bool wrapper)
    {
        var xml = ParseDiscovery();
        if (!wrapper)
        {
            var run = xml.Root!.Element("test-run")!;
            run.Remove();
            xml = new XDocument(run);
        }
        var policies = Read(xml);
        Assert.That(policies.Select(item => item.FixtureFullName), Is.EqualTo(new[]
        {
            "Example.FirstFixture", "Example.FirstFixture", "Example.SecondFixture", "Example.SecondFixture",
        }));
        Assert.That(policies.All(item => !item.FreshHost && item.Profile is null && item.ProfileOrdinal is null
            && item.ExclusiveResources.Count == 0), Is.True, "execution-metadata-ordinary-leaf-must-remain-default");
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-metadata/observed-adapter-envelope")]
    public void TheSupportedAdapterEnvelopeRetainsCatalogAndExecutionPoliciesWithoutRewritingRawBytes()
    {
        var run = ParseDiscovery().Root!.Element("test-run")!;
        var raw = "<NUnitXml><VSTest input/><TestPackage>example.dll</TestPackage></VSTest input/>"
            + run.ToString(SaveOptions.DisableFormatting) + "</NUnitXml>";
        var path = Path.Combine(_root, "observed-adapter-envelope.xml");
        File.WriteAllText(path, raw, new UTF8Encoding(false));
        var bytes = File.ReadAllBytes(path);
        var catalog = NUnitEvidence.ReadCatalog(path);
        Assert.That(catalog.Count, Is.EqualTo(4), "The existing reader must first admit this observed adapter format.");
        IReadOnlyList<NUnitCaseExecutionPolicy>? policies = null;

        Assert.DoesNotThrow(() => policies = NUnitExecutionMetadata.ReadValidated(path),
            "execution-metadata-supported-adapter-envelope-must-remain-readable");

        Assert.That(policies!.Select(item => item.Case), Is.EqualTo(catalog));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes), "Adapter interpretation must retain the original raw bytes.");
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-metadata/fixture-fresh-host")]
    public void FixtureFreshHostAppliesOnlyToItsOwnLeaves()
    {
        var xml = ParseDiscovery();
        Add(Fixtures(xml)[0], Fresh, "true");
        var policies = Read(xml);
        Assert.That(policies.Select(item => item.FreshHost), Is.EqualTo(new[] { true, true, false, false }),
            "execution-metadata-fixture-fresh-host-must-be-inherited");
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-metadata/leaf-fresh-host")]
    public void LeafFreshHostDoesNotPromoteItsFixtureOrSibling()
    {
        var xml = ParseDiscovery();
        Add(Leaves(xml)[0], Fresh, "true");
        var policies = Read(xml);
        Assert.That(policies.Select(item => item.FreshHost), Is.EqualTo(new[] { true, false, false, false }),
            "execution-metadata-leaf-fresh-host-must-stay-leaf-scoped");
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-metadata/nearest-fixture")]
    public void FixtureFullNameComesFromTheNearestActualTestFixtureAncestor()
    {
        var xml = ParseDiscovery();
        var first = Fixtures(xml)[0];
        var outer = new XElement("test-suite", new XAttribute("type", "TestFixture"), new XAttribute("id", "0-90"),
            new XAttribute("name", "Outer"), new XAttribute("fullname", "Example.Outer"),
            new XAttribute("runstate", "Runnable"), new XAttribute("testcasecount", "2"));
        first.AddBeforeSelf(outer);
        first.Remove();
        outer.Add(first);
        Leaves(xml)[0].SetAttributeValue("classname", "Example.GeneratedClass");
        var policies = Read(xml);
        Assert.That(policies[0].Case.ClassName, Is.EqualTo("Example.GeneratedClass"));
        Assert.That(policies[0].FixtureFullName, Is.EqualTo("Example.FirstFixture"),
            "execution-metadata-fixture-must-not-be-derived-from-class-or-method-name");
        Assert.That(policies[1].FixtureFullName, Is.EqualTo("Example.FirstFixture"));
    }

    [TestCase(false, TestName = "execution-metadata/profile/catalog-order")]
    [TestCase(true, TestName = "execution-metadata/profile/ordinal-does-not-sort-catalog")]
    public void ACompleteProfileSpansFixturesAndImpliesFreshHostWithoutReorderingCases(bool permuteOrdinals)
    {
        var xml = ParseDiscovery();
        CompleteProfile(xml);
        var expected = permuteOrdinals ? new[] { 2, 0, 3, 1 } : new[] { 0, 1, 2, 3 };
        var leaves = Leaves(xml);
        for (var index = 0; index < leaves.Length; index++)
            Property(leaves[index], Ordinal).SetAttributeValue("value", expected[index].ToString(CultureInfo.InvariantCulture));
        var policies = Read(xml);
        Assert.That(policies.Select(item => item.Profile), Is.EqualTo(new[] { ProfileName, ProfileName, ProfileName, ProfileName }));
        Assert.That(policies.Select(item => item.ProfileOrdinal), Is.EqualTo(expected.Select(value => (int?)value)),
            "execution-metadata-profile-ordinal-must-be-preserved");
        Assert.That(policies.All(item => item.FreshHost), Is.True, "execution-metadata-profile-must-require-fresh-host");
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-metadata/inherited-profile-ordinal")]
    public void AFixtureOrdinalCanDescribeItsOnlyLeafWithinTheWholeProfile()
    {
        var xml = ParseDiscovery();
        var leaves = Leaves(xml);
        leaves[1].Remove();
        leaves[3].Remove();
        foreach (var owner in xml.Descendants().Where(item => item.Attribute("testcasecount") is not null))
            owner.SetAttributeValue("testcasecount", owner.Descendants("test-case").Count());
        var fixtures = Fixtures(xml);
        for (var index = 0; index < fixtures.Length; index++)
        {
            Add(fixtures[index], Profile, ProfileName);
            Add(fixtures[index], Ordinal, index.ToString(CultureInfo.InvariantCulture));
        }
        var policies = Read(xml);
        Assert.That(policies.Select(item => item.Case.CanonicalId), Is.EqualTo(new[] { "metadata.first/zero", "metadata.second/two" }));
        Assert.That(policies.Select(item => item.ProfileOrdinal), Is.EqualTo(new int?[] { 0, 1 }),
            "execution-metadata-ordinal-is-an-execution-only-inheritable-key");
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-metadata/resource-union-and-leaf-only-coverage")]
    public void ResourcesUnionAcrossAncestorsWithoutInheritingCoverageOrDependencyDeclarations()
    {
        var xml = ParseDiscovery();
        var assembly = xml.Descendants("test-suite").Single(item => (string?)item.Attribute("type") == "Assembly");
        var first = Fixtures(xml)[0];
        var method = first.Elements("test-suite").Single();
        Add(assembly, Resource, "desktop");
        Add(first, Resource, "keyboard");
        Add(first, Resource, "desktop");
        Add(method, Resource, "keyboard");
        Add(Leaves(xml)[0], Resource, "mouse");
        Add(Leaves(xml)[0], Resource, "mouse");
        Add(first, "FishAudioEmptyProductCoverage", "RepositoryResourcesOnly");
        Add(first, "FishAudioRepositoryDependency", "ancestor-private-marker.json");
        Add(first, "FishAudioUnrecognizedPolicy", "true");
        Add(Leaves(xml)[0], "FishAudioRepositoryDependency", "leaf-only.json");
        var (path, catalog) = WriteAdmittedCatalog(xml);
        var policies = ReadPolicies(path, catalog);
        Assert.That(policies[0].ExclusiveResources, Is.EquivalentTo(new[] { "desktop", "keyboard", "mouse" }),
            "execution-metadata-resources-must-union-without-duplicates");
        Assert.That(policies[1].ExclusiveResources, Is.EquivalentTo(new[] { "desktop", "keyboard" }));
        Assert.That(policies[2].ExclusiveResources, Is.EqualTo(new[] { "desktop" }));
        Assert.That(policies[0].FreshHost, Is.False, "execution-metadata-unrecognized-or-coverage-property-is-not-an-execution-policy");
        var leafMetadata = NUnitEvidence.ReadCaseMetadata(path, "metadata.first/zero");
        Assert.That(leafMetadata.Properties["FishAudioRepositoryDependency"], Is.EqualTo(new[] { "leaf-only.json" }));
        Assert.That(leafMetadata.Properties.ContainsKey("FishAudioEmptyProductCoverage"), Is.False,
            "execution-metadata-must-not-broaden-existing-coverage-inheritance");
        Assert.That(leafMetadata.Properties.ContainsKey("FishAudioUnrecognizedPolicy"), Is.False);
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-metadata/safe-identity-boundary")]
    public void BoundedSafeProfileAndResourceNamesArePreserved()
    {
        var xml = ParseDiscovery();
        var identity = "a" + new string('b', 127);
        CompleteProfile(xml, identity);
        Add(Fixtures(xml)[0], Resource, identity);
        var policies = Read(xml);
        Assert.That(policies[0].Profile, Is.EqualTo(identity));
        Assert.That(policies[0].ExclusiveResources, Is.EqualTo(new[] { identity }));
    }

    [TestCaseSource(nameof(InvalidPolicies))]
    public void InvalidExecutionPolicyCannotBeAdmittedAsAnOrdinaryOrPartialProfile(string fault, string code)
    {
        var xml = ParseDiscovery();
        MutatePolicy(xml, fault);
        var (path, _) = WriteAdmittedCatalog(xml);
        var error = Assert.Throws<NUnitExecutionMetadataException>(() => NUnitExecutionMetadata.ReadValidated(path), RejectMarker)!;
        Assert.That(error.Code, Is.EqualTo(code));
        Assert.That(error.Message, Is.EqualTo(code));
        Assert.That(error.Message, Does.Not.Contain(_root).And.Not.Contain("private-policy-marker"));
        Assert.That(error.InnerException, Is.Null);
    }

    public static IEnumerable<TestCaseData> InvalidPolicies()
    {
        foreach (var fault in new[] { "fresh-false", "fresh-uppercase", "fresh-whitespace", "fresh-empty", "fresh-false-over-true" })
            yield return Invalid(fault, "execution-metadata-invalid-fresh-host");
        foreach (var fault in new[] { "profile-unsafe", "profile-too-long" })
            yield return Invalid(fault, "execution-metadata-invalid-profile");
        foreach (var fault in new[] { "ordinal-without-profile", "ordinal-missing", "ordinal-leading-zero", "ordinal-negative", "ordinal-overflow" })
            yield return Invalid(fault, "execution-metadata-invalid-ordinal");
        foreach (var fault in new[] { "profile-duplicate-ordinal", "profile-gap" })
            yield return Invalid(fault, "execution-metadata-profile-ordinals");
        foreach (var fault in new[] { "duplicate-fresh", "duplicate-profile", "duplicate-ordinal" })
            yield return Invalid(fault, "execution-metadata-duplicate-scalar");
        foreach (var fault in new[] { "fixture-missing", "fixture-fullname-missing" })
            yield return Invalid(fault, "execution-metadata-invalid-fixture");
        foreach (var fault in new[] { "resource-empty", "resource-unsafe", "resource-control", "resource-too-long" })
            yield return Invalid(fault, "execution-metadata-invalid-resource");
    }

    private static TestCaseData Invalid(string fault, string code) => new TestCaseData(fault, code)
        .SetName("execution-metadata/reject/" + fault)
        .SetProperty("FishAudioCaseId", "infra.execution-metadata/reject/" + fault);

    private static void MutatePolicy(XDocument xml, string fault)
    {
        var fixtures = Fixtures(xml);
        var leaves = Leaves(xml);
        switch (fault)
        {
            case "fresh-false": Add(leaves[0], Fresh, "false"); break;
            case "fresh-uppercase": Add(leaves[0], Fresh, "TRUE"); break;
            case "fresh-whitespace": Add(leaves[0], Fresh, " true"); break;
            case "fresh-empty": Add(leaves[0], Fresh, ""); break;
            case "fresh-false-over-true": Add(fixtures[0], Fresh, "true"); Add(leaves[0], Fresh, "false"); break;
            case "profile-unsafe": CompleteProfile(xml, "private-policy-marker/profile"); break;
            case "profile-too-long": CompleteProfile(xml, new string('p', 129)); break;
            case "ordinal-without-profile": Add(leaves[0], Ordinal, "0"); break;
            case "ordinal-missing": CompleteProfile(xml); Property(leaves[0], Ordinal).Remove(); break;
            case "ordinal-leading-zero": CompleteProfile(xml); Property(leaves[0], Ordinal).SetAttributeValue("value", "00"); break;
            case "ordinal-negative": CompleteProfile(xml); Property(leaves[0], Ordinal).SetAttributeValue("value", "-1"); break;
            case "ordinal-overflow": CompleteProfile(xml); Property(leaves[0], Ordinal).SetAttributeValue("value", "2147483648"); break;
            case "profile-duplicate-ordinal": CompleteProfile(xml); Property(leaves[2], Ordinal).SetAttributeValue("value", "1"); break;
            case "profile-gap": CompleteProfile(xml); Property(leaves[3], Ordinal).SetAttributeValue("value", "4"); break;
            case "duplicate-fresh": Add(fixtures[0], Fresh, "true"); Add(fixtures[0], Fresh, "true"); break;
            case "duplicate-profile": CompleteProfile(xml); Add(fixtures[0], Profile, ProfileName); break;
            case "duplicate-ordinal": CompleteProfile(xml); Add(leaves[0], Ordinal, "0"); break;
            case "fixture-missing": fixtures[0].SetAttributeValue("type", "TestSuite"); break;
            case "fixture-fullname-missing": fixtures[0].Attribute("fullname")!.Remove(); break;
            case "resource-empty": Add(leaves[0], Resource, ""); break;
            case "resource-unsafe": Add(leaves[0], Resource, "../private-policy-marker"); break;
            case "resource-control": Add(leaves[0], Resource, "desktop\tprivate-policy-marker"); break;
            case "resource-too-long": Add(leaves[0], Resource, new string('r', 129)); break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }

    private IReadOnlyList<NUnitCaseExecutionPolicy> Read(XDocument xml)
    {
        var (path, catalog) = WriteAdmittedCatalog(xml);
        return ReadPolicies(path, catalog);
    }

    private static IReadOnlyList<NUnitCaseExecutionPolicy> ReadPolicies(string path, IReadOnlyList<DiscoveredNUnitCase> catalog)
    {
        var policies = NUnitExecutionMetadata.ReadValidated(path);
        Assert.That(policies.Count, Is.EqualTo(catalog.Count), CountMarker);
        Assert.That(policies.Select(item => item.Case), Is.EqualTo(catalog),
            "execution-metadata-must-preserve-validated-catalog-identity-and-order");
        return policies;
    }

    private (string Path, IReadOnlyList<DiscoveredNUnitCase> Catalog) WriteAdmittedCatalog(XDocument xml)
    {
        var path = Path.Combine(_root, "private-policy-marker-" + Guid.NewGuid().ToString("N") + ".xml");
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
            writer.Write(xml.ToString(SaveOptions.DisableFormatting));
        // Every policy test first crosses the already-supported discovery
        // reader. A malformed XML fixture must not masquerade as reader RED.
        var catalog = NUnitEvidence.ReadCatalog(path);
        Assert.That(catalog.Count, Is.EqualTo(Leaves(xml).Length), "execution-metadata-fixture-must-first-pass-existing-catalog-reader");
        Assert.That(catalog.Select(item => item.CanonicalId), Is.EqualTo(Leaves(xml)
            .Select(item => Property(item, "FishAudioCaseId").Attribute("value")!.Value)));
        return (path, catalog);
    }

    private static XDocument ParseDiscovery() => XDocument.Parse(Discovery);
    private static XElement[] Leaves(XDocument xml) => xml.Descendants("test-case").ToArray();
    private static XElement[] Fixtures(XDocument xml) => xml.Descendants("test-suite")
        .Where(item => (string?)item.Attribute("type") == "TestFixture").ToArray();
    private static XElement Property(XElement owner, string name) => owner.Elements("properties").Elements("property")
        .Single(item => (string?)item.Attribute("name") == name);

    private static void Add(XElement owner, string name, string value)
    {
        var properties = owner.Element("properties");
        if (properties is null) { properties = new XElement("properties"); owner.AddFirst(properties); }
        properties.Add(new XElement("property", new XAttribute("name", name), new XAttribute("value", value)));
    }

    private static void CompleteProfile(XDocument xml, string name = ProfileName)
    {
        foreach (var fixture in Fixtures(xml)) Add(fixture, Profile, name);
        var leaves = Leaves(xml);
        for (var index = 0; index < leaves.Length; index++)
            Add(leaves[index], Ordinal, index.ToString(CultureInfo.InvariantCulture));
    }
}
