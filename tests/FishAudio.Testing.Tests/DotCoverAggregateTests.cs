using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FishAudio.Testing.Evidence;
using NUnit.Framework;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class DotCoverAggregateTests
{
    // Reduced, sanitized shapes observed in CLI 2026.2.3.1 aggregate JSON:
    // omitted accessor names, nested compiled methods and OwnCoverage are real
    // report structures. Counts are declared fixtures, not product evidence;
    // retaining a parent does not imply its children are a complete partition.
    private const string Representative = """
        {
          "Kind": "Root", "CoveredStatements": 20, "TotalStatements": 6510, "CoveragePercent": 1,
          "Children": [
            {
              "Kind": "Assembly", "Name": "Other", "CoveredStatements": 1, "TotalStatements": 9, "CoveragePercent": 12,
              "Children": [{ "Kind": "Type", "Name": "Worker", "CoveredStatements": 1, "TotalStatements": 9, "CoveragePercent": 12 }]
            },
            {
              "Kind": "Assembly", "Name": "Product", "CoveredStatements": 19, "TotalStatements": 6501, "CoveragePercent": 1,
              "Children": [{
                "Kind": "Namespace", "Name": "Example", "CoveredStatements": 19, "TotalStatements": 6501, "CoveragePercent": 1,
                "Children": [{
                  "Kind": "Type", "Name": "Worker", "CoveredStatements": 19, "TotalStatements": 6501, "CoveragePercent": 1,
                  "Children": [
                    {
                      "Kind": "Property", "Name": "State:Task", "CoveredStatements": 1, "TotalStatements": 2, "CoveragePercent": 50,
                      "Children": [
                        { "Kind": "PropertyGetter", "CoveredStatements": 1, "TotalStatements": 1, "CoveragePercent": 100 },
                        { "Kind": "PropertySetter", "CoveredStatements": 0, "TotalStatements": 1, "CoveragePercent": 0 }
                      ]
                    },
                    {
                      "Kind": "AutoProperty", "Name": "Name:string", "CoveredStatements": 0, "TotalStatements": 1, "CoveragePercent": 0,
                      "Children": [
                        { "Kind": "PropertyGetter", "CoveredStatements": 0, "TotalStatements": 1, "CoveragePercent": 0 },
                        { "Kind": "PropertyInitOnlySetter", "CoveredStatements": 0, "TotalStatements": 0, "CoveragePercent": 0 }
                      ]
                    },
                    {
                      "Kind": "Method", "Name": "Run():Task", "CoveredStatements": 18, "TotalStatements": 6498, "CoveragePercent": 1,
                      "Children": [{ "Kind": "InternalCompiledMethod", "Name": "MoveNext():void", "CoveredStatements": 18, "TotalStatements": 6498, "CoveragePercent": 1 }]
                    },
                    {
                      "Kind": "Constructor", "Name": "Worker()", "CoveredStatements": 0, "TotalStatements": 0, "CoveragePercent": 0,
                      "Children": [
                        { "Kind": "OwnCoverage", "CoveredStatements": 0, "TotalStatements": 0, "CoveragePercent": 0 },
                        { "Kind": "AnonymousMethod", "Name": "(string):void", "CoveredStatements": 0, "TotalStatements": 0, "CoveragePercent": 0 }
                      ]
                    },
                    {
                      "Kind": "Event", "Name": "Changed:EventHandler", "CoveredStatements": 0, "TotalStatements": 0, "CoveragePercent": 0,
                      "Children": [
                        { "Kind": "EventAdder", "CoveredStatements": 0, "TotalStatements": 0, "CoveragePercent": 0 },
                        { "Kind": "EventRemover", "CoveredStatements": 0, "TotalStatements": 0, "CoveragePercent": 0 }
                      ]
                    }
                  ]
                }]
              }]
            }
          ]
        }
        """;

    private string _root = "";

    [SetUp]
    public void CreateOwnedDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), "fa-dotcover-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void DeleteOwnedDirectory()
    {
        var full = Path.GetFullPath(_root);
        Assert.That(Path.GetDirectoryName(full), Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetTempPath())).IgnoreCase);
        Assert.That(Path.GetFileName(full), Does.StartWith("fa-dotcover-tests-"));
        if (Directory.Exists(full)) Directory.Delete(full, recursive: true);
    }

    [Test]
    public void ExactAssemblySelectionRetainsHierarchyCountersAndRawFileDigest()
    {
        var path = Write(Representative);
        var report = Read(path);
        Assert.That(report.ObservedSchemaVersion, Is.EqualTo(1));
        Assert.That(report.ToolVersion, Is.EqualTo("2026.2.3.1"));
        Assert.That(report.ReportPath, Is.EqualTo(path));
        Assert.That(report.ReportSha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))));
        Assert.That(report.TargetAssemblyName, Is.EqualTo("Product"));
        Assert.That(report.TargetAssembly, Is.SameAs(report.Members[0]));
        Assert.That(report.TargetAssembly!.CoveredStatements, Is.EqualTo(19));
        Assert.That(report.TargetAssembly.TotalStatements, Is.EqualTo(6501));
        Assert.That(report.TargetAssembly.CoveragePercent, Is.EqualTo(1));
        Assert.That(report.Members, Has.Count.EqualTo(17));
        Assert.That(report.Members.All(member => member.AssemblyName == "Product"), Is.True);
        Assert.That(report.Members.Select(member => member.RawPath).Distinct().Count(), Is.EqualTo(17));
        var method = report.Members.Single(member => member.Name == "Run():Task");
        Assert.That(method.SymbolSegments, Is.EqualTo(new DotCoverSymbolSegment[]
        {
            new("Assembly", "Product"), new("Namespace", "Example"), new("Type", "Worker"), new("Method", "Run():Task"),
        }));
        Assert.That(method.RawPath, Is.EqualTo("/Children/1/Children/0/Children/0/Children/2"));
        Assert.That(method.Unresolved, Is.False);
        // Ancestor and descendant counts overlap. The reader returns their
        // observations without summing them into another coverage claim.
        Assert.That(report.Members.Sum(member => member.CoveredStatements), Is.GreaterThan(19));
    }

    [Test]
    public void PropertyAccessorsAndCompiledDescendantsRetainTheirExactParents()
    {
        var report = Read(Write(Representative));
        var getter = report.Members.Single(member => member.Kind == "PropertyGetter" && member.CoveredStatements == 1);
        Assert.That(getter.Name, Is.Null);
        Assert.That(getter.SymbolSegments[^2], Is.EqualTo(new DotCoverSymbolSegment("Property", "State:Task")));
        Assert.That(getter.SymbolSegments[^1], Is.EqualTo(new DotCoverSymbolSegment("PropertyGetter", null)));
        var initializer = report.Members.Single(member => member.Kind == "PropertyInitOnlySetter");
        Assert.That(initializer.SymbolSegments[^2].Kind, Is.EqualTo("AutoProperty"));
        var compiled = report.Members.Single(member => member.Kind == "InternalCompiledMethod");
        Assert.That(compiled.Name, Is.EqualTo("MoveNext():void"));
        Assert.That(compiled.SymbolSegments[^2], Is.EqualTo(new DotCoverSymbolSegment("Method", "Run():Task")));
        Assert.That(compiled.UnresolvedReasons, Does.Contain("compiler-generated-member"));
        var own = report.Members.Single(member => member.Kind == "OwnCoverage");
        Assert.That(own.Name, Is.Null);
        Assert.That(own.SymbolSegments[^2].Kind, Is.EqualTo("Constructor"));
        Assert.That(report.Members.Where(member => member.Kind is "Event" or "EventAdder" or "EventRemover")
            .All(member => member.CoveredStatements == 0 && member.TotalStatements == 0), Is.True);
    }

    [TestCase("Product", TestName = "aggregate/zero-covered-present-target")]
    [TestCase("product", TestName = "aggregate/exact-case-absent-target")]
    [TestCase("Product.dll", TestName = "aggregate/extension-absent-target")]
    public void AValidEmptyModuleDiffersFromAnAbsentExactAssembly(string target)
    {
        const string text = """
            {"Kind":"Root","CoveredStatements":0,"TotalStatements":0,"CoveragePercent":0,"Children":[
              {"Kind":"Assembly","Name":"Product","CoveredStatements":0,"TotalStatements":0,"CoveragePercent":0,"Children":[]}
            ]}
            """;
        var report = Read(Write(text), target);
        Assert.That(report.TargetAssembly is not null, Is.EqualTo(target == "Product"));
        Assert.That(report.Members.Count, Is.EqualTo(target == "Product" ? 1 : 0));
        if (report.TargetAssembly is { } assembly)
        {
            Assert.That(assembly.CoveredStatements, Is.Zero);
            Assert.That(assembly.TotalStatements, Is.Zero);
            Assert.That(assembly.Unresolved, Is.False);
        }
    }

    [Test]
    public void ZeroCoveredStatementsWithRealUncoveredStatementsRemainValid()
    {
        var json = Fixture();
        Product(json)["CoveredStatements"] = 0;
        Product(json)["CoveragePercent"] = 0;
        var report = Read(Write(json.ToJsonString()));
        Assert.That(report.TargetAssembly!.CoveredStatements, Is.Zero);
        Assert.That(report.TargetAssembly.TotalStatements, Is.EqualTo(6501));
    }

    [Test]
    public void CallerCanSelectATestOrToolAssemblyWithoutAProductNameHeuristic()
    {
        var report = Read(Write(Representative), "Other");
        Assert.That(report.TargetAssembly!.AssemblyName, Is.EqualTo("Other"));
        Assert.That(report.TargetAssembly.CoveredStatements, Is.EqualTo(1));
        Assert.That(report.Members, Has.Count.EqualTo(2));
        Assert.That(report.Members[1].Name, Is.EqualTo("Worker"));
        Assert.That(report.Members[1].SymbolSegments[0].Name, Is.EqualTo("Other"));
    }

    [Test]
    public void UnknownKindsAndTheirChildrenRemainVisibleAndUnresolved()
    {
        var json = Fixture();
        Method(json)["Kind"] = "FutureCompiledNode";
        Method(json).AsObject().Remove("Name");
        var report = Read(Write(json.ToJsonString()));
        var unknown = report.Members.Single(member => member.Kind == "FutureCompiledNode");
        Assert.That(unknown.Name, Is.Null);
        Assert.That(unknown.CoveredStatements, Is.EqualTo(18));
        Assert.That(unknown.UnresolvedReasons, Does.Contain("unknown-kind"));
        var descendant = report.Members.Single(member => member.Name == "MoveNext():void");
        Assert.That(descendant.UnresolvedReasons, Does.Contain("unknown-kind"));
        Assert.That(descendant.SymbolSegments[^2].Kind, Is.EqualTo("FutureCompiledNode"));
        Assert.That(report.Members, Has.Count.EqualTo(17));
    }

    [Test]
    public void DuplicateStateMachineSiblingNamesKeepDistinctRawPathsAndEveryDescendant()
    {
        var json = Fixture();
        var method = Method(json);
        method["Kind"] = "UnresolvedStateMachineMethod";
        var anonymous = JsonNode.Parse("""
            {"Kind":"AnonymousMethod","Name":"(Task):Task","CoveredStatements":0,"TotalStatements":10,"CoveragePercent":0,
             "Children":[{"Kind":"InternalCompiledMethod","Name":"MoveNext():void","CoveredStatements":0,"TotalStatements":10,"CoveragePercent":0}]}
            """)!;
        method["Children"] = new JsonArray(anonymous, anonymous.DeepClone());
        var report = Read(Write(json.ToJsonString()));
        var siblings = report.Members.Where(member => member.Name == "(Task):Task").ToArray();
        Assert.That(siblings, Has.Length.EqualTo(2));
        Assert.That(siblings[0].SymbolSegments, Is.EqualTo(siblings[1].SymbolSegments));
        Assert.That(siblings[0].RawPath, Is.Not.EqualTo(siblings[1].RawPath));
        Assert.That(siblings.All(member => member.UnresolvedReasons.Contains("ambiguous-sibling-symbol") &&
            member.UnresolvedReasons.Contains("unresolved-state-machine")), Is.True);
        var descendants = report.Members.Where(member => member.Name == "MoveNext():void").ToArray();
        Assert.That(descendants, Has.Length.EqualTo(2));
        Assert.That(descendants.All(member => member.UnresolvedReasons.Contains("ambiguous-sibling-symbol")), Is.True);
    }

    [TestCase("truncated", TestName = "aggregate/json-truncated")]
    [TestCase("malformed", TestName = "aggregate/json-malformed")]
    [TestCase("duplicate-root", TestName = "aggregate/json-duplicate-root-property")]
    [TestCase("duplicate-child", TestName = "aggregate/json-duplicate-child-property")]
    [TestCase("escaped-duplicate", TestName = "aggregate/json-escaped-duplicate-property")]
    [TestCase("trailing-content", TestName = "aggregate/json-trailing-content")]
    [TestCase("comment", TestName = "aggregate/json-comment")]
    public void InvalidOrDuplicateJsonCannotAcquireAnObservation(string fault)
    {
        var text = fault switch
        {
            "truncated" => Representative[..^1],
            "malformed" => Representative.Replace("\"Kind\": \"Root\"", "\"Kind\":", StringComparison.Ordinal),
            "duplicate-root" => Representative.Replace("\"Kind\": \"Root\"", "\"Kind\": \"Root\", \"Kind\": \"Root\"", StringComparison.Ordinal),
            "duplicate-child" => Representative.Replace("\"Name\": \"Run():Task\"", "\"Name\": \"Run():Task\", \"Name\": \"other\"", StringComparison.Ordinal),
            "escaped-duplicate" => Representative.Replace("\"Kind\": \"Root\"", "\"Kind\": \"Root\", \"\\u004bind\": \"Root\"", StringComparison.Ordinal),
            "trailing-content" => Representative + " {}",
            "comment" => "/* private-json-marker */" + Representative,
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        Reject(Write(text));
    }

    [TestCase("negative", TestName = "aggregate/count-negative")]
    [TestCase("greater-than-total", TestName = "aggregate/count-covered-exceeds-total")]
    [TestCase("fractional", TestName = "aggregate/count-fractional")]
    [TestCase("string-count", TestName = "aggregate/count-string")]
    [TestCase("missing-count", TestName = "aggregate/count-missing")]
    [TestCase("overflow", TestName = "aggregate/count-overflow")]
    [TestCase("identity-number", TestName = "aggregate/identity-number")]
    [TestCase("identity-null", TestName = "aggregate/identity-null")]
    [TestCase("identity-control", TestName = "aggregate/identity-control")]
    [TestCase("missing-method-name", TestName = "aggregate/identity-missing-method-name")]
    [TestCase("children-object", TestName = "aggregate/children-object")]
    [TestCase("child-null", TestName = "aggregate/child-null")]
    [TestCase("percent-negative", TestName = "aggregate/percent-negative")]
    [TestCase("percent-too-large", TestName = "aggregate/percent-over-100")]
    [TestCase("percent-fractional", TestName = "aggregate/percent-fractional")]
    [TestCase("unknown-property", TestName = "aggregate/unobserved-property")]
    [TestCase("nested-assembly", TestName = "aggregate/nested-assembly")]
    public void MalformedDescendantsCannotHideBehindValidAssemblyTotals(string fault)
    {
        var json = Fixture();
        var method = Method(json);
        switch (fault)
        {
            case "negative": method["CoveredStatements"] = -1; break;
            case "greater-than-total": method["CoveredStatements"] = 6499; break;
            case "fractional": method["TotalStatements"] = 1.5; break;
            case "string-count": method["TotalStatements"] = "6498"; break;
            case "missing-count": method.AsObject().Remove("TotalStatements"); break;
            case "overflow": method["TotalStatements"] = JsonNode.Parse("9223372036854775808"); break;
            case "identity-number": method["Name"] = 12; break;
            case "identity-null": method["Kind"] = null; break;
            case "identity-control": method["Name"] = "private\nmarker"; break;
            case "missing-method-name": method.AsObject().Remove("Name"); break;
            case "children-object": method["Children"] = new JsonObject(); break;
            case "child-null": method["Children"] = new JsonArray((JsonNode?)null); break;
            case "percent-negative": method["CoveragePercent"] = -1; break;
            case "percent-too-large": method["CoveragePercent"] = 101; break;
            case "percent-fractional": method["CoveragePercent"] = 0.5; break;
            case "unknown-property": method["SourceLine"] = 20; break;
            case "nested-assembly": method["Kind"] = "Assembly"; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
        Reject(Write(json.ToJsonString()));
    }

    [TestCase("Product", TestName = "aggregate/duplicate-target-assembly")]
    [TestCase("Other", TestName = "aggregate/duplicate-other-assembly")]
    public void DuplicateRootAssemblyIdentitiesAreRejectedEvenOutsideTheTarget(string duplicate)
    {
        var json = Fixture();
        var assemblies = json["Children"]!.AsArray();
        assemblies.Add(assemblies.Single(node => node!["Name"]!.GetValue<string>() == duplicate)!.DeepClone());
        Reject(Write(json.ToJsonString()));
    }

    [Test]
    public void MalformedNonTargetAssemblyIsStillRejected()
    {
        var json = Fixture();
        json["Children"]![0]!["Children"]![0]!["CoveredStatements"] = -1;
        Reject(Write(json.ToJsonString()));
    }

    [TestCase("wrong-kind", TestName = "aggregate/root-wrong-kind")]
    [TestCase("missing-children", TestName = "aggregate/root-missing-children")]
    [TestCase("named-root", TestName = "aggregate/root-unobserved-name")]
    [TestCase("non-assembly-child", TestName = "aggregate/root-non-assembly-child")]
    public void InvalidRootShapeCannotEstablishAReport(string fault)
    {
        var json = Fixture();
        switch (fault)
        {
            case "wrong-kind": json["Kind"] = "Assembly"; break;
            case "missing-children": json.AsObject().Remove("Children"); break;
            case "named-root": json["Name"] = "Product"; break;
            case "non-assembly-child": Product(json)["Kind"] = "Namespace"; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
        Reject(Write(json.ToJsonString()));
    }

    [Test]
    public void Utf8BomAndWhitespaceAreIncludedInTheDigestOfTheValidatedBytes()
    {
        var path = Path.Combine(_root, "aggregate.json");
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Representative + "\r\n ")).ToArray();
        File.WriteAllBytes(path, bytes);
        var report = Read(path);
        Assert.That(report.ReportSha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes))));
        Assert.That(report.ReportSha256, Is.Not.EqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Representative)))));
    }

    [Test]
    public void InvalidUtf8BytesCannotAcquireAValidatedReport()
    {
        var bytes = Encoding.UTF8.GetBytes(Representative);
        bytes[Array.IndexOf(bytes, (byte)'P')] = 0xff;
        var path = Path.Combine(_root, "aggregate.json");
        File.WriteAllBytes(path, bytes);
        Reject(path);
    }

    [Test]
    public void OversizedFilesAreRejectedBeforeParsing()
    {
        var path = Path.Combine(_root, "aggregate.json");
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) output.SetLength(64L * 1024 * 1024 + 1);
        Assert.That(Assert.Throws<DotCoverAggregateException>(() => Read(path))!.Code, Is.EqualTo("aggregate-size-limit"));
    }

    [Test]
    public void ExcessiveNestingIsRejectedWithAControlledDiagnostic()
    {
        var json = Fixture();
        var current = Method(json);
        for (var index = 0; index < 80; index++)
        {
            var nested = JsonNode.Parse("""
                {"Kind":"FutureNode","Name":"nested","CoveredStatements":0,"TotalStatements":0,"CoveragePercent":0}
                """)!;
            current["Children"] = new JsonArray(nested);
            current = nested;
        }
        // JsonNode's writer has its own limit; allow the fixture to reach the
        // parser's lower bound without relaxing the parser's production limit.
        Reject(Write(json.ToJsonString(new() { MaxDepth = 256 })));
    }

    [Test]
    public void ReportContentsCannotSelectTheirOwnToolVersionOrExposePrivateInputOnFailure()
    {
        var path = Write(Representative);
        var unsupported = Assert.Throws<DotCoverAggregateException>(() =>
            DotCoverAggregate.ReadValidated(path, "Product", "2026.2.2"))!;
        Assert.That(unsupported.Code, Is.EqualTo("unsupported-dotcover-version"));
        var json = Fixture();
        json["ToolVersion"] = DotCoverAggregate.SupportedToolVersion;
        var error = Reject(Write(json.ToJsonString(), "private-input-marker.json"));
        Assert.That(error.Message, Does.Not.Contain("private-input-marker").And.Not.Contain(_root).And.Not.Contain("Product"));
        Assert.That(error.InnerException, Is.Null);
    }

    [Test]
    public void RelativePathsAndMissingFilesCannotAcquireAnObservation()
    {
        Reject("private-relative-input.json");
        Reject(Path.Combine(_root, "missing-private-input.json"));
    }

    private static JsonNode Fixture() => JsonNode.Parse(Representative)!;
    private static JsonNode Product(JsonNode json) => json["Children"]![1]!;
    private static JsonNode Method(JsonNode json) => Product(json)["Children"]![0]!["Children"]![0]!["Children"]![2]!;
    private static DotCoverAggregateReport Read(string path, string target = "Product")
        => DotCoverAggregate.ReadValidated(path, target, DotCoverAggregate.SupportedToolVersion);

    private string Write(string text, string name = "aggregate.json")
    {
        var path = Path.Combine(_root, name);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(output, new UTF8Encoding(false));
        writer.Write(text);
        return path;
    }

    private static DotCoverAggregateException Reject(string path)
        => Assert.Throws<DotCoverAggregateException>(() => Read(path))!;
}
