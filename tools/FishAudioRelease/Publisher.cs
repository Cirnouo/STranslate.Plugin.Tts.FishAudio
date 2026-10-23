using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Security.Cryptography;

namespace FishAudioRelease;

internal sealed record ReleaseInput(string Repository, string Tag, string Sha, string TagObject);
internal interface IReleaseIO
{
    string Git(params string[] args);
    Task<JsonNode?> Request(string method, string endpoint, JsonObject? body = null);
    Task<byte[]> Download(long assetId);
    Task<JsonNode?> Upload(long releaseId, string name, byte[] bytes);
}

internal static class Publisher
{
    internal const string AssetName = "STranslate.Plugin.Tts.FishAudio.spkg";
    internal static string Text(JsonNode? node, string name) => node?[name]?.GetValue<string>() ?? throw new InvalidOperationException("Missing GitHub string field.");
    internal static bool Flag(JsonNode? node, string name) => node?[name]?.GetValue<bool>() ?? throw new InvalidOperationException("Missing GitHub boolean field.");
    internal static long Id(JsonNode? node)
    {
        var value = node?["id"]?.GetValue<long>() ?? 0;
        Protocol.Require(value > 0, "Missing GitHub record identity.");
        return value;
    }

    internal static void ValidateInput(ReleaseInput input)
    {
        Protocol.Version(input.Tag);
        Protocol.Require(Regex.IsMatch(input.Repository, @"\A[A-Za-z0-9_-][A-Za-z0-9_.-]*/[A-Za-z0-9_-][A-Za-z0-9_.-]*\z") &&
            ObjectId(input.Sha) &&
            ObjectId(input.TagObject), "Invalid repository or object identity.");
    }

    internal static bool ObjectId(string value) => Regex.IsMatch(value, @"\A[a-f0-9]{40}\z") &&
            value != new string('0', 40);
    internal static void LocalIdentity(ReleaseInput input, IReleaseIO io)
    {
        ValidateInput(input);
        Protocol.Require(io.Git("cat-file", "-t", $"refs/tags/{input.Tag}") == "tag", "Annotated tag required.");
        Protocol.Require(io.Git("rev-parse", "--verify", $"refs/tags/{input.Tag}^{{commit}}") == input.Sha &&
            io.Git("rev-parse", "--verify", "HEAD") == input.Sha &&
            io.Git("rev-parse", "--verify", $"refs/tags/{input.Tag}") == input.TagObject, "Tag object, peeled commit and HEAD must match original push.");
        io.Git("merge-base", "--is-ancestor", input.Sha, "refs/remotes/origin/main");
    }

    internal static void PackageIdentity(string manifest, byte[] bytes, string version)
    {
        var source = JsonNode.Parse(manifest) ?? throw new InvalidOperationException("Invalid manifest.");
        Protocol.Require(Text(source, "Version") == version, "Tag/source manifest versions differ.");
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = zip.Entries.Where(x => x.FullName.Equals("plugin.json", StringComparison.OrdinalIgnoreCase)).ToArray();
        Protocol.Require(entries.Length == 1 &&
            entries[0].FullName == "plugin.json" &&
            entries[0].Length <= 1024 * 1024, "Package must contain one root plugin.json.");
        using var reader = new StreamReader(entries[0].Open());
        var packaged = JsonNode.Parse(reader.ReadToEnd());
        Protocol.Require(JsonNode.DeepEquals(source, packaged), "Packaged and source manifests differ.");
        Protocol.Require(!string.IsNullOrEmpty(Text(source, "PluginID")) &&
            !string.IsNullOrEmpty(Text(source, "ExecuteFileName")), "Missing plugin identity.");
    }

    internal static void Record(JsonNode? value, ReleaseInput input, bool draft, long? id = null)
    {
        var actualId = Id(value);
        var matchesReleaseIdentity = (id is null || actualId == id) &&
            Text(value, "tag_name") == input.Tag &&
            Text(value, "name") == input.Tag &&
            Flag(value, "draft") == draft &&
            !Flag(value, "prerelease") &&
            (draft
                ? Uri.TryCreate(Text(value, "html_url"), UriKind.Absolute, out var draftUrl) &&
            draftUrl.Scheme == "https" &&
            draftUrl.Host == "github.com" &&
            draftUrl.IsDefaultPort &&
            draftUrl.UserInfo.Length == 0 &&
            draftUrl.Query.Length == 0 &&
            draftUrl.Fragment.Length == 0 &&
            draftUrl.AbsolutePath.StartsWith($"/{input.Repository}/releases/tag/", StringComparison.Ordinal)
                : Text(value, "html_url") == $"https://github.com/{input.Repository}/releases/tag/{input.Tag}");
        Protocol.Require(matchesReleaseIdentity, "GitHub release record identity/classification mismatch.");
    }

    internal static async Task Remote(ReleaseInput input, IReleaseIO io, string root)
    {
        var value = await io.Request("GET", $"{root}/git/ref/tags/{Uri.EscapeDataString(input.Tag)}");
        Protocol.Require(Text(value?["object"], "type") == "tag" &&
            Text(value?["object"], "sha") == input.TagObject, "Remote annotated tag changed.");
    }

