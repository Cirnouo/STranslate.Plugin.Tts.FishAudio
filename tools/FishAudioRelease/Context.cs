using System.Text.Json.Nodes;

namespace FishAudioRelease;

internal static class Context
{
    internal const string IsolatedTagObjectVariable = "FISH_AUDIO_V120_TAG_OBJECT";

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
        var tag = Get("GITHUB_REF_NAME");
        var sha = Get("GITHUB_SHA");
        var after = Publisher.Text(push, "after");
        var tagObject = after;
        if (tag == Publisher.IsolatedTag)
        {
            // A tag push can report the peeled commit. The maintainer pins the annotation before pushing.
            tagObject = Get(IsolatedTagObjectVariable);
            Protocol.Require(Publisher.ObjectId(tagObject) && tagObject != sha &&
                (after == sha || after == tagObject), "The push must match the approved v1.2.0 tag object or its commit.");
        }

        var input = new ReleaseInput(Get("GITHUB_REPOSITORY"), tag, sha, tagObject);
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

        var isolatedEnv = new Dictionary<string, string>(env)
        {
            ["GITHUB_REF"] = "refs/tags/v1.2.0",
            ["GITHUB_REF_NAME"] = "v1.2.0",
            [IsolatedTagObjectVariable] = PublisherTests.TagObject
        };
        var isolatedPush = (JsonObject)push.DeepClone();
        isolatedPush["ref"] = "refs/tags/v1.2.0";
        isolatedPush["after"] = PublisherTests.Sha;
        SelfTest.Equal(PublisherTests.TagObject, Parse(isolatedEnv, isolatedPush).TagObject);
        isolatedPush["after"] = PublisherTests.TagObject;
        SelfTest.Equal(PublisherTests.TagObject, Parse(isolatedEnv, isolatedPush).TagObject);
        isolatedPush["after"] = new string('3', 40);
        SelfTest.Reject(() => Parse(isolatedEnv, isolatedPush));
        isolatedPush["after"] = PublisherTests.Sha;
        isolatedEnv[IsolatedTagObjectVariable] = PublisherTests.Sha;
        SelfTest.Reject(() => Parse(isolatedEnv, isolatedPush));
        isolatedEnv.Remove(IsolatedTagObjectVariable);
        SelfTest.Reject(() => Parse(isolatedEnv, isolatedPush));

        env["GITHUB_EVENT_NAME"] = "workflow_dispatch";
        SelfTest.Reject(() => Parse(env, push));
        Console.WriteLine("Release event admission tests passed.");
    }
}
