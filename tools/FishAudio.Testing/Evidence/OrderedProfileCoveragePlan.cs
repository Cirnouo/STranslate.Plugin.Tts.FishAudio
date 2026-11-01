using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using FishAudio.Testing.Execution;

namespace FishAudio.Testing.Evidence;

public sealed record OrderedProfileCoverageMember(int Ordinal, DiscoveredNUnitCase Case);
public sealed record OrderedProfileCoveragePlanRecord(string Profile, string RawDiscoverySha256,
    IReadOnlyList<OrderedProfileCoverageMember> Members)
{
    public int MaximumConcurrency => 1;
    public bool RequireOrder => true;
    public bool CompleteBaseline => false;
    public IReadOnlyList<string> ExclusiveResources { get; init; } = [];
    public bool RequiresQueuedRootActions => OrderedProfileCoveragePlan.IsNativeProfile(Profile);
    public string ExecutionDependency => (RequiresQueuedRootActions ? "ordered-native-profile:" : "ordered-profile:") + Profile;
}

public sealed class OrderedProfileCoverageException : Exception
{
    internal OrderedProfileCoverageException(string code) : base(code) => Code = code;
    public string Code { get; }
}

// This is one complete framework-declared execution dependency, never a coverage map.
public static class OrderedProfileCoveragePlan
{
    public static OrderedProfileCoveragePlanRecord ReadValidated(string absoluteDiscovery, string profile)
    {
        try
        {
            Require(Path.IsPathFullyQualified(absoluteDiscovery), "discovery-path");
            using var guard = new FileStream(absoluteDiscovery, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(guard.Length is > 0 and <= 64 * 1024 * 1024, "discovery-size");
            Require(profile is { Length: > 0 and <= 128 }, "profile-required");
            var policies = NUnitExecutionMetadata.ReadValidated(absoluteDiscovery);
            var members = policies.Where(item => item.Profile == profile).OrderBy(item => item.ProfileOrdinal).ToArray();
            // Exact membership in admitted execution metadata is the authority
            // for general tokens. Native first-use profiles retain desktop.
            Require(members.Length > 0, "complete-ordered-profile-required");
            Require(!IsNativeProfile(profile) || members.All(item => item.ExclusiveResources.Contains("desktop", StringComparer.Ordinal)),
                "complete-desktop-profile-required");
            var platforms = members.Select(item => item.Case.PlatformId).ToHashSet(StringComparer.Ordinal);
            var document = NUnitEvidence.ReadDiscoveryDocument(absoluteDiscovery);
            Require(!document.Descendants("test-case").Where(item => platforms.Contains((string?)item.Attribute("id") ?? ""))
                .Elements("properties").Elements("property").Any(item =>
                    (string?)item.Attribute("name") == "FishAudioOwnedChild"), "owned-app-host-unsupported");
            var ids = members.Select(item => item.Case.CanonicalId).ToArray();
            var plan = NUnitExecutionPlan.ReadValidated(absoluteDiscovery, ids, 1);
            Require(plan.Phases.Count == 1 && plan.Phases[0].Kind == "ordered-profile" &&
                plan.Phases[0].Profile == profile && plan.Phases[0].RequireOrder && plan.Phases[0].MaximumConcurrency == 1 &&
                plan.Phases[0].Cases.SequenceEqual(members.Select(item => item.Case)) &&
                members.Select(item => item.ProfileOrdinal).SequenceEqual(Enumerable.Range(0, members.Length).Select(item => (int?)item)),
                "complete-ordered-profile-required");
            return new(profile, plan.RawDiscoverySha256, Array.AsReadOnly(members.Select(item =>
                new OrderedProfileCoverageMember(item.ProfileOrdinal!.Value, item.Case)).ToArray()))
            { ExclusiveResources = plan.Phases[0].ExclusiveResources };
        }
        catch (OrderedProfileCoverageException) { throw; }
        catch (Exception error) when (error is NUnitExecutionMetadataException or NUnitExecutionPlanException or NUnitEvidenceException or
            IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        { throw new OrderedProfileCoverageException("ordered-profile-coverage-discovery-rejected"); }
    }

    public static OrderedProfileCoveragePlanRecord ValidateSelection(string absoluteDiscovery, string profile,
        string rawDiscoverySha256, IReadOnlyList<FrameworkCaseBinding> cases, int maximumConcurrency, bool requireOrder)
    {
        var plan = ReadValidated(absoluteDiscovery, profile);
        Require(maximumConcurrency == 1 && requireOrder && rawDiscoverySha256 == plan.RawDiscoverySha256 &&
            cases is not null && cases.Count == plan.Members.Count &&
            cases.SequenceEqual(plan.Members.Select(item => new FrameworkCaseBinding(item.Case.CanonicalId, item.Case.FullName))),
            "selection-mismatch");
        return plan;
    }

    internal static bool IsNativeProfile(string profile) =>
        profile is "native-settings" or "native-input" or "native-popups" or "native-cards";

    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw new OrderedProfileCoverageException("ordered-profile-coverage-" + code);
    }
}
