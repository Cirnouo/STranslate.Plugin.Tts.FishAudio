using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FishAudio.Testing.Evidence;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace FishAudio.Testing.Tests;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class CoverageCiTransportTests
{
    private string _root = "";
    private string _owner = "";
    private string _archive = "";
    private CoverageCiTransportExpectation _expected = null!;
    private const string Tree = "eb1bd2b0ae178f769a41111781e174abc026ce36";
    private static readonly byte[] Raw = [0, 255, 13, 10, 10, 128, 65, 0];

    [SetUp]
    public void PrepareOwnedBytes()
    {
        _root = Path.Combine(Path.GetTempPath(), "fa-coverage-transport-" + Guid.NewGuid().ToString("N"));
        _owner = Path.Combine(_root, "owner");
        _archive = Path.Combine(_root, "operation.zip");
        Directory.CreateDirectory(Path.Combine(_owner, "source", ".config"));
        Directory.CreateDirectory(Path.Combine(_owner, "source", ".artifacts", "coverage", "resources", "日本語"));
        Directory.CreateDirectory(Path.Combine(_owner, "source", "empty"));
        File.WriteAllBytes(Path.Combine(_owner, "source", ".config", "tool.json"), Raw);
        File.WriteAllBytes(Path.Combine(_owner, "source", ".artifacts", "coverage", "resources", "日本語", "support.exe"), Raw.Reverse().ToArray());
        File.WriteAllText(Path.Combine(_owner, "source", "request.json"), "{\"original\":\"" + _owner.Replace("\\", "\\\\") + "\"}\r\n", new UTF8Encoding(false));
        File.SetAttributes(Path.Combine(_owner, "source", ".config", "tool.json"), FileAttributes.Hidden);
        _expected = new(1, _owner, Path.Combine(_owner, "source"), Tree);
    }

    [TestCase("inside", TestName = "coverage-transport/complete-hidden-unicode-support-tree-roundtrip")]
    [TestCase("outside", TestName = "coverage-transport/shard-owner-keeps-external-original-source-root")]
    public void SameOriginalRootRoundTripPreservesAllBytesAndEmptyDirectories(string sourceLocation)
    {
        if (sourceLocation == "outside") _expected = _expected with { SourceDirectory = Path.Combine(_root, "separate-source") };
        var before = Bytes(_owner);
        var packed = CoverageCiTransport.Pack(new(1, _owner, _archive), _expected);
        MoveOwnerAside();
        var restored = CoverageCiTransport.Restore(new(1, _archive, packed.ArchiveSha256), _expected);
        Assert.Multiple(() =>
        {
            Assert.That(Bytes(_owner).Keys, Is.EquivalentTo(before.Keys));
            foreach (var pair in before) Assert.That(Bytes(_owner)[pair.Key], Is.EqualTo(pair.Value), pair.Key);
            Assert.That(Directory.Exists(Path.Combine(_owner, "source", "empty")), Is.True);
            Assert.That(restored.ArchiveSha256, Is.EqualTo(packed.ArchiveSha256));
            Assert.That(restored.SourceDirectory, Is.EqualTo(_expected.SourceDirectory));
            Assert.That(restored.ByteTransportComplete, Is.True);
            Assert.That(restored.SourceAuthenticated || restored.BuildAuthenticated || restored.TestExecutionVerified ||
                restored.MainProvenanceVerified || restored.CoverageUnionComplete || restored.CompleteBaseline ||
                restored.PublicationAuthorized || restored.TiaSelectionAuthorized, Is.False);
        });
    }

    [Test]
    public void WrongIndependentArchiveDigestRejectsBeforeCreatingAStage()
    {
        _ = PackAndRemoveOwner();
        var before = Directory.GetDirectories(_root);
        Assert.That(Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.Restore(new(1, _archive, new('0', 64)), _expected))!.Code,
            Is.EqualTo("coverage-ci-transport-archive-digest"));
        Assert.That(Directory.GetDirectories(_root), Is.EquivalentTo(before));
        Assert.That(Directory.Exists(_owner), Is.False);
    }

    [TestCase("root", TestName = "coverage-transport/reject-original-owner-root-mismatch")]
    [TestCase("source", TestName = "coverage-transport/reject-original-source-root-mismatch")]
    [TestCase("tree", TestName = "coverage-transport/reject-caller-tree-mismatch")]
    public void DownloadedManifestCannotChooseIdentityOrDestination(string mutation)
    {
        var receipt = PackAndRemoveOwner();
        var wrong = mutation switch
        {
            "root" => _expected with { OriginalRoot = _owner + "-foreign" },
            "source" => _expected with { SourceDirectory = Path.Combine(_root, "foreign-source") },
            _ => _expected with { SourceTree = new('a', 40) },
        };
        Assert.That(Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.Restore(new(1, _archive, receipt.ArchiveSha256), wrong))!.Code,
            Is.EqualTo("coverage-ci-transport-manifest-identity"));
        Assert.That(Directory.Exists(_owner) || Directory.Exists(_owner + "-foreign"), Is.False);
    }

    [TestCase("../escape", TestName = "coverage-transport/reject-parent-traversal")]
    [TestCase("C:/escape", TestName = "coverage-transport/reject-absolute-entry")]
    [TestCase("text:stream", TestName = "coverage-transport/reject-alternate-stream")]
    [TestCase("CON", TestName = "coverage-transport/reject-device-name")]
    [TestCase("file~1", TestName = "coverage-transport/reject-short-path-alias")]
    public void InvalidManifestRelativePathRejectsBeforeExtraction(string path)
    {
        _ = PackAndRemoveOwner();
        Rewrite(items =>
        {
            var manifest = JsonNode.Parse(items.Single(item => item.Name == "transport-manifest.json").Bytes)!;
            var first = manifest["files"]![0]!;
            var old = first["path"]!.GetValue<string>();
            first["path"] = path;
            items[items.FindIndex(item => item.Name == "payload/" + old)] = new("payload/" + path, Raw, 0);
            var ordered = manifest["files"]!.AsArray().Select(item => item!.DeepClone())
                .OrderBy(item => item["path"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
            manifest["files"] = new JsonArray(ordered);
            ReplaceManifest(items, manifest);
        });
        RejectRehashedArchiveBeforeDestination(path == "C:/escape" ? "relative-path" : path == "CON" ? "device-path" : "path-component");
    }

    [TestCase("missing", TestName = "coverage-transport/reject-missing-zip-member")]
    [TestCase("extra", TestName = "coverage-transport/reject-extra-zip-member")]
    [TestCase("case-alias", TestName = "coverage-transport/reject-case-colliding-zip-member")]
    [TestCase("symlink", TestName = "coverage-transport/reject-zip-symlink-attributes")]
    [TestCase("bytes", TestName = "coverage-transport/reject-payload-byte-drift")]
    [TestCase("truncated", TestName = "coverage-transport/reject-payload-length-drift")]
    public void ValidArchiveDigestDoesNotReplaceExactMemberAndByteAdmission(string mutation)
    {
        _ = PackAndRemoveOwner();
        Rewrite(items =>
        {
            var index = items.FindIndex(item => item.Name.StartsWith("payload/", StringComparison.Ordinal) && !item.Name.EndsWith('/'));
            var file = items[index];
            switch (mutation)
            {
                case "missing": items.RemoveAt(index); break;
                case "extra": items.Add(new("payload/unlisted.bin", Raw, 0)); break;
                case "case-alias": items.Add(file with { Name = file.Name.ToUpperInvariant() }); break;
                case "symlink": items[index] = file with { Attributes = unchecked((int)0xa1ff0000) }; break;
                case "bytes": items[index] = file with { Bytes = file.Bytes.Select(value => (byte)(value ^ 1)).ToArray() }; break;
                default: items[index] = file with { Bytes = file.Bytes[..^1] }; break;
            }
        });
        RejectRehashedArchiveBeforeDestination(mutation switch
        {
            "missing" or "truncated" => "file-entry", "extra" => "entry-set", "case-alias" => "duplicate-entry",
            "symlink" => "entry-attributes", _ => "payload-bytes",
        });
    }

    [Test]
    public void OccupiedOriginalRootIsNeverOverwritten()
    {
        var packed = CoverageCiTransport.Pack(new(1, _owner, _archive), _expected);
        var before = Bytes(_owner);
        Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.Restore(new(1, _archive, packed.ArchiveSha256), _expected));
        foreach (var pair in before) Assert.That(Bytes(_owner)[pair.Key], Is.EqualTo(pair.Value));
    }

    [Test]
    public void PackCannotPlaceItsArchiveInsideTheTreeBeingCaptured()
    {
        var archive = Path.Combine(_owner, "self.zip");
        Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.Pack(new(1, _owner, archive), _expected));
        Assert.That(File.Exists(archive), Is.False);
    }

    [Test]
    public void ParticipatingTransportUsesTheExistingSourceKeyedLease()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fa-full-coverage-source-leases");
        Directory.CreateDirectory(directory);
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_expected.SourceDirectory.ToUpperInvariant())));
        using var lease = new FileStream(Path.Combine(directory, key + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.Pack(new(1, _owner, _archive), _expected));
        Assert.That(File.Exists(_archive), Is.False);
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    public void CliPreflightsReceiptBeforePackingAndNeverReplacesIt()
    {
        var input = Path.Combine(_root, "request.json");
        var expectation = Path.Combine(_root, "expected.json");
        var receipt = Path.Combine(_root, "receipt.json");
        File.WriteAllBytes(input, JsonSerializer.SerializeToUtf8Bytes(new CoverageCiTransportPackInput(1, _owner, _archive), CoverageCiTransport.JsonOptions));
        File.WriteAllBytes(expectation, JsonSerializer.SerializeToUtf8Bytes(_expected, CoverageCiTransport.JsonOptions));
        File.WriteAllBytes(receipt, Raw);
        var prior = Console.Error;
        using var errors = new StringWriter();
        try
        {
            Console.SetError(errors);
            Assert.That(CoverageCiTransportCommands.Run(["coverage-ci-transport-pack", "--input", input,
                "--expectation", expectation, "--output", receipt]), Is.Not.Zero);
        }
        finally { Console.SetError(prior); }
        Assert.That(File.ReadAllBytes(receipt), Is.EqualTo(Raw));
        Assert.That(File.Exists(_archive), Is.False);
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    public void CliPackAndRestorePublishFreshByteOnlyReceipts()
    {
        var input = Path.Combine(_root, "request.json");
        var expectation = Path.Combine(_root, "expected.json");
        var packedReceipt = Path.Combine(_root, "packed.json");
        var restoredReceipt = Path.Combine(_root, "restored.json");
        File.WriteAllBytes(input, JsonSerializer.SerializeToUtf8Bytes(new CoverageCiTransportPackInput(1, _owner, _archive), CoverageCiTransport.JsonOptions));
        File.WriteAllBytes(expectation, JsonSerializer.SerializeToUtf8Bytes(_expected, CoverageCiTransport.JsonOptions));
        Assert.That(RunCli("coverage-ci-transport-pack", input, expectation, packedReceipt), Is.Zero);
        using var packed = JsonDocument.Parse(File.ReadAllBytes(packedReceipt));
        var digest = packed.RootElement.GetProperty("archiveSha256").GetString()!;
        MoveOwnerAside();
        // Replacing this held input after return also requires the CLI's guard
        // release, instead of leaving an otherwise green result holding it open.
        File.WriteAllBytes(input, JsonSerializer.SerializeToUtf8Bytes(new CoverageCiTransportRestoreInput(1, _archive, digest), CoverageCiTransport.JsonOptions));
        Assert.That(RunCli("coverage-ci-transport-restore", input, expectation, restoredReceipt), Is.Zero);
        using var restored = JsonDocument.Parse(File.ReadAllBytes(restoredReceipt));
        Assert.Multiple(() =>
        {
            Assert.That(restored.RootElement.GetProperty("operation").GetString(), Is.EqualTo("Restore"));
            Assert.That(restored.RootElement.GetProperty("originalRoot").GetString(), Is.EqualTo(_owner));
            Assert.That(restored.RootElement.GetProperty("archiveSha256").GetString(), Is.EqualTo(digest));
            Assert.That(restored.RootElement.GetProperty("byteTransportComplete").GetBoolean(), Is.True);
            foreach (var claim in new[] { "sourceAuthenticated", "buildAuthenticated", "testExecutionVerified", "mainProvenanceVerified",
                         "coverageUnionComplete", "completeBaseline", "publicationAuthorized", "tiaSelectionAuthorized" })
                Assert.That(restored.RootElement.GetProperty(claim).GetBoolean(), Is.False, claim);
            Assert.That(File.ReadAllBytes(Path.Combine(_owner, "source", ".config", "tool.json")), Is.EqualTo(Raw));
        });
    }

    [TestCase("duplicate-json", TestName = "coverage-transport/reject-duplicate-manifest-property")]
    [TestCase("truncated-archive", TestName = "coverage-transport/reject-truncated-zip-container")]
    public void MalformedContainerRejectsBeforeCreatingAStage(string mutation)
    {
        _ = PackAndRemoveOwner();
        if (mutation == "duplicate-json")
            Rewrite(items =>
            {
                var index = items.FindIndex(item => item.Name == "transport-manifest.json");
                items[index] = items[index] with { Bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(items[index].Bytes).Insert(1, "\"schemaVersion\":1,")) };
            });
        else
        {
            AssertOwned();
            var bytes = File.ReadAllBytes(_archive);
            File.WriteAllBytes(_archive, bytes[..(bytes.Length / 2)]);
        }
        var before = Directory.GetDirectories(_root);
        var error = Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.Restore(new(1, _archive,
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_archive)))), _expected))!;
        Assert.That(error.Code, Is.EqualTo("coverage-ci-transport-" +
            (mutation == "duplicate-json" ? "duplicate-json-property" : "io-or-invalid-data")));
        Assert.That(Directory.GetDirectories(_root), Is.EquivalentTo(before));
        Assert.That(Directory.Exists(_owner), Is.False);
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    public void FailedRestoreKeepsPartialEvidenceAndCannotPublishAFinalReceipt()
    {
        _ = PackAndRemoveOwner();
        Rewrite(items =>
        {
            var index = items.FindIndex(item => item.Name.StartsWith("payload/", StringComparison.Ordinal) && !item.Name.EndsWith('/'));
            items[index] = items[index] with { Bytes = items[index].Bytes.Select(value => (byte)(value ^ 1)).ToArray() };
        });
        var input = Path.Combine(_root, "restore-request.json");
        var expectation = Path.Combine(_root, "restore-expected.json");
        var receipt = Path.Combine(_root, "failed-receipt.json");
        File.WriteAllBytes(input, JsonSerializer.SerializeToUtf8Bytes(new CoverageCiTransportRestoreInput(1, _archive,
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_archive)))), CoverageCiTransport.JsonOptions));
        File.WriteAllBytes(expectation, JsonSerializer.SerializeToUtf8Bytes(_expected, CoverageCiTransport.JsonOptions));
        Assert.That(RunCli("coverage-ci-transport-restore", input, expectation, receipt), Is.Not.Zero);
        Assert.That(File.Exists(receipt) || Directory.Exists(_owner), Is.False);
        Assert.That(Directory.GetDirectories(_root).Any(path => Path.GetFileName(path).StartsWith(".owner.restore-", StringComparison.Ordinal)), Is.True);
        Assert.That(File.Exists(_archive) && Directory.Exists(Path.Combine(_root, "retained-original")), Is.True);
    }

    [TestCase("inside", TestName = "coverage-transport-v2/exact-subsecond-times-and-bytes-roundtrip")]
    [TestCase("outside", TestName = "coverage-transport-v2/external-original-source-keeps-exact-file-times")]
    public void V2RoundTripPreservesExactFileTimesWithoutChangingTheOriginal(string sourceLocation)
    {
        if (sourceLocation == "outside") _expected = _expected with { SourceDirectory = Path.Combine(_root, "separate-source") };
        var times = StampOwner();
        var bytes = Bytes(_owner);
        var expected = _expected with { SchemaVersion = 2 };
        var packed = CoverageCiTransport.PackV2(new(2, _owner, _archive), expected);
        var inspected = CoverageCiTransport.InspectV2(new(2, _archive, packed.ArchiveSha256), expected);
        Assert.Multiple(() =>
        {
            Assert.That(inspected.Manifest.SchemaVersion, Is.EqualTo(2));
            Assert.That(inspected.Manifest.FileTimes.Select(item => item.Path), Is.EquivalentTo(times.Keys));
            foreach (var item in inspected.Manifest.FileTimes)
                Assert.That(item.LastWriteTimeUtcTicks, Is.EqualTo(times[item.Path]), item.Path);
            foreach (var item in times)
                Assert.That(File.GetLastWriteTimeUtc(Path.Combine(_owner, item.Key)).Ticks, Is.EqualTo(item.Value), item.Key);
        });
        MoveOwnerAside();
        var restored = CoverageCiTransport.RestoreV2(new(2, _archive, packed.ArchiveSha256), expected);
        Assert.Multiple(() =>
        {
            Assert.That(restored.SchemaVersion, Is.EqualTo(2));
            Assert.That(restored.ByteTransportComplete, Is.True);
            foreach (var item in bytes)
            {
                Assert.That(File.ReadAllBytes(Path.Combine(_owner, item.Key)), Is.EqualTo(item.Value), item.Key);
                Assert.That(File.GetLastWriteTimeUtc(Path.Combine(_owner, item.Key)).Ticks, Is.EqualTo(times[item.Key]), item.Key);
                Assert.That(File.GetLastWriteTimeUtc(Path.Combine(_root, "retained-original", item.Key)).Ticks, Is.EqualTo(times[item.Key]), item.Key);
            }
            Assert.That(restored.SourceAuthenticated || restored.BuildAuthenticated || restored.TestExecutionVerified ||
                restored.MainProvenanceVerified || restored.CoverageUnionComplete || restored.CompleteBaseline ||
                restored.PublicationAuthorized || restored.TiaSelectionAuthorized, Is.False);
        });
        using var zip = ZipFile.OpenRead(_archive);
        using var manifestStream = zip.GetEntry("transport-manifest.json")!.Open();
        using var document = JsonDocument.Parse(manifestStream);
        foreach (var file in document.RootElement.GetProperty("files").EnumerateArray())
            Assert.That(file.EnumerateObject().Select(item => item.Name), Is.EquivalentTo(new[] { "path", "length", "sha256" }));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-transport-v2/v1-remains-byte-only-without-file-time-metadata")]
    public void V1StillTransportsBytesWithoutRestoringTimestampMetadata()
    {
        var times = StampOwner();
        var packed = CoverageCiTransport.Pack(new(1, _owner, _archive), _expected);
        using (var zip = ZipFile.OpenRead(_archive))
        using (var stream = zip.GetEntry("transport-manifest.json")!.Open())
        using (var document = JsonDocument.Parse(stream))
        {
            Assert.That(document.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(document.RootElement.TryGetProperty("fileTimes", out _), Is.False);
        }
        MoveOwnerAside();
        _ = CoverageCiTransport.Restore(new(1, _archive, packed.ArchiveSha256), _expected);
        var relative = "source/.config/tool.json";
        Assert.That(File.ReadAllBytes(Path.Combine(_owner, relative)), Is.EqualTo(Raw));
        Assert.That(File.GetLastWriteTimeUtc(Path.Combine(_owner, relative)).Ticks, Is.Not.EqualTo(times[relative]));
        Assert.That(File.GetLastWriteTimeUtc(Path.Combine(_root, "retained-original", relative)).Ticks, Is.EqualTo(times[relative]));
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-transport-v2/v1-rejects-added-time-table")]
    public void V1ArchiveSchemaDoesNotSilentlyAcceptV2TimeMetadata()
    {
        _ = PackAndRemoveOwner();
        Rewrite(items =>
        {
            var manifest = JsonNode.Parse(items.Single(item => item.Name == "transport-manifest.json").Bytes)!;
            manifest["fileTimes"] = new JsonArray();
            ReplaceManifest(items, manifest);
        });
        RejectRehashedArchiveBeforeDestination("io-or-invalid-data");
    }

    [TestCaseSource(nameof(V2TimeMetadataFaultCases))]
    public void V2TimeMetadataMustBeCompleteExactAndRepresentableBeforeExtraction(string mutation, string code)
    {
        var packed = PackV2AndRetainOwner();
        var before = Directory.GetDirectories(_root);
        Rewrite(items =>
        {
            var manifest = JsonNode.Parse(items.Single(item => item.Name == "transport-manifest.json").Bytes)!;
            var times = manifest["fileTimes"]!.AsArray();
            switch (mutation)
            {
                case "missing-table": manifest.AsObject().Remove("fileTimes"); break;
                case "null-table": manifest["fileTimes"] = null; break;
                case "missing-row": times.RemoveAt(0); break;
                case "extra-row": times.Add(new JsonObject { ["path"] = "unlisted.bin", ["lastWriteTimeUtcTicks"] = 638713838451234567L }); break;
                case "duplicate-row": times[1] = times[0]!.DeepClone(); break;
                case "case-alias": times[0]!["path"] = times[0]!["path"]!.GetValue<string>().ToUpperInvariant(); break;
                case "parent-path": times[0]!["path"] = "../escape"; break;
                case "missing-ticks": times[0]!.AsObject().Remove("lastWriteTimeUtcTicks"); break;
                case "null-ticks": times[0]!["lastWriteTimeUtcTicks"] = null; break;
                case "negative": times[0]!["lastWriteTimeUtcTicks"] = -1L; break;
                case "before-filetime": times[0]!["lastWriteTimeUtcTicks"] = 504911231999999999L; break;
                case "after-datetime": times[0]!["lastWriteTimeUtcTicks"] = 3155378976000000000L; break;
                case "fractional": times[0]!["lastWriteTimeUtcTicks"] = 638713838451234567.5m; break;
                case "string": times[0]!["lastWriteTimeUtcTicks"] = "638713838451234567"; break;
                case "unknown-root": manifest["timePolicy"] = true; break;
                case "unknown-row": times[0]!["zone"] = "UTC"; break;
                case "duplicate-json": break;
                default: throw new InvalidOperationException("unknown-time-metadata-fixture");
            }
            ReplaceManifest(items, manifest);
            if (mutation == "duplicate-json")
            {
                var index = items.FindIndex(item => item.Name == "transport-manifest.json");
                var text = Encoding.UTF8.GetString(items[index].Bytes);
                var position = text.IndexOf("\"lastWriteTimeUtcTicks\":", StringComparison.Ordinal);
                Assert.That(position, Is.GreaterThan(0));
                items[index] = items[index] with { Bytes = Encoding.UTF8.GetBytes(text.Insert(position, "\"lastWriteTimeUtcTicks\":638713838451234567,")) };
            }
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_archive)));
        Assert.That(digest, Is.Not.EqualTo(packed.ArchiveSha256));
        Assert.That(Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.RestoreV2(new(2, _archive, digest),
            _expected with { SchemaVersion = 2 }))!.Code, Is.EqualTo("coverage-ci-transport-" + code));
        Assert.That(Directory.GetDirectories(_root), Is.EquivalentTo(before), "invalid metadata must reject before creating a stage");
        Assert.That(Directory.Exists(_owner), Is.False);
        Assert.That(Directory.Exists(Path.Combine(_root, "retained-original")), Is.True);
    }

    private static IEnumerable<TestCaseData> V2TimeMetadataFaultCases()
    {
        foreach (var row in new[]
        {
            ("missing-table", "manifest-file-times", "coverage-transport-v2/reject-missing-time-table"),
            ("null-table", "manifest-file-times", "coverage-transport-v2/reject-null-time-table"),
            ("missing-row", "manifest-file-times", "coverage-transport-v2/reject-incomplete-time-table"),
            ("extra-row", "manifest-file-times", "coverage-transport-v2/reject-extra-time-path"),
            ("duplicate-row", "manifest-file-times", "coverage-transport-v2/reject-duplicate-time-path"),
            ("case-alias", "manifest-file-times", "coverage-transport-v2/reject-case-aliased-time-path"),
            ("parent-path", "manifest-file-times", "coverage-transport-v2/reject-time-path-outside-file-set"),
            ("missing-ticks", "manifest-file-time", "coverage-transport-v2/reject-missing-ticks"),
            ("null-ticks", "io-or-invalid-data", "coverage-transport-v2/reject-null-ticks"),
            ("negative", "manifest-file-time", "coverage-transport-v2/reject-negative-time"),
            ("before-filetime", "manifest-file-time", "coverage-transport-v2/reject-pre-windows-filetime"),
            ("after-datetime", "manifest-file-time", "coverage-transport-v2/reject-unrepresentable-time"),
            ("fractional", "io-or-invalid-data", "coverage-transport-v2/reject-fractional-ticks"),
            ("string", "io-or-invalid-data", "coverage-transport-v2/reject-string-ticks"),
            ("unknown-root", "io-or-invalid-data", "coverage-transport-v2/reject-unknown-time-policy"),
            ("unknown-row", "io-or-invalid-data", "coverage-transport-v2/reject-unknown-time-row-field"),
            ("duplicate-json", "duplicate-json-property", "coverage-transport-v2/reject-duplicate-time-row-property"),
        }) yield return new TestCaseData(row.Item1, row.Item2).SetName(row.Item3).SetProperty("FishAudioCaseId", row.Item3);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-transport-v2/timestamps-cannot-authorize-corrupt-bytes")]
    public void V2ValidTimeMetadataCannotAuthorizeCorruptPayloadBytes()
    {
        _ = PackV2AndRetainOwner();
        Rewrite(items =>
        {
            var index = items.FindIndex(item => item.Name.StartsWith("payload/", StringComparison.Ordinal) && !item.Name.EndsWith('/'));
            items[index] = items[index] with { Bytes = items[index].Bytes.Select(value => (byte)(value ^ 1)).ToArray() };
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_archive)));
        Assert.That(Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.RestoreV2(new(2, _archive, digest),
            _expected with { SchemaVersion = 2 }))!.Code, Is.EqualTo("coverage-ci-transport-payload-bytes"));
        Assert.That(Directory.Exists(_owner), Is.False);
        Assert.That(Directory.GetDirectories(_root).Any(path => Path.GetFileName(path).StartsWith(".owner.restore-", StringComparison.Ordinal)), Is.True);
        Assert.That(Directory.Exists(Path.Combine(_root, "retained-original")) && File.Exists(_archive), Is.True);
    }

    [TestCase(1, 2, "manifest-identity", TestName = "coverage-transport-v2/v2-entry-refuses-v1-byte-archive")]
    [TestCase(2, 1, "io-or-invalid-data", TestName = "coverage-transport-v2/v1-entry-refuses-v2-time-archive")]
    public void ExplicitEntryVersionMustMatchTheActualArchive(int archiveVersion, int entryVersion, string code)
    {
        var packed = archiveVersion == 1 ? PackAndRemoveOwner() : PackV2AndRetainOwner();
        var input = new CoverageCiTransportRestoreInput(entryVersion, _archive, packed.ArchiveSha256);
        var expected = _expected with { SchemaVersion = entryVersion };
        var before = Directory.GetDirectories(_root);
        Assert.That(Assert.Throws<CoverageCiTransportException>(() =>
            _ = entryVersion == 1 ? CoverageCiTransport.Restore(input, expected) : CoverageCiTransport.RestoreV2(input, expected))!.Code,
            Is.EqualTo("coverage-ci-transport-" + code));
        Assert.That(Directory.GetDirectories(_root), Is.EquivalentTo(before));
        Assert.That(Directory.Exists(_owner), Is.False);
    }

    [Test]
    [Property("FishAudioCaseId", "coverage-transport-v2/occupied-original-root-never-rewritten")]
    public void V2CannotOverwriteAnOccupiedOriginalRootOrItsTimes()
    {
        var times = StampOwner();
        var bytes = Bytes(_owner);
        var expected = _expected with { SchemaVersion = 2 };
        var packed = CoverageCiTransport.PackV2(new(2, _owner, _archive), expected);
        Assert.That(Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.RestoreV2(new(2, _archive, packed.ArchiveSha256), expected))!.Code,
            Is.EqualTo("coverage-ci-transport-destination-occupied"));
        foreach (var item in times)
        {
            Assert.That(File.GetLastWriteTimeUtc(Path.Combine(_owner, item.Key)).Ticks, Is.EqualTo(item.Value), item.Key);
            Assert.That(File.ReadAllBytes(Path.Combine(_owner, item.Key)), Is.EqualTo(bytes[item.Key]), item.Key);
        }
    }

    [Test]
    [NonParallelizable]
    [Property("FishAudioExclusiveResource", "console")]
    [Property("FishAudioCaseId", "coverage-transport-v2/explicit-cli-preserves-times-and-keeps-authority-false")]
    public void ExplicitV2CliPublishesTimeAwareInspectionAndFreshRestoreReceipts()
    {
        var times = StampOwner();
        var input = Path.Combine(_root, "v2-request.json");
        var expectation = Path.Combine(_root, "v2-expected.json");
        var packedReceipt = Path.Combine(_root, "v2-packed.json");
        var inspectedReceipt = Path.Combine(_root, "v2-inspected.json");
        var restoredReceipt = Path.Combine(_root, "v2-restored.json");
        var expected = _expected with { SchemaVersion = 2 };
        File.WriteAllBytes(input, JsonSerializer.SerializeToUtf8Bytes(new CoverageCiTransportPackInput(2, _owner, _archive), CoverageCiTransport.JsonOptions));
        File.WriteAllBytes(expectation, JsonSerializer.SerializeToUtf8Bytes(expected, CoverageCiTransport.JsonOptions));
        Assert.That(RunCli("coverage-ci-transport-pack-v2", input, expectation, packedReceipt), Is.Zero);
        using var packed = JsonDocument.Parse(File.ReadAllBytes(packedReceipt));
        var digest = packed.RootElement.GetProperty("archiveSha256").GetString()!;
        File.WriteAllBytes(input, JsonSerializer.SerializeToUtf8Bytes(new CoverageCiTransportRestoreInput(2, _archive, digest), CoverageCiTransport.JsonOptions));
        Assert.That(RunCli("coverage-ci-transport-inspect-v2", input, expectation, inspectedReceipt), Is.Zero);
        using var inspected = JsonDocument.Parse(File.ReadAllBytes(inspectedReceipt));
        Assert.That(inspected.RootElement.GetProperty("manifest").GetProperty("fileTimes").GetArrayLength(), Is.EqualTo(times.Count));
        MoveOwnerAside();
        Assert.That(RunCli("coverage-ci-transport-restore-v2", input, expectation, restoredReceipt), Is.Zero);
        using var restored = JsonDocument.Parse(File.ReadAllBytes(restoredReceipt));
        Assert.That(restored.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(2));
        foreach (var item in times)
            Assert.That(File.GetLastWriteTimeUtc(Path.Combine(_owner, item.Key)).Ticks, Is.EqualTo(item.Value), item.Key);
        foreach (var claim in new[] { "sourceAuthenticated", "buildAuthenticated", "testExecutionVerified", "mainProvenanceVerified",
                     "coverageUnionComplete", "completeBaseline", "publicationAuthorized", "tiaSelectionAuthorized" })
            Assert.That(restored.RootElement.GetProperty(claim).GetBoolean(), Is.False, claim);
    }

    private Dictionary<string, long> StampOwner()
    {
        var first = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks + 1_234_567;
        var times = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["source/.config/tool.json"] = first,
            ["source/.artifacts/coverage/resources/日本語/support.exe"] = first + 17,
            ["source/request.json"] = first + 29,
        };
        foreach (var item in times)
        {
            File.SetLastWriteTimeUtc(Path.Combine(_owner, item.Key), new DateTime(item.Value, DateTimeKind.Utc));
            Assert.That(File.GetLastWriteTimeUtc(Path.Combine(_owner, item.Key)).Ticks, Is.EqualTo(item.Value), "fixture requires actual high-resolution UTC file times");
        }
        return times;
    }

    private CoverageCiTransportReceipt PackV2AndRetainOwner()
    {
        var expected = _expected with { SchemaVersion = 2 };
        var packed = CoverageCiTransport.PackV2(new(2, _owner, _archive), expected);
        var observed = CoverageCiTransport.InspectV2(new(2, _archive, packed.ArchiveSha256), expected);
        Assert.That(observed.Manifest.FileTimes.Count, Is.EqualTo(3), "positive v2 archive precondition");
        MoveOwnerAside();
        return packed;
    }
    private static int RunCli(string operation, string input, string expectation, string output)
    {
        var previous = Console.Error;
        using var errors = new StringWriter();
        try
        {
            Console.SetError(errors);
            return CoverageCiTransportCommands.Run([operation, "--input", input, "--expectation", expectation, "--output", output]);
        }
        finally { Console.SetError(previous); }
    }

    private CoverageCiTransportReceipt PackAndRemoveOwner()
    {
        var receipt = CoverageCiTransport.Pack(new(1, _owner, _archive), _expected);
        MoveOwnerAside();
        return receipt;
    }

    private void MoveOwnerAside()
    {
        AssertOwned();
        Directory.Move(_owner, Path.Combine(_root, "retained-original"));
    }

    private void RejectRehashedArchiveBeforeDestination(string code)
    {
        Assert.That(Assert.Throws<CoverageCiTransportException>(() => CoverageCiTransport.Restore(new(1, _archive,
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(_archive)))), _expected))!.Code,
            Is.EqualTo("coverage-ci-transport-" + code));
        Assert.That(Directory.Exists(_owner), Is.False);
    }

    private sealed record ZipItem(string Name, byte[] Bytes, int Attributes);
    private void Rewrite(Action<List<ZipItem>> mutate)
    {
        AssertOwned();
        List<ZipItem> items;
        using (var input = ZipFile.OpenRead(_archive))
            items = input.Entries.Select(entry => { using var bytes = new MemoryStream(); using var stream = entry.Open();
                stream.CopyTo(bytes); return new ZipItem(entry.FullName, bytes.ToArray(), entry.ExternalAttributes); }).ToList();
        mutate(items);
        var changed = Path.Combine(_root, "changed.zip");
        using (var output = ZipFile.Open(changed, ZipArchiveMode.Create))
            foreach (var item in items)
            {
                var entry = output.CreateEntry(item.Name, CompressionLevel.NoCompression);
                entry.ExternalAttributes = item.Attributes;
                using var stream = entry.Open();
                stream.Write(item.Bytes);
            }
        File.Move(changed, _archive, true);
    }

    private static void ReplaceManifest(List<ZipItem> items, JsonNode manifest)
    {
        var index = items.FindIndex(item => item.Name == "transport-manifest.json");
        items[index] = items[index] with { Bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, CoverageCiTransport.JsonOptions) };
    }

    private static Dictionary<string, byte[]> Bytes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);

    private void AssertOwned()
    {
        var path = Path.GetFullPath(_root);
        if (!Path.GetDirectoryName(path)!.Equals(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(path).StartsWith("fa-coverage-transport-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(Path.GetFileName(path)[22..], "N", out _) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            !_owner.Equals(Path.Combine(path, "owner"), StringComparison.Ordinal) ||
            !_archive.Equals(Path.Combine(path, "operation.zip"), StringComparison.Ordinal))
            throw new InvalidOperationException("coverage-transport-fixture-ownership");
    }

    [TearDown]
    public void ReleaseOwnedFixture()
    {
        if (_root.Length == 0 || !Directory.Exists(_root)) return;
        var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
        try
        {
            AssertOwned();
            if (failed) { TestContext.Error.WriteLine("coverage-transport: failed fixture retained as " + Path.GetFileName(_root)); return; }
            Directory.Delete(_root, true);
        }
        catch (Exception error) when (failed) { TestContext.Error.WriteLine("coverage-transport: cleanup failed (" + error.GetType().Name + ")."); }
    }
}

public sealed partial class IsolatedCoverageSessionTests
{
    [TestCase(false, TestName = "coverage-transport-v2/nonempty-restored-raw-readmission-preserves-exact-evidence")]
    [TestCase(true, TestName = "coverage-transport-v2/timestamp-only-drift-still-rejects-raw-readmission")]
    public void RestoredNonemptySessionUsesTheUnchangedStrictRawReader(bool changeRestoredTimestamp)
    {
        Assert.That(Read().Aggregate.TargetAssembly!.CoveredStatements, Is.EqualTo(3), "original raw admission positive control");
        var raw = Path.GetFullPath(_input.ArtifactDirectory);
        var retained = Path.Combine(_root, "retained-raw-v2-" + Guid.NewGuid().ToString("N"));
        var archive = Path.Combine(_root, "session-v2.zip");
        var originals = Directory.EnumerateFiles(raw, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(raw, path), path => (Bytes: File.ReadAllBytes(path), Ticks: File.GetLastWriteTimeUtc(path).Ticks), StringComparer.Ordinal);
        var transportExpected = new CoverageCiTransportExpectation(2, raw, _expected.SourceDirectory, _expected.Identity.Source.Tree);
        var packed = CoverageCiTransport.PackV2(new(2, raw, archive), transportExpected);
        AssertSessionTransportMoveOwned(raw, retained, archive);
        Directory.Move(raw, retained);
        _ = CoverageCiTransport.RestoreV2(new(2, archive, packed.ArchiveSha256), transportExpected);
        foreach (var item in originals)
        {
            Assert.That(File.ReadAllBytes(Path.Combine(raw, item.Key)), Is.EqualTo(item.Value.Bytes), item.Key);
            Assert.That(File.GetLastWriteTimeUtc(Path.Combine(raw, item.Key)).Ticks, Is.EqualTo(item.Value.Ticks), item.Key);
            Assert.That(File.GetLastWriteTimeUtc(Path.Combine(retained, item.Key)).Ticks, Is.EqualTo(item.Value.Ticks), item.Key);
        }
        Assert.That(Read().Aggregate.TargetAssembly!.CoveredStatements, Is.EqualTo(3), "same-root restored raw admission positive control");
        if (changeRestoredTimestamp)
        {
            var path = ArtifactPath(CoverageArtifactKind.AggregateJson);
            var before = File.ReadAllBytes(path);
            File.SetLastWriteTimeUtc(path, Artifact(CoverageArtifactKind.AggregateJson).LastWriteUtc.UtcDateTime.AddTicks(1));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before), "timestamp-only negative stimulus");
            Assert.That(Assert.Throws<IsolatedCoverageSessionException>(() => Read())!.Code,
                Is.EqualTo("coverage-session-artifact-timestamp-mismatch"));
        }
    }

    private void AssertSessionTransportMoveOwned(string raw, string retained, string archive)
    {
        var root = Path.GetFullPath(_root);
        var name = Path.GetFileName(root);
        Assert.That(Path.GetDirectoryName(root), Is.EqualTo(_parent).IgnoreCase);
        Assert.That(name.StartsWith("fa-session-", StringComparison.Ordinal) && Guid.TryParseExact(name[11..], "N", out _), Is.True);
        Assert.That(raw, Is.EqualTo(Path.Combine(root, "raw")));
        Assert.That(archive, Is.EqualTo(Path.Combine(root, "session-v2.zip")));
        Assert.That(Path.GetDirectoryName(retained), Is.EqualTo(root));
        var retainedName = Path.GetFileName(retained);
        Assert.That(retainedName.StartsWith("retained-raw-v2-", StringComparison.Ordinal) &&
            Guid.TryParseExact(retainedName[16..], "N", out _), Is.True);
        Assert.That(Directory.Exists(retained) || File.Exists(retained), Is.False);
        for (var directory = new DirectoryInfo(raw); directory is not null; directory = directory.Parent)
            Assert.That((directory.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) == 0, Is.True);
    }
}
