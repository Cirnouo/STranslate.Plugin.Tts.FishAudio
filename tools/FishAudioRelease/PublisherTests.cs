using System.Text.Json.Nodes;
using System.IO.Compression;
using System.Text;

namespace FishAudioRelease;

internal static class PublisherTests
{
    internal const string Sha = "1111111111111111111111111111111111111111", TagObject = "2222222222222222222222222222222222222222";
    internal const string Manifest = "{\"Version\":\"1.2.3\",\"PluginID\":\"fish\",\"ExecuteFileName\":\"fish.dll\"}";
    internal static byte[] Package(string manifest = Manifest)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("plugin.json").Open());
            writer.Write(manifest);
        }

        return bytes.ToArray();
    }

    internal static async Task Run()
    {
        var input = new ReleaseInput("owner/repo", "v1.2.3", Sha, TagObject);
        var fake = new Fake();
        var package = Package();
        SelfTest.Equal("published", await Publisher.Publish(input, fake, Manifest, package, "## [1.2.3] - 2026-10-23\n- change"));
        SelfTest.Equal("upload,download,publish", string.Join(',', fake.Events.Where(x => x is "upload" or "download" or "publish")));
        SelfTest.Equal(3, fake.Events.Count(x => x == "remote"));
        await Reject(async () => await Publisher.Publish(input, new Fake { WrongRemote = true }, Manifest, package, "## [1.2.3] - 2026-10-23\n- change"));
        await Reject(async () => await Publisher.Publish(input, new Fake(), Manifest, Package(Manifest.Replace("1.2.3", "1.2.4")), "## [1.2.3] - 2026-10-23\n- change"));
        fake = new Fake
        {
            Published = true,
            Bytes = package
        };
        SelfTest.Equal("skipped", await Publisher.Publish(input, fake, Manifest, package, "## [1.2.3] - 2026-10-23\n- change"));
        SelfTest.Equal(false, fake.Events.Contains("publish"));
        fake = new Fake
        {
            Published = true,
            Bytes = [1, 2]
        };
        await Reject(async () => await Publisher.Publish(input, fake, Manifest, package, "## [1.2.3] - 2026-10-23\n- change"));
        foreach (var fail in new[]
        {
            "upload",
            "download",
            "publish"
        })
        {
            fake = new Fake
            {
                Fail = fail
            };
            await Reject(async () => await Publisher.Publish(input, fake, Manifest, package, "## [1.2.3] - 2026-10-23\n- change"));
            if (fail != "publish")
            {
                SelfTest.Equal(false, fake.Events.Contains("publish"));
            }
        }

        fake = new Fake
        {
            Draft = true,
            DraftUrl = true,
            Bytes = package
        };
        SelfTest.Equal("published", await Publisher.Publish(input, fake, Manifest, package, "## [1.2.3] - 2026-10-23\n- change"));
        SelfTest.Equal(false, fake.Events.Contains("upload"));
        foreach (var boundary in new[]
        {
            "cat-file",
            "rev-parse"
        })
        {
            fake = new Fake
            {
                BadGit = boundary
            };
            await Reject(async () => await Publisher.Publish(input, fake, Manifest, package, "## [1.2.3] - 2026-10-23\n- change"));
            SelfTest.Equal(0, fake.Events.Count);
        }

        foreach (var boundary in new[]
        {
            1,
            2,
            3
        })
        {
            fake = new Fake
            {
                MoveAt = boundary
            };
            await Reject(async () => await Publisher.Publish(input, fake, Manifest, package, "## [1.2.3] - 2026-10-23\n- change"));
            SelfTest.Equal(false, fake.Events.Contains("publish"));
        }

        fake = new Fake
        {
            Published = true,
            BadClassification = true,
            Bytes = package
        };
        await Reject(async () => await Publisher.Publish(input, fake, Manifest, package, "## [1.2.3] - 2026-10-23\n- change"));
        await IsolatedRelease(input, package);
        Console.WriteLine("Release publication transaction tests passed.");
    }

    private static async Task IsolatedRelease(ReleaseInput standardInput, byte[] standardPackage)
    {
        var input = standardInput with { Tag = "v1.2.0" };
        var manifest = Manifest.Replace("1.2.3", "1.2.0", StringComparison.Ordinal);
        var package = Package(manifest);
        const string changelog = "## [1.2.0] - 2026-11-11\n- change";
        var fake = new Fake { Tag = input.Tag, NotOnMain = true };
        SelfTest.Equal("published", await Publisher.Publish(input, fake, manifest, package, changelog));
        SelfTest.Equal(3, fake.Events.Count(x => x == "release-branch"));

        foreach (var local in new[]
        {
            new Fake { MissingReleaseTip = true },
            new Fake { ReleaseTip = TagObject },
            new Fake { Parents = TagObject },
            new Fake { Parents = Fake.Cut + " " + TagObject }
        })
        {
            await Reject(() => Publisher.Publish(input, local, manifest, package, changelog));
            SelfTest.Equal(0, local.Events.Count);
        }

        foreach (var remote in new[]
        {
            new Fake { RemoteReleaseBranch = null },
            new Fake { RemoteReleaseBranch = "refs/heads/release/v1.2.0-other" },
            new Fake { RemoteReleaseType = "tag" },
            new Fake { RemoteReleaseTip = TagObject }
        })
        {
            await Reject(() => Publisher.Publish(input, remote, manifest, package, changelog));
            SelfTest.Equal(false, remote.Events.Contains("draft"));
        }

        fake = new Fake { Tag = input.Tag, MoveReleaseAt = 3 };
        await Reject(() => Publisher.Publish(input, fake, manifest, package, changelog));
        SelfTest.Equal(true, fake.Events.Contains("upload"));
        SelfTest.Equal(false, fake.Events.Contains("publish"));

        fake = new Fake { Tag = input.Tag, Published = true, Bytes = package, NotOnMain = true };
        SelfTest.Equal("skipped", await Publisher.Publish(input, fake, manifest, package, changelog));
        fake = new Fake { Tag = input.Tag, Published = true, Bytes = package, RemoteReleaseTip = TagObject };
        await Reject(() => Publisher.Publish(input, fake, manifest, package, changelog));

        fake = new Fake { NotOnMain = true };
        await Reject(() => Publisher.Publish(standardInput, fake, Manifest, standardPackage, "## [1.2.3] - 2026-10-23\n- change"));
        SelfTest.Equal(0, fake.Events.Count);
    }

    internal static async Task Reject(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new Exception("Expected publisher rejection");
    }

    internal sealed class Fake : IReleaseIO
    {
        internal const string Cut = "4bc15267a940040d4c485ff74ede45f1d61dc2ec";
        internal string Tag = "v1.2.3", ReleaseTip = Sha, Parents = Cut;
        internal string? RemoteReleaseBranch = "refs/heads/release/v1.2.0";
        internal string RemoteReleaseType = "commit", RemoteReleaseTip = Sha;
        internal bool MissingReleaseTip, NotOnMain;
        internal int MoveReleaseAt;
        internal List<string> Events = [];
        internal bool WrongRemote, Published, Draft, BadClassification, DraftUrl;
        internal string? BadGit;
        internal int MoveAt;
        internal string? Fail;
        internal byte[] Bytes = [];
        public string Git(params string[] args)
        {
            if (BadGit == args[0])
            {
                return "wrong";
            }

            if (args.SequenceEqual(["rev-parse", "--verify", "refs/remotes/origin/release/v1.2.0"]))
            {
                Protocol.Require(!MissingReleaseTip, "Release branch ref is missing.");
                return ReleaseTip;
            }

            if (args.SequenceEqual(["rev-list", "--parents", "-n", "1", Sha]))
            {
                return $"{Sha} {Parents}";
            }

            if (args.SequenceEqual(["merge-base", "--is-ancestor", Sha, "refs/remotes/origin/main"]))
            {
                Protocol.Require(!NotOnMain, "The release commit is outside main.");
                return "";
            }

            return args[0] == "cat-file" ? "tag" : args[^1].EndsWith("^{commit}", StringComparison.Ordinal) || args[^1] == "HEAD" ? Sha : TagObject;
        }
        internal JsonObject Release(bool draft) => new()
        {
            ["id"] = 1L,
            ["tag_name"] = Tag,
            ["name"] = Tag,
            ["draft"] = draft,
            ["prerelease"] = BadClassification,
            ["html_url"] = draft &&
            DraftUrl ? "https://github.com/owner/repo/releases/tag/untagged-example" : $"https://github.com/owner/repo/releases/tag/{Tag}"
        };
        public Task<JsonNode?> Request(string method, string endpoint, JsonObject? body = null)
        {
            if (method == "GET" && endpoint == "/repos/owner/repo/git/ref/heads/release/v1.2.0")
            {
                Events.Add("release-branch");
                Protocol.Require(RemoteReleaseBranch is not null, "Remote release branch is missing.");
                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["ref"] = RemoteReleaseBranch,
                    ["object"] = new JsonObject
                    {
                        ["type"] = RemoteReleaseType,
                        ["sha"] = MoveReleaseAt > 0 && Events.Count(x => x == "release-branch") >= MoveReleaseAt
                            ? TagObject
                            : RemoteReleaseTip
                    }
                });
            }

            if (endpoint.Contains("/git/ref/", StringComparison.Ordinal))
            {
                Events.Add("remote");
                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["object"] = new JsonObject
                    {
                        ["type"] = "tag",
                        ["sha"] = WrongRemote || (MoveAt > 0 &&
            Events.Count(x => x == "remote") >= MoveAt) ? Sha : TagObject
                    }
                });
            }

            if (endpoint.EndsWith("/assets", StringComparison.Ordinal))
            {
                return Task.FromResult<JsonNode?>(Bytes.Length == 0 ? new JsonArray() : new JsonArray(new JsonObject { ["id"] = 5L, ["name"] = "STranslate.Plugin.Tts.FishAudio.spkg", ["size"] = (long)Bytes.Length, ["state"] = "uploaded" }));
            }

            if (endpoint.Contains("/tags/", StringComparison.Ordinal))
            {
                return Task.FromResult<JsonNode?>(Published ? Release(false) : null);
            }

            if (endpoint.EndsWith("/latest", StringComparison.Ordinal))
            {
                return Task.FromResult<JsonNode?>(null);
            }

            if (method == "GET")
            {
                return Task.FromResult<JsonNode?>(Draft ? new JsonArray(Release(true)) : new JsonArray());
            }

            if (endpoint.EndsWith("generate-notes", StringComparison.Ordinal))
            {
                return Task.FromResult<JsonNode?>(new JsonObject { ["body"] = "" });
            }

            if (body?["draft"]?.GetValue<bool>() == false)
            {
                Hit("publish");
                return Task.FromResult<JsonNode?>(Release(false));
            }

            Events.Add("draft");
            return Task.FromResult<JsonNode?>(Release(true));
        }

        void Hit(string action)
        {
            Events.Add(action);
            if (Fail == action)
            {
                throw new InvalidOperationException("Injected boundary failure");
            }
        }

        public Task<byte[]> Download(long assetId)
        {
            Hit("download");
            return Task.FromResult(Bytes);
        }

        public Task<JsonNode?> Upload(long releaseId, string name, byte[] bytes)
        {
            Hit("upload");
            Bytes = bytes;
            return Task.FromResult<JsonNode?>(new JsonObject { ["id"] = 5L, ["name"] = name, ["size"] = (long)bytes.Length, ["state"] = "uploaded" });
        }
    }
}
