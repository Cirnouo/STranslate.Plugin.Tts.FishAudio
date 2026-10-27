using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Build.Locator;

namespace FishAudio.Testing;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (EvidenceCommands.IsInvocation(args)) return EvidenceCommands.Run(args);
        if (CoverageCommands.IsInvocation(args)) return CoverageCommands.Run(args);
        if (!TryReadOptions(args, out var options))
        {
            Console.Error.WriteLine("Usage: workspace-probe --project <absolute tests csproj> --output <absolute JSON> [--coverage]");
            return 2;
        }

        var report = new ProbeReport { Coverage = options.Coverage };
        var stage = "project-input";
        try
        {
            var projectDirectory = Path.GetDirectoryName(options.Project)!;
            var repositoryRoot = Directory.GetParent(projectDirectory)?.Parent?.FullName;
            if (repositoryRoot is null || !File.Exists(options.Project)
                || !ProbePaths.Same(options.Project, Path.Combine(repositoryRoot, ProbePaths.TestProject)))
                throw new InvalidDataException();

            report.Project = ProbePaths.Relative(repositoryRoot, options.Project);
            stage = "msbuild-registration";
            RegisterMSBuild(projectDirectory, report);
            stage = "workspace";
            await RunWorkspaceAsync(options, repositoryRoot, report);
        }
        catch (Exception error)
        {
            report.Failures.Add(new(stage, "exception", ProbePaths.Token(error.GetType().FullName)));
        }

        report.Passed = report.Failures.Count == 0
            && report.Checks.All(check => check.Attempted && check.Passed);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.Output)!);
            await using var output = new FileStream(options.Output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(output, report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            });
        }
        catch (Exception)
        {
            Console.Error.WriteLine("workspace-probe: output-write-failed (an existing output is never replaced).");
            return 2;
        }

        Console.WriteLine(report.Passed ? "workspace-probe: passed." : "workspace-probe: failed; inspect JSON tokens.");
        return report.Passed ? 0 : 1;
    }

    // No workspace or Microsoft.Build API types may enter this registration method.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterMSBuild(string projectDirectory, ProbeReport report)
    {
        var instance = MSBuildLocator.QueryVisualStudioInstances(new VisualStudioInstanceQueryOptions
        {
            DiscoveryTypes = DiscoveryType.DotNetSdk,
            WorkingDirectory = projectDirectory,
        }).FirstOrDefault() ?? throw new InvalidOperationException();
        MSBuildLocator.RegisterInstance(instance);
        var msbuildFile = Path.Combine(instance.MSBuildPath, "Microsoft.Build.dll");
        report.Toolchain = new()
        {
            ProbeRuntimeVersion = Environment.Version.ToString(),
            LocatorVersion = VersionOf(typeof(MSBuildLocator).Assembly),
            SelectedSdkVersion = instance.Version.ToString(),
            SelectedMSBuildFileVersion = ProbePaths.Token(FileVersionInfo.GetVersionInfo(msbuildFile).FileVersion),
        };
        if (report.Toolchain.LocatorVersion == "[unavailable]" || report.Toolchain.SelectedMSBuildFileVersion == "[unavailable]")
            report.Failures.Add(new("msbuild-registration", "toolchain-version-unavailable"));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task RunWorkspaceAsync(ProbeOptions options, string repositoryRoot, ProbeReport report)
        => WorkspaceProbe.RunAsync(options, repositoryRoot, report);

    internal static string VersionOf(Assembly assembly)
        => ProbePaths.Token(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString());

    private static bool TryReadOptions(string[] args, out ProbeOptions options)
    {
        options = new("", "", false);
        if (args.Length == 0 || args[0] != "workspace-probe") return false;
        string? project = null, output = null;
        var coverage = false;
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index] == "--coverage" && !coverage) coverage = true;
            else if (args[index] == "--project" && project is null && index + 1 < args.Length) project = args[++index];
            else if (args[index] == "--output" && output is null && index + 1 < args.Length) output = args[++index];
            else return false;
        }
        if (project is null || output is null || !Path.IsPathFullyQualified(project) || !Path.IsPathFullyQualified(output)) return false;
        try { options = new(Path.GetFullPath(project), Path.GetFullPath(output), coverage); }
        catch (Exception) { return false; }
        return true;
    }
}

internal sealed record ProbeOptions(string Project, string Output, bool Coverage);
