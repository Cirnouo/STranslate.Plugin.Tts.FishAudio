using FishAudioRelease;
using System.Text.Json.Nodes;

try
{
    if (args.SequenceEqual(new[] { "--self-test" }))
    {
        SelfTest.Run();
        Context.Tests();
        await PublisherTests.Run();
        await HttpTests.Run();
        await CanonicalPackageTests.Run();
        WorkflowTests.Run(Directory.GetCurrentDirectory());
    }
    else if (args.SequenceEqual(new[] { "--canonicalize-package" }))
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), Publisher.AssetName);
        var manifest = await File.ReadAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "STranslate.Plugin.Tts.FishAudio", "plugin.json"));
        var metadata = JsonNode.Parse(manifest);
        var version = Publisher.Text(metadata, "Version");
        Protocol.Version("v" + version);
        var normalized = CanonicalPackage.Normalize(await File.ReadAllBytesAsync(path));
        Publisher.PackageIdentity(manifest, normalized, version);
        var temporary = path + ".canonical.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, normalized);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        Console.WriteLine("Canonical package validated; payload names and bytes preserved.");
    }
    else if (args.SequenceEqual(new[] { "--publish" }) || args.SequenceEqual(new[] { "--verify" }))
    {
        var env = new Dictionary<string, string>();
        foreach (var key in new[]
        {
            "GITHUB_EVENT_NAME",
            "GITHUB_REF_TYPE",
            "GITHUB_REF",
            "GITHUB_REF_NAME",
            "GITHUB_REPOSITORY",
            "GITHUB_SHA"
        })
        {
            env[key] = Environment.GetEnvironmentVariable(key) ?? "";
        }

        var eventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
        Protocol.Require(!string.IsNullOrWhiteSpace(eventPath), "GitHub push event file is required.");
        var input = Context.Parse(env, JsonNode.Parse(await File.ReadAllTextAsync(eventPath!)));
        var root = Directory.GetCurrentDirectory();
        // Validate admission before constructing any network client. Never accept a token argument/PAT fallback.
        var token = args[0] == "--publish" ? Environment.GetEnvironmentVariable("GITHUB_TOKEN") : "offline-verification";
        using var io = new GitHubIO(input.Repository, token ?? "", root);
        Publisher.LocalIdentity(input, io);
        var manifest = await File.ReadAllTextAsync(Path.Combine(root, "STranslate.Plugin.Tts.FishAudio", "plugin.json"));
        var package = await File.ReadAllBytesAsync(Path.Combine(root, Publisher.AssetName));
        var changelog = await File.ReadAllTextAsync(Path.Combine(root, "CHANGELOG.md"));
        Publisher.PackageIdentity(manifest, package, Protocol.Version(input.Tag));
        Protocol.Changes(changelog, Protocol.Version(input.Tag));
        if (args[0] == "--publish")
        {
            Console.WriteLine("Release " + await Publisher.Publish(input, io, manifest, package, changelog));
        }
        else
        {
            Console.WriteLine("Release identity and verified package accepted offline.");
        }
    }
    else
    {
        throw new InvalidOperationException("Use --self-test, --verify, or --publish. Publication requires a newly created annotated GitHub tag push.");
    }
}
catch (Exception error) when (error is not OutOfMemoryException)
{
    Console.Error.WriteLine("Release operation failed: " + (error is InvalidOperationException ? error.Message : error.GetType().Name));
    Environment.ExitCode = 1;
}
