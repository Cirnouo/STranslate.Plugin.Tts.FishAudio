using System.Text.Json.Nodes;

namespace FishAudioRelease;

internal static class Context
{
    internal static ReleaseInput Parse(IReadOnlyDictionary<string, string> env, JsonNode? push)
    {
        string Get(string key) => env.TryGetValue(key, out var value) ? value : "";
        Protocol.Require(Get("GITHUB_EVENT_NAME") == "push" &&
            Get("GITHUB_REF_TYPE") == "tag" &&
            push is JsonObject &&
            Publisher.Flag(push, "created") &&
            !Publisher.Flag(push, "deleted") &&
            !Publisher.Flag(push, "forced") &&
            Publisher.Text(push, "ref") == Get("GITHUB_REF") &&
            Get("GITHUB_REF") == "refs/tags/" + Get("GITHUB_REF_NAME"), "Only matching newly created non-forced tag push is admitted.");
        var input = new ReleaseInput(Get("GITHUB_REPOSITORY"), Get("GITHUB_REF_NAME"), Get("GITHUB_SHA"), Publisher.Text(push, "after"));
        Publisher.ValidateInput(input);
        return input;
    }

    internal static void Tests()
    {
        var env = new Dictionary<string, string>
        {
            ["GITHUB_EVENT_NAME"] = "push",
            ["GITHUB_REF_TYPE"] = "tag",
            ["GITHUB_REF"] = "refs/tags/v1.2.3",
            ["GITHUB_REF_NAME"] = "v1.2.3",
            ["GITHUB_REPOSITORY"] = "owner/repo",
            ["GITHUB_SHA"] = PublisherTests.Sha
        };
        var push = new JsonObject
        {
            ["created"] = true,
            ["deleted"] = false,
            ["forced"] = false,
            ["ref"] = "refs/tags/v1.2.3",
            ["after"] = PublisherTests.TagObject
        };
        SelfTest.Equal("v1.2.3", Parse(env, push).Tag);
        foreach (var field in new[]
        {
            "created",
            "deleted",
            "forced"
        })
        {
            var changed = push.DeepClone();
            changed[field] = field != "created";
            SelfTest.Reject(() => Parse(env, changed));
        }

        env["GITHUB_EVENT_NAME"] = "workflow_dispatch";
        SelfTest.Reject(() => Parse(env, push));
        Console.WriteLine("Release event admission tests passed.");
    }
}