    internal static async Task<List<JsonNode>> Releases(IReleaseIO io, string root)
    {
        var result = new List<JsonNode>();
        for (int page = 1; page <= 10000; page++)
        {
            var values = await io.Request("GET", $"{root}/releases?per_page=100&page={page}") as JsonArray ?? throw new InvalidOperationException("Invalid release list.");
            Protocol.Require(values.Count <= 100, "Oversized release page.");
            foreach (var value in values)
            {
                Id(value);
                result.Add(value ?? throw new InvalidOperationException("Null release."));
            }

            if (values.Count < 100)
            {
                return result;
            }
        }

        throw new InvalidOperationException("Release pagination limit reached.");
    }

    internal static void Asset(JsonNode? asset, int size) => Protocol.Require(Id(asset) > 0 &&
            Text(asset, "name") == AssetName &&
            asset?["size"]?.GetValue<long>() == size &&
            Text(asset, "state") == "uploaded", "Package asset metadata mismatch.");
    internal static async Task VerifyAsset(IReleaseIO io, JsonNode asset, byte[] package)
    {
        Asset(asset, package.Length);
        var remote = await io.Download(Id(asset));
        Protocol.Require(CryptographicOperations.FixedTimeEquals(SHA256.HashData(remote), SHA256.HashData(package)), "Existing package SHA256 differs from verified package.");
    }

    internal static async Task<string> Publish(ReleaseInput input, IReleaseIO io, string manifest, byte[] package, string changelog)
    {
        LocalIdentity(input, io);
        var version = Protocol.Version(input.Tag);
        PackageIdentity(manifest, package, version);
        var changes = Protocol.Changes(changelog, version);
        var root = $"/repos/{input.Repository}";
        var existing = await io.Request("GET", $"{root}/releases/tags/{Uri.EscapeDataString(input.Tag)}");
        var releases = await Releases(io, root);
        var matches = releases.Where(x => Text(x, "tag_name") == input.Tag).ToArray();
        Protocol.Require(matches.Length <= 1 &&
            (existing is null || matches.Length == 0 || Id(existing) == Id(matches[0])), "Ambiguous release identity.");
        existing ??= matches.SingleOrDefault();
        if (existing is not null &&
            !Flag(existing, "draft"))
        {
            Record(existing, input, false);
            var assets = await io.Request("GET", $"{root}/releases/{Id(existing)}/assets") as JsonArray ?? throw new InvalidOperationException("Invalid assets.");
            Protocol.Require(assets.Count == 1, "Published release must contain exact verified package.");
            await VerifyAsset(io, assets[0]!, package);
            await Remote(input, io, root);
            return "skipped";
        }

        if (existing is not null)
        {
            Record(existing, input, true);
        }

        var latest = await io.Request("GET", $"{root}/releases/latest");
        string? baseline = null;
        if (latest is not null)
        {
            Id(latest);
            Protocol.Require(!Flag(latest, "draft") &&
            !Flag(latest, "prerelease"), "Latest stable endpoint returned invalid classification.");
            baseline = Text(latest, "tag_name");
            Protocol.Version(baseline);
            Protocol.Require(baseline != input.Tag, "Latest stable baseline unexpectedly equals unpublished tag.");
        }

        if (baseline is not null)
        {
            Protocol.Require(System.Version.Parse(version) > System.Version.Parse(baseline[1..]), "New stable version must exceed latest published stable.");
        }

        var generated = await io.Request("POST", $"{root}/releases/generate-notes", new JsonObject { ["tag_name"] = input.Tag, ["previous_tag_name"] = baseline });
        var body = Protocol.Notes(changes, input.Tag, Text(generated, "body"), input.Repository, baseline);
        JsonObject Fields(bool draft) => new()
        {
            ["tag_name"] = input.Tag,
            ["name"] = input.Tag,
            ["body"] = body,
            ["draft"] = draft,
            ["prerelease"] = false
        };
        await Remote(input, io, root);
        var draft = await io.Request(
            existing is null ? "POST" : "PATCH",
            existing is null ? $"{root}/releases" : $"{root}/releases/{Id(existing)}",
            Fields(true));
        Record(draft, input, true, existing is null ? null : Id(existing));
        var id = Id(draft);
        await Remote(input, io, root);
        var draftAssets = await io.Request("GET", $"{root}/releases/{id}/assets") as JsonArray ?? throw new InvalidOperationException("Invalid assets.");
        Protocol.Require(draftAssets.Count <= 1, "Unexpected draft assets; no deletion/overwrite allowed.");
        var asset = draftAssets.Count == 1 ? draftAssets[0] : await io.Upload(id, AssetName, package);
        Protocol.Require(asset is not null, "Missing uploaded asset.");
        await VerifyAsset(io, asset!, package);
        await Remote(input, io, root);
        var finalFields = Fields(false);
        finalFields["make_latest"] = "legacy";
        var published = await io.Request("PATCH", $"{root}/releases/{id}", finalFields);
        Record(published, input, false, id);
        return "published";
    }
}
