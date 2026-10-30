using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FishAudio.Testing;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[NonParallelizable]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class ExecutionMetadataCommandTests
{
    private const string Prefix = "fa-metadata-command-";
    private string _root = "";

    [SetUp]
    public void CreateOwnedDirectory()
    {
        _root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void DeleteOwnedDirectory()
    {
        if (_root.Length == 0) return;
        try
        {
            var path = Path.GetFullPath(_root);
            var name = Path.GetFileName(path);
            if (!string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
                    StringComparison.OrdinalIgnoreCase) || !name.StartsWith(Prefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(name[Prefix.Length..], "N", out _) ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("metadata-command-fixture-owner-changed");
            foreach (var entry in Directory.GetFileSystemEntries(path))
                if ((File.GetAttributes(entry) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new InvalidOperationException("metadata-command-fixture-entry-changed");
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
            {
                TestContext.Progress.WriteLine("metadata-command: cleanup failed; original failure and fixture retained.");
                return;
            }
            throw new InvalidOperationException("metadata-command-fixture-cleanup-failed");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-metadata-command/current-raw-discovery")]
    public void TheCommandExportsPoliciesBoundToTheUnchangedRawDiscovery()
    {
        var path = WriteDiscovery(invalidProfile: false);
        var bytes = File.ReadAllBytes(path);
        var output = Path.Combine(_root, "policies.json");
        var args = new[] { "evidence-execution-metadata", "--discovery", path, "--output", output };
        Assert.That(NUnitEvidence.ReadCatalog(path).Count, Is.EqualTo(1));
        Assert.That(EvidenceCommands.IsInvocation(args), Is.True, "execution-metadata-command-entry-required");
        Assert.That(Invoke(args).ExitCode, Is.Zero);

        using var document = JsonDocument.Parse(File.ReadAllBytes(output));
        var root = document.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("nunit-execution-metadata"));
            Assert.That(root.GetProperty("complete").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("discoverySha256").GetString(), Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes))));
            Assert.That(root.GetProperty("cases").GetArrayLength(), Is.EqualTo(1));
            Assert.That(root.GetProperty("cases")[0].GetProperty("case").GetProperty("canonicalId").GetString(), Is.EqualTo("example/case"));
            Assert.That(root.GetProperty("cases")[0].GetProperty("freshHost").GetBoolean(), Is.True);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
        });
    }

    [Test]
    [Property("FishAudioCaseId", "infra.execution-metadata-command/rejected-policy")]
    public void ARejectedPolicyReturnsFailureWithoutPublishingACompleteDocument()
    {
        var path = WriteDiscovery(invalidProfile: true);
        var output = Path.Combine(_root, "policies.json");
        var args = new[] { "evidence-execution-metadata", "--discovery", path, "--output", output };
        Assert.That(NUnitEvidence.ReadCatalog(path).Count, Is.EqualTo(1));
        Assert.That(EvidenceCommands.IsInvocation(args), Is.True, "execution-metadata-command-entry-required");

        var result = Invoke(args);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Error, Is.EqualTo("execution-metadata: execution-metadata-invalid-profile" + Environment.NewLine));
            Assert.That(File.Exists(output), Is.False);
        });
    }

    private string WriteDiscovery(bool invalidProfile)
    {
        var properties = new XElement("properties", new XElement("property", new XAttribute("name", "FishAudioCaseId"),
            new XAttribute("value", "example/case")), new XElement("property", new XAttribute("name", "FishAudioFreshHost"),
            new XAttribute("value", "true")));
        if (invalidProfile)
            properties.Add(new XElement("property", new XAttribute("name", "FishAudioExecutionProfile"), new XAttribute("value", "invalid/profile")),
                new XElement("property", new XAttribute("name", "FishAudioProfileOrdinal"), new XAttribute("value", "0")));
        var leaf = new XElement("test-case", new XAttribute("id", "0-1"), new XAttribute("name", "Case"),
            new XAttribute("fullname", "Example.Cases.Case"), new XAttribute("classname", "Example.Cases"),
            new XAttribute("methodname", "Case"), new XAttribute("runstate", "Runnable"), properties);
        var fixture = new XElement("test-suite", new XAttribute("type", "TestFixture"), new XAttribute("id", "0-2"),
            new XAttribute("name", "Cases"), new XAttribute("fullname", "Example.Cases"), new XAttribute("runstate", "Runnable"),
            new XAttribute("testcasecount", "1"), leaf);
        var run = new XElement("test-run", new XAttribute("id", "0"), new XAttribute("name", "example.dll"),
            new XAttribute("runstate", "Runnable"), new XAttribute("testcasecount", "1"), fixture);
        var path = Path.Combine(_root, "discovery.dump");
        File.WriteAllText(path, new XElement("NUnitXml", run).ToString(SaveOptions.DisableFormatting), new UTF8Encoding(false));
        return path;
    }

    private static (int ExitCode, string Error) Invoke(string[] args)
    {
        var previousError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            var exitCode = EvidenceCommands.Run(args);
            return (exitCode, error.ToString());
        }
        finally { Console.SetError(previousError); }
    }
}
