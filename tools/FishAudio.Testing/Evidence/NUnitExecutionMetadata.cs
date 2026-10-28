using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace FishAudio.Testing.Evidence;

public sealed record NUnitCaseExecutionPolicy(DiscoveredNUnitCase Case, string FixtureFullName,
    bool FreshHost, string? Profile, int? ProfileOrdinal, IReadOnlyList<string> ExclusiveResources);

// Only fixed execution-metadata-* codes may leave the future reader. Input
// XML, filesystem paths and original parser exception details stay private.
public sealed class NUnitExecutionMetadataException : Exception
{
    internal NUnitExecutionMetadataException(string code) : base(code) => Code = code;
    public string Code { get; }
}

public static class NUnitExecutionMetadata
{
    private const string Fresh = "FishAudioFreshHost";
    private const string Profile = "FishAudioExecutionProfile";
    private const string Ordinal = "FishAudioProfileOrdinal";
    private const string Resource = "FishAudioExclusiveResource";
    private const int MaximumPolicyReferences = 2_000_000;

    /// <summary>
    /// Reads execution properties only. Coverage and repository declarations
    /// retain their existing leaf-only API and are never promoted here.
    /// The caller independently binds this raw discovery to its current build.
    /// </summary>
    public static IReadOnlyList<NUnitCaseExecutionPolicy> ReadValidated(string absoluteDump)
    {
        FileStream? guard = null;
        var completed = false;
        try
        {
            Require(Path.IsPathFullyQualified(absoluteDump), "invalid-input-or-file");
            guard = new FileStream(absoluteDump, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(guard.Length is > 0 and <= 64 * 1024 * 1024, "invalid-input-or-file");
            // Keep the same file immutable across catalog admission and policy
            // parsing. This reader neither loads the assembly nor runs sources.
            var catalog = NUnitEvidence.ReadCatalog(absoluteDump);
            var document = NUnitEvidence.ReadDiscoveryDocument(absoluteDump);

            var scopes = document.Descendants().Where(IsOwner).ToDictionary(owner => owner, ReadScope);
            var leaves = document.Descendants("test-case").ToDictionary(
                leaf => (string)leaf.Attribute("id")!, StringComparer.Ordinal);
            Require(catalog.Count > 0 && leaves.Count == catalog.Count, "invalid-discovery");
            var policies = new List<NUnitCaseExecutionPolicy>(catalog.Count);
            var work = 0;
            foreach (var item in catalog)
            {
                Require(leaves.TryGetValue(item.PlatformId, out var leaf), "invalid-discovery");
                var fixture = leaf.Ancestors("test-suite").FirstOrDefault(owner =>
                    (string?)owner.Attribute("type") == "TestFixture");
                var fixtureName = (string?)fixture?.Attribute("fullname");
                Require(Identity(fixtureName), "invalid-fixture");
                var owners = leaf.AncestorsAndSelf().Where(IsOwner).Reverse().ToArray();
                Require(owners.Length <= 128, "work-limit");
                var fresh = false;
                string? profile = null;
                int? ordinal = null;
                var resources = new HashSet<string>(StringComparer.Ordinal);
                foreach (var owner in owners)
                {
                    var scope = scopes[owner];
                    work = checked(work + 1 + scope.Resources.Length);
                    Require(work <= MaximumPolicyReferences, "work-limit");
                    fresh |= scope.FreshHost;
                    // Conflicting scopes cannot silently weaken or redirect
                    // a fixture's execution contract through a leaf override.
                    if (scope.Profile is not null)
                    {
                        Require(profile is null || profile == scope.Profile, "invalid-profile");
                        profile = scope.Profile;
                    }
                    if (scope.Ordinal is not null)
                    {
                        Require(ordinal is null || ordinal == scope.Ordinal, "invalid-ordinal");
                        ordinal = scope.Ordinal;
                    }
                    resources.UnionWith(scope.Resources);
                    Require(resources.Count <= 128, "invalid-resource");
                }
                Require((profile is null) == (ordinal is null), "invalid-ordinal");
                policies.Add(new(item, fixtureName!, fresh || profile is not null, profile, ordinal,
                    Array.AsReadOnly(resources.Order(StringComparer.Ordinal).ToArray())));
            }
            foreach (var profile in policies.Where(item => item.Profile is not null).GroupBy(item => item.Profile))
            {
                var ordinals = profile.Select(item => item.ProfileOrdinal!.Value).Order().ToArray();
                for (var index = 0; index < ordinals.Length; index++)
                    Require(ordinals[index] == index, "profile-ordinals");
            }
            completed = true;
            return policies.AsReadOnly();
        }
        catch (NUnitExecutionMetadataException) { throw; }
        catch (NUnitEvidenceException) { throw Rejected("invalid-discovery"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or XmlException or OverflowException or
            System.Security.SecurityException)
        { throw Rejected("invalid-input-or-file"); }
        finally
        {
            try { guard?.Dispose(); }
            catch { if (completed) throw Rejected("file-release-failed"); }
        }
    }

    private sealed record ScopePolicy(bool FreshHost, string? Profile, int? Ordinal, string[] Resources);

    private static ScopePolicy ReadScope(XElement owner)
    {
        var containers = owner.Elements("properties").Take(2).ToArray();
        Require(containers.Length <= 1, "invalid-properties");
        var properties = containers.SelectMany(container => container.Elements("property"))
            .Where(property => (string?)property.Attribute("name") is Fresh or Profile or Ordinal or Resource)
            .Take(129).ToArray();
        Require(properties.Length <= 128, "work-limit");
        var fresh = Scalar(properties, Fresh);
        Require(fresh is null or "true", "invalid-fresh-host");
        var profile = Scalar(properties, Profile);
        Require(profile is null || SafeName(profile), "invalid-profile");
        var ordinalText = Scalar(properties, Ordinal);
        int? ordinal = null;
        if (ordinalText is not null)
        {
            Require(int.TryParse(ordinalText, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
                value >= 0 && value.ToString(CultureInfo.InvariantCulture) == ordinalText, "invalid-ordinal");
            ordinal = value;
        }
        var resources = properties.Where(property => (string?)property.Attribute("name") == Resource)
            .Select(property => (string?)property.Attribute("value")).ToArray();
        Require(resources.All(SafeName), "invalid-resource");
        return new(fresh is not null, profile, ordinal, resources.Select(value => value!).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string? Scalar(XElement[] properties, string name)
    {
        var values = properties.Where(property => (string?)property.Attribute("name") == name).ToArray();
        Require(values.Length <= 1, "duplicate-scalar");
        if (values.Length == 0) return null;
        var value = (string?)values[0].Attribute("value");
        Require(value is not null, name switch
        {
            Fresh => "invalid-fresh-host",
            Profile => "invalid-profile",
            _ => "invalid-ordinal",
        });
        return value;
    }

    private static bool IsOwner(XElement element) => element.Name == "test-run" ||
        element.Name == "test-suite" || element.Name == "test-case";

    private static bool SafeName(string? value) => value is { Length: > 0 and <= 128 } &&
        AsciiStart(value[0]) && value.All(character => AsciiStart(character) || character is '.' or '_' or '-');

    private static bool AsciiStart(char character) => character is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool Identity(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 16_384 && !value.Any(char.IsControl);

    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw Rejected(code);
    }

    private static NUnitExecutionMetadataException Rejected(string code) => new("execution-metadata-" + code);
}
