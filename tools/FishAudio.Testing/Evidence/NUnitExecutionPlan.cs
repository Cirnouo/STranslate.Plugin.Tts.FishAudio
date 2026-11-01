using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;

namespace FishAudio.Testing.Evidence;

public sealed record NUnitExecutionPhase(string Id, string Kind, string? Profile,
    IReadOnlyList<DiscoveredNUnitCase> Cases, bool RequireOrder, int MaximumConcurrency,
    IReadOnlyList<string> ExclusiveResources);

public sealed record NUnitExecutionExpansionReason(string Code, string Profile,
    IReadOnlyList<string> SelectedIds, IReadOnlyList<string> AddedIds);

public sealed record NUnitExecutionPlanResult(string RawDiscoverySha256,
    IReadOnlyList<string> RequestedIds, IReadOnlyList<string> ExpandedIds,
    IReadOnlyList<NUnitExecutionExpansionReason> ExpansionReasons, IReadOnlyList<NUnitExecutionPhase> Phases);

public sealed class NUnitExecutionPlanException : Exception
{
    internal NUnitExecutionPlanException(string code) : base(code) => Code = code;
    public string Code { get; }
}

public static class NUnitExecutionPlan
{
    /// <summary>
    /// Builds execution phases from current framework metadata. A profile is an
    /// explicit execution dependency, never a file-to-suite impact mapping.
    /// The caller still owns source/build admission and actual execution.
    /// </summary>
    public static NUnitExecutionPlanResult ReadValidated(string absoluteDiscovery,
        IReadOnlyList<string> requestedIds, int maximumConcurrency = 4)
    {
        FileStream? guard = null;
        var completed = false;
        try
        {
            Require(maximumConcurrency is >= 1 and <= 4, "invalid-concurrency");
            Require(requestedIds is { Count: > 0 }, "empty-selection");
            Require(requestedIds.Count <= 100_000, "selection-limit");
            var requested = requestedIds.ToArray();
            Require(Path.IsPathFullyQualified(absoluteDiscovery), "invalid-discovery");
            guard = new FileStream(absoluteDiscovery, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(guard.Length is > 0 and <= 64 * 1024 * 1024, "invalid-discovery");
            var policies = NUnitExecutionMetadata.ReadValidated(absoluteDiscovery);
            Require(policies.Count > 0, "invalid-discovery");
            var digest = Convert.ToHexStringLower(SHA256.HashData(guard));
            var byId = policies.ToDictionary(item => item.Case.CanonicalId, StringComparer.Ordinal);
            var requestedSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in requested)
            {
                Require(!string.IsNullOrWhiteSpace(id) && byId.ContainsKey(id), "unknown-selection");
                Require(requestedSet.Add(id), "duplicate-selection");
            }

            var expanded = new HashSet<string>(requestedSet, StringComparer.Ordinal);
            var reasons = new List<NUnitExecutionExpansionReason>();
            var profiles = policies.Where(item => item.Profile is not null)
                .GroupBy(item => item.Profile!, StringComparer.Ordinal).ToArray();
            var groups = new List<Group>();
            var indexes = policies.Select((item, index) => (item.Case.CanonicalId, index))
                .ToDictionary(item => item.CanonicalId, item => item.index, StringComparer.Ordinal);
            foreach (var profile in profiles)
            {
                var members = profile.ToArray();
                var selected = members.Where(item => requestedSet.Contains(item.Case.CanonicalId)).ToArray();
                if (selected.Length == 0) continue;
                var added = members.Where(item => !requestedSet.Contains(item.Case.CanonicalId)).ToArray();
                foreach (var item in added) expanded.Add(item.Case.CanonicalId);
                if (added.Length != 0)
                    reasons.Add(new("complete-profile", profile.Key,
                        Array.AsReadOnly(selected.Select(item => item.Case.CanonicalId).ToArray()),
                        Array.AsReadOnly(added.Select(item => item.Case.CanonicalId).ToArray())));
                groups.Add(new(members.Min(item => indexes[item.Case.CanonicalId]), "ordered-profile", profile.Key,
                    members.OrderBy(item => item.ProfileOrdinal!.Value).ToArray()));
            }
            var ordinary = policies.Where(item => expanded.Contains(item.Case.CanonicalId) &&
                item.Profile is null && !item.FreshHost).ToArray();
            if (ordinary.Length != 0)
                groups.Add(new(indexes[ordinary[0].Case.CanonicalId], "ordinary", null, ordinary));
            foreach (var item in policies.Where(item => expanded.Contains(item.Case.CanonicalId) &&
                         item.Profile is null && item.FreshHost))
                groups.Add(new(indexes[item.Case.CanonicalId], "fresh-case", null, [item]));

            var phases = new List<NUnitExecutionPhase>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in groups.OrderBy(item => item.FirstIndex))
            {
                var resources = group.Members.SelectMany(item => item.ExclusiveResources)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var ordered = group.Kind == "ordered-profile";
                var concurrency = group.Kind == "ordinary" && resources.Length == 0 ? maximumConcurrency : 1;
                foreach (var item in group.Members)
                    Require(seen.Add(item.Case.CanonicalId), "invalid-phase-union");
                phases.Add(new("phase-" + (phases.Count + 1).ToString("D4", CultureInfo.InvariantCulture),
                    group.Kind, group.Profile, Array.AsReadOnly(group.Members.Select(item => item.Case).ToArray()),
                    ordered, concurrency, Array.AsReadOnly(resources)));
            }
            Require(phases.Count > 0 && seen.SetEquals(expanded), "invalid-phase-union");
            completed = true;
            return new(digest, Array.AsReadOnly(requested),
                Array.AsReadOnly(policies.Where(item => expanded.Contains(item.Case.CanonicalId))
                    .Select(item => item.Case.CanonicalId).ToArray()), reasons.AsReadOnly(), phases.AsReadOnly());
        }
        catch (NUnitExecutionPlanException) { throw; }
        catch (NUnitExecutionMetadataException) { throw Rejected("invalid-discovery"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or OverflowException or System.Security.SecurityException)
        { throw Rejected("invalid-input-or-file"); }
        finally
        {
            try { guard?.Dispose(); }
            catch { if (completed) throw Rejected("file-release-failed"); }
        }
    }

    private sealed record Group(int FirstIndex, string Kind, string? Profile, NUnitCaseExecutionPolicy[] Members);

    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw Rejected(code);
    }

    private static NUnitExecutionPlanException Rejected(string code) => new("execution-plan-" + code);
}
