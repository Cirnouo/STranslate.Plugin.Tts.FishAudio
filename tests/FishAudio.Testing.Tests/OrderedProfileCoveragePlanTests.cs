using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FishAudio.Testing.Evidence;
using FishAudio.Testing.Execution;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

[TestFixture]
public sealed class OrderedProfileCoveragePlanTests
{
    [TestCase("partial", TestName = "ordered-group/reject-partial-profile")]
    [TestCase("reordered", TestName = "ordered-group/reject-reordered-profile")]
    [TestCase("wrong-discovery", TestName = "ordered-group/reject-stale-discovery-digest")]
    public void AGroupRequestCannotDropColdPredecessorsOrBorrowAnotherDiscovery(string fault)
    {
        var root = Path.Combine(Path.GetTempPath(), "fa-ordered-group-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var discovery = Path.Combine(root, "discovery.dump");
        try
        {
            File.WriteAllText(discovery, Discovery, new UTF8Encoding(false));
            var plan = OrderedProfileCoveragePlan.ReadValidated(discovery, "native-input");
            Assert.That(plan.Members.Select(item => item.Ordinal), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(plan.Members.Select(item => item.Case.CanonicalId), Is.EqualTo(new[] { "group/cold", "group/later" }));
            var cases = plan.Members.Select(item => new FrameworkCaseBinding(item.Case.CanonicalId, item.Case.FullName)).ToArray();
            if (fault == "partial") cases = [cases[1]];
            if (fault == "reordered") Array.Reverse(cases);
            var digest = fault == "wrong-discovery" ? new string('f', 64) : plan.RawDiscoverySha256;
            var error = Assert.Throws<OrderedProfileCoverageException>(() =>
                OrderedProfileCoveragePlan.ValidateSelection(discovery, "native-input", digest, cases, 1, true));
            Assert.That(error!.Code, Is.EqualTo("ordered-profile-coverage-selection-mismatch"));
            Assert.That(error.Message, Does.Not.Contain(root));
        }
        finally
        {
            // This fixture only creates these two known entries beneath its exact owned root.
            if (File.Exists(discovery)) File.Delete(discovery);
            Directory.Delete(root);
        }
    }

    [Test]
    public void CompleteCurrentProfileRetainsTheRealOrderedBindings()
    {
        var root = Path.Combine(Path.GetTempPath(), "fa-ordered-group-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var discovery = Path.Combine(root, "discovery.dump");
        try
        {
            File.WriteAllText(discovery, Discovery, new UTF8Encoding(false));
            var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(discovery)));
            var plan = OrderedProfileCoveragePlan.ReadValidated(discovery, "native-input");
            OrderedProfileCoveragePlan.ValidateSelection(discovery, "native-input", digest,
                plan.Members.Select(item => new FrameworkCaseBinding(item.Case.CanonicalId, item.Case.FullName)).ToArray(), 1, true);
            Assert.That(plan.MaximumConcurrency, Is.EqualTo(1));
            Assert.That(plan.RequireOrder, Is.True);
            Assert.That(plan.CompleteBaseline, Is.False);
        }
        finally { if (File.Exists(discovery)) File.Delete(discovery); Directory.Delete(root); }
    }

    [Test]
    [Property("FishAudioCaseId", "ordered-group/declared-compatibility-profile-keeps-four-row-owner")]
    public void DeclaredCompatibilityProfileRetainsAllFourRowsAcrossFixturesWithoutDesktop()
    {
        WithGeneralDiscovery(GeneralDiscovery(), path =>
        {
            var plan = OrderedProfileCoveragePlan.ReadValidated(path, "compat.ordered-profile");
            Assert.That(plan.Members.Select(item => item.Ordinal), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(plan.Members.Select(item => item.Case.CanonicalId), Is.EqualTo(new[]
            {
                "compat.ordered-profile/first-0", "compat.ordered-profile/first-1",
                "compat.ordered-profile/second-2", "compat.ordered-profile/second-3",
            }));
            Assert.That(plan.ExclusiveResources, Is.Empty);
            Assert.That(plan.RequiresQueuedRootActions, Is.False);
            Assert.That(plan.ExecutionDependency, Is.EqualTo("ordered-profile:compat.ordered-profile"));
            Assert.That(plan.RequireOrder && plan.MaximumConcurrency == 1, Is.True);
            OrderedProfileCoveragePlan.ValidateSelection(path, plan.Profile, plan.RawDiscoverySha256,
                plan.Members.Select(item => new FrameworkCaseBinding(item.Case.CanonicalId, item.Case.FullName)).ToArray(), 1, true);
            Assert.That(plan.CompleteBaseline, Is.False);
        });
    }

    [TestCase("native-settings", TestName = "ordered-group/native-settings-still-requires-desktop-and-roots")]
    [TestCase("native-input", TestName = "ordered-group/native-input-still-requires-desktop-and-roots")]
    [TestCase("native-popups", TestName = "ordered-group/native-popups-still-requires-desktop-and-roots")]
    [TestCase("native-cards", TestName = "ordered-group/native-cards-still-requires-desktop-and-roots")]
    public void NativeProfilesRetainTheirDesktopAndRootRequirements(string profile)
    {
        var source = Discovery.Replace("native-input", profile, StringComparison.Ordinal);
        var document = XDocument.Parse(source);
        document.Descendants("property").Where(item => (string?)item.Attribute("name") == "FishAudioExclusiveResource").Remove();
        WithGeneralDiscovery(document.ToString(SaveOptions.DisableFormatting), path =>
            Assert.Throws<OrderedProfileCoverageException>(() => OrderedProfileCoveragePlan.ReadValidated(path, profile)));
        WithGeneralDiscovery(source, path =>
        {
            var plan = OrderedProfileCoveragePlan.ReadValidated(path, profile);
            Assert.That(plan.ExclusiveResources, Does.Contain("desktop"));
            Assert.That(plan.RequiresQueuedRootActions, Is.True);
            Assert.That(plan.ExecutionDependency, Is.EqualTo("ordered-native-profile:" + profile));
        });
    }

    [TestCase("unknown", TestName = "ordered-group/reject-undeclared-general-profile")]
    [TestCase("empty", TestName = "ordered-group/reject-empty-general-profile")]
    [TestCase("malformed", TestName = "ordered-group/reject-malformed-general-profile")]
    [TestCase("gap", TestName = "ordered-group/reject-general-ordinal-gap")]
    [TestCase("duplicate", TestName = "ordered-group/reject-general-duplicate-ordinal")]
    [TestCase("missing-ordinal", TestName = "ordered-group/reject-general-member-without-ordinal")]
    [TestCase("owned-child", TestName = "ordered-group/reject-general-owned-app-host")]
    [TestCase("partial", TestName = "ordered-group/reject-general-isolated-substitution")]
    [TestCase("resource-drift", TestName = "ordered-group/reject-general-resource-drift-under-old-digest")]
    [TestCase("unordered", TestName = "ordered-group/reject-general-unordered-request")]
    [TestCase("concurrent", TestName = "ordered-group/reject-general-concurrent-request")]
    public void GeneralProfilesCannotLoseDeclarationMembershipOrExecutionOwnership(string fault)
    {
        var document = XDocument.Parse(GeneralDiscovery());
        var ordinals = document.Descendants("property").Where(item => (string?)item.Attribute("name") == "FishAudioProfileOrdinal").ToArray();
        if (fault == "gap") ordinals[3].SetAttributeValue("value", "4");
        if (fault == "duplicate") ordinals[3].SetAttributeValue("value", "2");
        if (fault == "missing-ordinal") ordinals[3].Remove();
        if (fault == "owned-child") document.Descendants("test-case").First().Element("properties")!.Add(
            new XElement("property", new XAttribute("name", "FishAudioOwnedChild"), new XAttribute("value", "true")));
        WithGeneralDiscovery(document.ToString(SaveOptions.DisableFormatting), path =>
        {
            var profile = fault switch { "unknown" => "not.declared", "empty" => "", "malformed" => "Compat/Unsafe", _ => "compat.ordered-profile" };
            if (fault is "unknown" or "empty" or "malformed" or "gap" or "duplicate" or "missing-ordinal" or "owned-child")
            {
                var error = Assert.Throws<OrderedProfileCoverageException>(() => OrderedProfileCoveragePlan.ReadValidated(path, profile));
                Assert.That(error!.Message, Does.Not.Contain(path));
                return;
            }
            var plan = OrderedProfileCoveragePlan.ReadValidated(path, profile);
            var cases = plan.Members.Select(item => new FrameworkCaseBinding(item.Case.CanonicalId, item.Case.FullName)).ToArray();
            if (fault == "partial") cases = [cases[0]];
            if (fault == "resource-drift")
            {
                document.Descendants("test-case").First().Element("properties")!.Add(new XElement("property",
                    new XAttribute("name", "FishAudioExclusiveResource"), new XAttribute("value", "configuration-store")));
                File.WriteAllText(path, document.ToString(SaveOptions.DisableFormatting), new UTF8Encoding(false));
                Assert.That(OrderedProfileCoveragePlan.ReadValidated(path, profile).ExclusiveResources, Does.Contain("configuration-store"));
            }
            Assert.Throws<OrderedProfileCoverageException>(() => OrderedProfileCoveragePlan.ValidateSelection(path, profile,
                plan.RawDiscoverySha256, cases, fault == "concurrent" ? 2 : 1, fault != "unordered"));
        });
    }

    private static void WithGeneralDiscovery(string xml, Action<string> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "fa-declared-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "discovery.dump");
        try { File.WriteAllText(path, xml, new UTF8Encoding(false)); body(path); }
        finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(root); }
    }

    // Reduced raw metadata for the existing four runnable compatibility rows;
    // this is a protocol fixture, not a claim that discovery or coverage ran.
    internal static string GeneralDiscovery() => """
        <NUnitXml><test-run id="run" name="fixture.dll" runstate="Runnable" testcasecount="4">
          <test-suite type="Assembly" id="assembly" name="fixture.dll" runstate="Runnable" testcasecount="4">
            <test-suite type="TestFixture" id="first" fullname="NUnitCompatibility.OrderedProfileZuluFixture" runstate="Runnable">
              <test-case id="row-0" name="OrderedProfile_ZuluFixture_ZuluRow0" fullname="NUnitCompatibility.OrderedProfileZuluFixture.OrderedProfile_ZuluFixture_ZuluRow0" classname="NUnitCompatibility.OrderedProfileZuluFixture" methodname="AProfileLeafRetainsItsOwnerAcrossAwait" runstate="Runnable">
                <properties><property name="FishAudioCaseId" value="compat.ordered-profile/first-0"/><property name="FishAudioExecutionProfile" value="compat.ordered-profile"/><property name="FishAudioProfileOrdinal" value="0"/></properties>
              </test-case>
              <test-case id="row-1" name="OrderedProfile_ZuluFixture_AlphaRow1" fullname="NUnitCompatibility.OrderedProfileZuluFixture.OrderedProfile_ZuluFixture_AlphaRow1" classname="NUnitCompatibility.OrderedProfileZuluFixture" methodname="AProfileLeafRetainsItsOwnerAcrossAwait" runstate="Runnable">
                <properties><property name="FishAudioCaseId" value="compat.ordered-profile/first-1"/><property name="FishAudioExecutionProfile" value="compat.ordered-profile"/><property name="FishAudioProfileOrdinal" value="1"/></properties>
              </test-case>
            </test-suite>
            <test-suite type="TestFixture" id="second" fullname="NUnitCompatibility.OrderedProfileAlphaFixture" runstate="Runnable">
              <test-case id="row-2" name="OrderedProfile_AlphaFixture_ZuluRow2" fullname="NUnitCompatibility.OrderedProfileAlphaFixture.OrderedProfile_AlphaFixture_ZuluRow2" classname="NUnitCompatibility.OrderedProfileAlphaFixture" methodname="AProfileLeafRetainsItsOwnerAcrossAwait" runstate="Runnable">
                <properties><property name="FishAudioCaseId" value="compat.ordered-profile/second-2"/><property name="FishAudioExecutionProfile" value="compat.ordered-profile"/><property name="FishAudioProfileOrdinal" value="2"/></properties>
              </test-case>
              <test-case id="row-3" name="OrderedProfile_AlphaFixture_AlphaRow3" fullname="NUnitCompatibility.OrderedProfileAlphaFixture.OrderedProfile_AlphaFixture_AlphaRow3" classname="NUnitCompatibility.OrderedProfileAlphaFixture" methodname="AProfileLeafRetainsItsOwnerAcrossAwait" runstate="Runnable">
                <properties><property name="FishAudioCaseId" value="compat.ordered-profile/second-3"/><property name="FishAudioExecutionProfile" value="compat.ordered-profile"/><property name="FishAudioProfileOrdinal" value="3"/></properties>
              </test-case>
            </test-suite>
          </test-suite>
        </test-run></NUnitXml>
        """;
    private const string Discovery = """
        <NUnitXml><test-run id="run" name="fixture.dll" runstate="Runnable" testcasecount="2">
          <test-suite type="Assembly" id="assembly" name="fixture.dll" runstate="Runnable" testcasecount="2">
            <test-suite type="TestFixture" id="fixture" name="Cases" fullname="GroupFixture.Cases" runstate="Runnable" testcasecount="2">
              <properties><property name="FishAudioExecutionProfile" value="native-input"/><property name="FishAudioExclusiveResource" value="desktop"/></properties>
              <test-case id="later" name="Later" fullname="GroupFixture.Cases.Later" classname="GroupFixture.Cases" methodname="Later" runstate="Runnable">
                <properties><property name="FishAudioCaseId" value="group/later"/><property name="FishAudioProfileOrdinal" value="1"/></properties>
              </test-case>
              <test-case id="cold" name="Cold" fullname="GroupFixture.Cases.Cold" classname="GroupFixture.Cases" methodname="Cold" runstate="Runnable">
                <properties><property name="FishAudioCaseId" value="group/cold"/><property name="FishAudioProfileOrdinal" value="0"/></properties>
              </test-case>
            </test-suite>
          </test-suite>
        </test-run></NUnitXml>
        """;
}
