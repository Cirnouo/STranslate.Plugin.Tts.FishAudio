using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace FishAudio.Testing.Evidence;

// A collection setting, separate from binary build identity. Absence is the
// historical unfiltered v0; callers cannot supply replacement patterns.
public static class CoverageInstrumentationPolicy
{
    public const string CurrentVersion = "framework-infrastructure-exclusions-v2";
    public const string ExcludedAssemblies = "Microsoft.TestPlatform.CommunicationUtilities,Microsoft.TestPlatform.CoreUtilities,Microsoft.TestPlatform.CrossPlatEngine,Microsoft.TestPlatform.PlatformAbstractions,Microsoft.VisualStudio.TestPlatform.*,NUnit3.TestAdapter,testhost";

    internal static bool IsSupported(string? version) => version is null or CurrentVersion;

    internal static string[] Arguments(string? version) => version switch
    {
        null => [],
        CurrentVersion => ["--exclude-assemblies", ExcludedAssemblies],
        _ => throw new InvalidDataException("framework-instrumentation-policy-unknown"),
    };

    internal static bool RetainsProtectedNames(string product, string tests) => !Excluded(product) && !Excluded(tests);

    private static bool Excluded(string name) => name.Equals("Microsoft.TestPlatform.CommunicationUtilities", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Microsoft.TestPlatform.CoreUtilities", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Microsoft.TestPlatform.CrossPlatEngine", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Microsoft.TestPlatform.PlatformAbstractions", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft.VisualStudio.TestPlatform.", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("NUnit3.TestAdapter", StringComparison.OrdinalIgnoreCase) || name.Equals("testhost", StringComparison.OrdinalIgnoreCase);

    internal static bool RecordedVersionMatches(JsonElement launch, string? version)
    {
        if (!IsSupported(version) || launch.ValueKind != JsonValueKind.Object) return false;
        if (!launch.TryGetProperty("instrumentationPolicyVersion", out var recorded)) return version is null;
        return recorded.ValueKind == JsonValueKind.Null ? version is null :
            recorded.ValueKind == JsonValueKind.String && recorded.GetString() == version;
    }

    internal static string ReadAssemblyName(FileStream stream)
    {
        try
        {
            if (stream.Length is <= 0 or > 256 * 1024 * 1024) throw new BadImageFormatException();
            stream.Position = 0;
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata || pe.PEHeaders.MetadataSize is <= 0 or > 64 * 1024 * 1024) throw new BadImageFormatException();
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) throw new BadImageFormatException();
            var name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 1024 || name.Any(char.IsControl)) throw new BadImageFormatException();
            return name;
        }
        catch (Exception error) when (error is BadImageFormatException or OverflowException)
        { throw new InvalidDataException("framework-instrumentation-policy-invalid-managed-input"); }
    }
}
