using NUnit.Framework;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using static TestAssertions;

[TestFixture]
[NonParallelizable]
internal static class BuildScriptSuite
{
    private const string ProjectName = "STranslate.Plugin.Tts.FishAudio";
    private const string TestProjectName = "STranslate.Plugin.Tts.FishAudio.Tests";

    private static readonly string[] ArtifactDirectories =
    [
        "bin",
        "obj",
        ".artifacts",
        Path.Combine(ProjectName, "bin"),
        Path.Combine(ProjectName, "obj"),
        Path.Combine(ProjectName, ".artifacts"),
        Path.Combine("tests", TestProjectName, "obj"),
        Path.Combine("tests", TestProjectName, "bin"),
    ];

    [Test]
    [Property("FishAudioCaseId", "repository.build/configuration-parameter-is-rejected")]
    [Order(67)]
    public static void ConfigurationParameterIsRejected()
    {
        using var repository = BuildScriptTestRepository.Create();

        var result = repository.RunBuild("-Configuration", "Release");

        AssertEqual(
            true,
            result.ExitCode != 0,
            "The removed -Configuration parameter should be rejected");
        AssertEqual(
            false,
            repository.DotnetWasInvoked(),
            "A rejected -Configuration parameter should stop before dotnet is invoked");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.build/test-selection-arguments-reach-runner")]
    [Order(76)]
    public static void TestSelectionArgumentsReachRunner()
    {
        using var repository = BuildScriptTestRepository.Create();
        var result = repository.RunBuild("-Release", "-Test", "-TestGroup", "settings.storage",
            "-TestCase", "settings.storage/round-trip", "-TestPartition", "core",
            "-TestResults", "selected results.json", "-Affected", "-TestBase", "origin/main");
        AssertEqual(0, result.ExitCode, "Selected regressions still build and verify the package");
        var invocation = repository.ReadDotnetInvocations().Single(line => line.StartsWith("run ", StringComparison.Ordinal));
        foreach (var argument in new[] { "--group settings.storage", "--case settings.storage/round-trip",
                     "--partition core", "--results selected results.json", "--affected", "--base origin/main" })
            AssertEqual(true, invocation.Contains(argument, StringComparison.Ordinal), "Build forwards the requested regression selection");
        AssertEqual(true, invocation.Contains(" -- --legacy --group ", StringComparison.Ordinal),
            "Full legacy validation explicitly selects the legacy entry before forwarding selectors");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.build/test-selection-without-test-is-rejected")]
    [Order(77)]
    public static void TestSelectionWithoutTestIsRejected()
    {
        using var repository = BuildScriptTestRepository.Create();
        var result = repository.RunBuild("-TestGroup", "settings.storage");
        AssertEqual(true, result.ExitCode != 0, "A test selector without -Test is an error");
        AssertEqual(false, repository.DotnetWasInvoked(), "Invalid selection stops before build or cleanup work");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.build/failed-selected-tests-keep-exit-code")]
    [Order(78)]
    public static void FailedSelectedTestsKeepExitCode()
    {
        using var repository = BuildScriptTestRepository.Create();
        var result = repository.RunBuildWithFailedTests("-Release", "-Test", "-Clean", "-TestGroup", "settings.storage");
        AssertEqual(23, result.ExitCode, "Selected regression failure keeps the runner exit code");
        AssertEqual(true, File.Exists(Path.Combine(repository.RootPath, $"{ProjectName}.spkg")), "A failed test does not erase the inspected package");
        AssertEqual(true, ArtifactDirectories.All(path => !repository.ArtifactDirectoryExists(path)), "Cleanup still runs after selected failure");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.build/release-switch-selects-release-without-debug-packaging")]
    [Order(68)]
    public static void ReleaseSwitchSelectsReleaseWithoutDebugPackaging()
    {
        using var repository = BuildScriptTestRepository.Create();
        repository.SeedDebugOnlyArtifact();

        var result = repository.RunBuild("-Release", "-Clean");
        var invocations = repository.ReadDotnetInvocations();

        AssertEqual(0, result.ExitCode, $"-Release build should succeed with the fake dotnet host. Output: {result.Output}");
        AssertEqual(
            true,
            invocations.First().Contains("-c Release", StringComparison.Ordinal),
            "-Release should pass the Release configuration to dotnet");
        AssertEqual(
            false,
            invocations.Any(InvokesDebugPackagingTarget),
            "-Release should rely on automatic packaging instead of the Debug packaging target");
        AssertEqual(
            false,
            repository.RootPackageContains("debug-only.txt"),
            "-Release should discover and copy only the isolated Release package");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.build/package-missing-audio-runtime-dependency-fails")]
    [Order(69)]
    public static void PackageMissingAudioRuntimeDependencyFails()
    {
        using var repository = BuildScriptTestRepository.Create();
        var result = repository.RunBuildWithoutAudioDependency();
        AssertEqual(true, result.ExitCode != 0,
            "A package missing the required audio runtime must fail verification");
        AssertEqual(true, result.Output.Contains("NAudio.WinMM.dll", StringComparison.Ordinal),
            "The failure should identify the missing runtime dependency");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.build/package-missing-streaming-runtime-dependency-fails")]
    [Order(72)]
    public static void PackageMissingStreamingRuntimeDependencyFails()
    {
        string[] dependencies = ["MessagePack.dll", "MessagePack.Annotations.dll", "Microsoft.NET.StringTools.dll"];
        foreach (var dependency in dependencies)
        {
            using var repository = BuildScriptTestRepository.Create();
            var result = repository.RunBuildWithoutStreamingDependency(dependency);
            AssertEqual(true, result.ExitCode != 0,
                $"A package missing the required streaming runtime {dependency} must fail verification");
            AssertEqual(true, result.Output.Contains(dependency, StringComparison.Ordinal),
                $"The failure should identify the missing streaming runtime {dependency}");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "repository.build/package-missing-russian-xaml-fails")]
    [Order(65)]
    public static void PackageMissingRussianXamlFails() => PackageMissingLocaleFails("Languages/ru.xaml");

    [Test]
    [Property("FishAudioCaseId", "repository.build/package-missing-turkish-metadata-fails")]
    [Order(66)]
    public static void PackageMissingTurkishMetadataFails() => PackageMissingLocaleFails("Languages/tr.json");

    private static void PackageMissingLocaleFails(string resource)
    {
        using var repository = BuildScriptTestRepository.Create();
        var result = repository.RunBuildWithoutLocale(resource);
        AssertEqual(true, result.ExitCode != 0, $"A package missing {resource} must fail verification");
        AssertEqual(true, result.Output.Contains($"Missing required files in .spkg: {resource}", StringComparison.Ordinal),
            "The package failure must identify the omitted locale resource");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.build/package-missing-octicons-notice-fails")]
    [Order(70)]
    public static void PackageMissingOcticonsNoticeFails()
    {
        using var repository = BuildScriptTestRepository.Create();
        var result = repository.RunBuildWithoutOcticonsNotice();
        AssertEqual(true, result.ExitCode != 0, "A package missing the required icon license must fail verification");
        AssertEqual(true, result.Output.Contains("Octicons.LICENSE.txt", StringComparison.Ordinal), "Package rejection must identify the missing icon license");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.build/package-missing-flag-icons-notice-fails")]
    [Order(71)]
    public static void PackageMissingFlagIconsNoticeFails()
    {
        using var repository = BuildScriptTestRepository.Create();
        var result = repository.RunBuildWithoutFlagIconsNotice();
        AssertEqual(true, result.ExitCode != 0, "A package missing the bundled flags' complete license must fail verification");
        AssertEqual(true, result.Output.Contains("flag-icons.LICENSE.txt", StringComparison.Ordinal), "Package rejection identifies the missing flag license");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.build/ordinary-build-cleans-every-artifact-directory-before-building")]
    [Order(73)]
    public static void OrdinaryBuildCleansEveryArtifactDirectoryBeforeBuilding()
    {
        using var repository = BuildScriptTestRepository.Create();
        repository.SeedArtifactDirectories();

        var result = repository.RunBuild();
        var invocations = repository.ReadDotnetInvocations();

        AssertEqual(0, result.ExitCode, $"Ordinary build should succeed with the fake dotnet host. Output: {result.Output}");
        AssertEqual(
            true,
            invocations.First().Contains("-c Debug", StringComparison.Ordinal),
            "An ordinary build should use Debug without an explicit configuration switch");
        AssertEqual(
            true,
            invocations.Any(InvokesDebugPackagingTarget),
            $"An ordinary Debug build should invoke the explicit packaging target. Invocations: {string.Join(" || ", invocations)}");
        AssertEqual(
            true,
            repository.FirstDotnetInvocationSawNoSeededArtifacts(),
            "Ordinary build should clean every configured artifact directory before the first dotnet build");
        AssertEqual(
            true,
            ArtifactDirectories.All(repository.ArtifactDirectoryExists),
            "Ordinary build should retain intermediate directories produced by the current build");
        AssertEqual(
            true,
            File.Exists(Path.Combine(repository.RootPath, $"{ProjectName}.spkg")),
            "Ordinary build should retain the copied root package");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.build/clean-build-cleans-every-artifact-directory-after-building")]
    [Order(74)]
    public static void CleanBuildCleansEveryArtifactDirectoryAfterBuilding()
    {
        using var repository = BuildScriptTestRepository.Create();
        repository.SeedArtifactDirectories();

        var result = repository.RunBuild("-Clean");

        AssertEqual(0, result.ExitCode, $"-Clean build should succeed with the fake dotnet host. Output: {result.Output}");
        AssertEqual(
            true,
            repository.FirstDotnetInvocationSawEverySeededArtifact(),
            "-Clean should defer cleanup until after the first dotnet build");
        AssertEqual(
            true,
            ArtifactDirectories.All(relativePath => !repository.ArtifactDirectoryExists(relativePath)),
            "-Clean should remove every configured artifact directory after building");
        AssertEqual(
            true,
            File.Exists(Path.Combine(repository.RootPath, $"{ProjectName}.spkg")),
            "-Clean should retain the copied root package");
    }

    [Test]
    [Property("FishAudioCaseId", "repository.build/clean-build-cleans-after-failed-regression-tests")]
    [Order(75)]
    public static void CleanBuildCleansAfterFailedRegressionTests()
    {
        using var repository = BuildScriptTestRepository.Create();
        repository.SeedArtifactDirectories();

        var result = repository.RunBuildWithFailedTests("-Clean", "-Test");

        AssertEqual(23, result.ExitCode, "-Clean should preserve the failed regression-test exit code");
        AssertEqual(
            true,
            ArtifactDirectories.All(relativePath => !repository.ArtifactDirectoryExists(relativePath)),
            "-Clean should remove every configured artifact directory even when regression tests fail");
        AssertEqual(
            true,
            File.Exists(Path.Combine(repository.RootPath, $"{ProjectName}.spkg")),
            "-Clean should retain the copied root package when later regression tests fail");
    }

    // NUnit discovers these three orchestration cases independently. They do not
    // add entries to, or replace assertions in, the legacy repository catalog.
    internal static Task FullReleaseRunsLegacyAndFrameworkOnceAsync() =>
        ExerciseFrameworkBuildAsync(legacyOnly: false, failFramework: false);

    internal static Task FrameworkFailureKeepsExitCodePackageAndCleanupAsync() =>
        ExerciseFrameworkBuildAsync(legacyOnly: false, failFramework: true);

    internal static Task LegacyOnlyCoordinatorSkipsFrameworkAsync() =>
        ExerciseFrameworkBuildAsync(legacyOnly: true, failFramework: false);

    private static async Task ExerciseFrameworkBuildAsync(bool legacyOnly, bool failFramework)
    {
        var clock = Stopwatch.StartNew();
        BuildScriptTestRepository? repository = null;
        Exception? primary = null;
        try
        {
            repository = BuildScriptTestRepository.Create(frameworkFixture: true);
            repository.SeedArtifactDirectories();
            var arguments = new List<string> { "-Release", "-Clean", "-Test" };
            if (legacyOnly) arguments.Add("-LegacyOnly");
            var result = await repository.RunFrameworkBuildAsync(clock, failFramework, arguments.ToArray());
            AssertEqual(failFramework ? 31 : 0, result.ExitCode, "Combined build preserves the framework outcome");
            var legacy = repository.ReadDotnetInvocations().Where(line => line.StartsWith("run ", StringComparison.Ordinal)).ToArray();
            AssertEqual(1, legacy.Length, "Combined build executes the legacy runner exactly once");
            AssertEqual(true, legacy[0].Contains("-c Release -- --legacy|", StringComparison.Ordinal),
                "The unfiltered Release invocation uses the explicit legacy entry without selectors");
            var framework = repository.ReadFrameworkInvocations();
            AssertEqual(legacyOnly ? 0 : 1, framework.Length, "The framework driver runs exactly once unless the outer coordinator owns it");
            if (!legacyOnly)
            {
                var fields = framework[0].Split('|');
                AssertEqual(4, fields.Length, "The framework process records its invocation and actual host");
                AssertEqual("Full", fields[0], "The combined build requests both complete framework catalogs");
                AssertEqual("Core", fields[1], "Windows PowerShell launches the framework phase in PowerShell Core");
                AssertEqual(true, int.TryParse(fields[2], out var major) && major >= 7, "The framework phase uses PowerShell 7 or later");
                AssertEqual("True", fields[3], "The inspected package exists before framework execution");
                var inspected = result.Output.IndexOf("All required files present.", StringComparison.Ordinal);
                var invoked = result.Output.IndexOf("framework-fixture: invoked", StringComparison.Ordinal);
                AssertEqual(true, inspected >= 0 && invoked > inspected, "Package inspection completes before framework execution");
            }
            AssertEqual(true, repository.RootPackageContains("plugin.json"), "The current inspected package remains available after the framework outcome");
            AssertEqual(true, ArtifactDirectories.All(path => !repository.ArtifactDirectoryExists(path)), "Deferred cleanup completes after the framework outcome");
            foreach (var project in new[] { "tools/FishAudio.Testing", "tests/FishAudio.Testing.Tests" })
                foreach (var output in new[] { "bin", "obj", ".artifacts" })
                    AssertEqual(false, repository.ArtifactDirectoryExists(Path.Combine(project, output)), "Deferred cleanup also removes framework build artifacts");
            _ = RemainingFixtureTime(clock);
        }
        catch (Exception error)
        {
            primary = error;
            repository?.RetainUnexpectedFailure();
            throw;
        }
        finally
        {
            try { repository?.Dispose(); }
            catch (Exception cleanup) when (primary is not null)
            {
                Console.Error.WriteLine("build-compatibility: cleanup failed (" + cleanup.GetType().Name + ").");
            }
        }
    }

    private static TimeSpan RemainingFixtureTime(Stopwatch clock)
    {
        var remaining = TimeSpan.FromSeconds(120) - clock.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("build-compatibility-fixture-root-deadline");
    }

    private sealed class BuildScriptTestRepository : IDisposable
    {
        private readonly string _fakeDotnetDirectory;
        private readonly string _invocationLogPath;
        private readonly string _frameworkLogPath;
        private bool _retainUnexpectedFailure;
        private bool _omitAudioDependency;
        private bool _omitOcticonsNotice;
        private bool _omitFlagIconsNotice;
        private string _omittedLocale = string.Empty;
        private string _omittedStreamingDependency = string.Empty;

        private BuildScriptTestRepository(string rootPath, string fakeDotnetDirectory, string invocationLogPath)
        {
            RootPath = rootPath;
            _fakeDotnetDirectory = fakeDotnetDirectory;
            _invocationLogPath = invocationLogPath;
            _frameworkLogPath = Path.Combine(rootPath, "framework-invocations.log");
        }

        internal string RootPath { get; }

        internal static BuildScriptTestRepository Create(bool frameworkFixture = false)
        {
            var rootPath = frameworkFixture ? CreateFrameworkFixtureDirectory() : CreateTempDirectory();
            var projectDirectory = Path.Combine(rootPath, ProjectName);
            var testProjectDirectory = Path.Combine(rootPath, "tests", TestProjectName);
            var fakeDotnetDirectory = Path.Combine(rootPath, "fake-dotnet");
            var invocationLogPath = Path.Combine(rootPath, "dotnet-invocations.log");

            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(testProjectDirectory);
            Directory.CreateDirectory(fakeDotnetDirectory);
            Directory.CreateDirectory(Path.Combine(rootPath, "tools"));
            File.Copy(FindRepoFile("build.ps1"), Path.Combine(rootPath, "build.ps1"));
            File.WriteAllText(Path.Combine(projectDirectory, $"{ProjectName}.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(testProjectDirectory, $"{TestProjectName}.csproj"), "<Project />");
            File.WriteAllText(
                Path.Combine(fakeDotnetDirectory, "dotnet.cmd"),
                "@powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%~dp0fake-dotnet.ps1\" %*\r\n");
            File.WriteAllText(
                Path.Combine(fakeDotnetDirectory, "fake-dotnet.ps1"),
                """
                $ErrorActionPreference = 'Stop'
                $repoRoot = $env:FISH_AUDIO_BUILD_TEST_REPO
                $logPath = $env:FISH_AUDIO_BUILD_TEST_LOG
                $projectName = 'STranslate.Plugin.Tts.FishAudio'
                $testProjectName = 'STranslate.Plugin.Tts.FishAudio.Tests'
                $argumentsText = $args -join ' '
                $configuration = if (
                    $argumentsText -match '(?i)(?:-c\s+Release|Configuration(?:=|:|\s)+Release)'
                ) {
                    'Release'
                }
                else {
                    'Debug'
                }
                $packageDirectory = Join-Path $repoRoot ".artifacts\$configuration"
                $relativeDirs = @(
                    'bin'
                    'obj'
                    '.artifacts'
                    (Join-Path $projectName 'bin')
                    (Join-Path $projectName 'obj')
                    (Join-Path $projectName '.artifacts')
                    (Join-Path (Join-Path 'tests' $testProjectName) 'obj')
                    (Join-Path (Join-Path 'tests' $testProjectName) 'bin')
                )

                if ($args.Count -gt 0 -and $args[0] -eq 'msbuild') {
                    Write-Output $packageDirectory
                    exit 0
                }

                $seedStates = foreach ($relativeDir in $relativeDirs) {
                    Test-Path -LiteralPath (Join-Path (Join-Path $repoRoot $relativeDir) 'seed.txt')
                }
                Add-Content -LiteralPath $logPath -Value "$($args -join ' ')|$($seedStates -join ',')"

                foreach ($relativeDir in $relativeDirs) {
                    $directory = Join-Path $repoRoot $relativeDir
                    New-Item -ItemType Directory -Path $directory -Force | Out-Null
                    Set-Content -LiteralPath (Join-Path $directory 'produced.txt') -Value 'produced'
                }

                if ($args.Count -gt 0 -and $args[0] -eq 'build') {
                    Add-Type -AssemblyName System.IO.Compression
                    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
                    $packagePath = Join-Path $packageDirectory "$projectName.spkg"
                    if (Test-Path -LiteralPath $packagePath) {
                        Remove-Item -LiteralPath $packagePath -Force
                    }

                    $stream = [System.IO.File]::Open(
                        $packagePath,
                        [System.IO.FileMode]::CreateNew,
                        [System.IO.FileAccess]::ReadWrite,
                        [System.IO.FileShare]::None)
                    $archive = New-Object System.IO.Compression.ZipArchive(
                        $stream,
                        [System.IO.Compression.ZipArchiveMode]::Create,
                        $false)
                    try {
                        $entryNames = @('plugin.json', "$projectName.dll", 'NAudio.Core.dll')
                        foreach ($locale in @('en', 'zh-cn', 'zh-tw', 'ja', 'ko', 'ru', 'tr')) {
                            foreach ($extension in @('xaml', 'json')) {
                                $resource = "Languages/$locale.$extension"
                                if ($env:FISH_AUDIO_BUILD_TEST_OMIT_LOCALE -ne $resource) {
                                    $entryNames += $resource
                                }
                            }
                        }
                        if ($env:FISH_AUDIO_BUILD_TEST_OMIT_OCTICONS -ne '1') {
                            $entryNames += 'Assets/Licenses/Octicons.LICENSE.txt'
                        }
                        if ($env:FISH_AUDIO_BUILD_TEST_OMIT_FLAGS -ne '1') {
                            $entryNames += 'Assets/Licenses/flag-icons.LICENSE.txt'
                        }
                        foreach ($streamingDependency in @('MessagePack.dll', 'MessagePack.Annotations.dll', 'Microsoft.NET.StringTools.dll')) {
                            if ($env:FISH_AUDIO_BUILD_TEST_OMIT_STREAMING -ne $streamingDependency) {
                                $entryNames += $streamingDependency
                            }
                        }
                        if ($env:FISH_AUDIO_BUILD_TEST_OMIT_AUDIO -ne '1') {
                            $entryNames += 'NAudio.WinMM.dll'
                        }
                        if (Test-Path -LiteralPath (Join-Path $packageDirectory 'debug-only.txt')) {
                            $entryNames += 'debug-only.txt'
                        }
                        foreach ($entryName in $entryNames) {
                            $entry = $archive.CreateEntry($entryName)
                            $writer = New-Object System.IO.StreamWriter($entry.Open())
                            try {
                                $writer.Write('test')
                            }
                            finally {
                                $writer.Dispose()
                            }
                        }
                    }
                    finally {
                        $archive.Dispose()
                        $stream.Dispose()
                    }
                }

                if ($args.Count -gt 0 -and
                    $args[0] -eq 'run' -and
                    $env:FISH_AUDIO_BUILD_TEST_FAIL_TESTS -eq '1') {
                    exit 23
                }
                """);

            File.WriteAllText(Path.Combine(rootPath, "tools", "Test.ps1"),
                """
                param([string]$Mode)
                Set-StrictMode -Version Latest
                $ErrorActionPreference = 'Stop'
                $package = Join-Path $env:FISH_AUDIO_BUILD_TEST_REPO 'STranslate.Plugin.Tts.FishAudio.spkg'
                $fields = @($Mode, $PSVersionTable.PSEdition, [string]$PSVersionTable.PSVersion.Major, [string][IO.File]::Exists($package))
                [IO.File]::AppendAllText($env:FISH_AUDIO_BUILD_TEST_FRAMEWORK_LOG, (($fields -join '|') + "`n"), [Text.UTF8Encoding]::new($false))
                foreach ($project in @('tools/FishAudio.Testing', 'tests/FishAudio.Testing.Tests')) {
                    foreach ($output in @('bin', 'obj', '.artifacts')) {
                        $directory = Join-Path $env:FISH_AUDIO_BUILD_TEST_REPO ($project + '/' + $output)
                        [IO.Directory]::CreateDirectory($directory) | Out-Null
                        [IO.File]::WriteAllText((Join-Path $directory 'framework-produced.txt'), 'produced')
                    }
                }
                Write-Output 'framework-fixture: invoked'
                if ($env:FISH_AUDIO_BUILD_TEST_FAIL_FRAMEWORK -eq '1') { exit 31 }
                exit 0
                """, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            return new BuildScriptTestRepository(rootPath, fakeDotnetDirectory, invocationLogPath);
        }

        private static string CreateFrameworkFixtureDirectory()
        {
            var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
            if (string.IsNullOrEmpty(configured)) return CreateTempDirectory();
            if (!Path.IsPathFullyQualified(configured) || !Directory.Exists(configured))
                throw new InvalidOperationException("build-compatibility-evidence-root-invalid");
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configured));
            for (var directory = new DirectoryInfo(parent); directory is not null; directory = directory.Parent)
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("build-compatibility-evidence-root-reparse");
            // Windows PowerShell 5.1 changes the argv of a .cmd executable
            // whose path exceeds MAX_PATH. Keep this fake-tool workspace in
            // the same short temp layout used by the legacy build fixtures;
            // the configured results directory may be much deeper.
            var name = "fa-build-" + Guid.NewGuid().ToString("N");
            var path = Path.Combine(Path.GetTempPath(), name);
            if (File.Exists(path) || Directory.Exists(path)) throw new InvalidOperationException("build-compatibility-evidence-root-exists");
            Directory.CreateDirectory(path);
            using var record = new FileStream(Path.Combine(parent, name + ".json"), FileMode.CreateNew, FileAccess.Write);
            System.Text.Json.JsonSerializer.Serialize(record, new { fixtureRoot = path });
            return path;
        }

        internal void SeedArtifactDirectories()
        {
            foreach (var relativePath in ArtifactDirectories)
            {
                var directory = Path.Combine(RootPath, relativePath);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "seed.txt"), "seed");
            }
        }

        internal void SeedDebugOnlyArtifact()
        {
            var directory = Path.Combine(RootPath, ".artifacts", "Debug");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "debug-only.txt"), "debug-only");
        }

        internal (int ExitCode, string Output) RunBuild(params string[] arguments)
        {
            return RunBuildCore(failTests: false, arguments);
        }

        internal (int ExitCode, string Output) RunBuildWithFailedTests(params string[] arguments)
        {
            return RunBuildCore(failTests: true, arguments);
        }

        internal (int ExitCode, string Output) RunBuildWithoutLocale(string resource)
        {
            _omittedLocale = resource;
            return RunBuild("-Release", "-Clean");
        }

        internal (int ExitCode, string Output) RunBuildWithoutOcticonsNotice()
        {
            _omitOcticonsNotice = true;
            return RunBuild("-Release", "-Clean");
        }

        internal (int ExitCode, string Output) RunBuildWithoutFlagIconsNotice()
        {
            _omitFlagIconsNotice = true;
            return RunBuild("-Release", "-Clean");
        }

        internal (int ExitCode, string Output) RunBuildWithoutAudioDependency()
        {
            _omitAudioDependency = true;
            return RunBuild("-Release", "-Clean");
        }

        internal (int ExitCode, string Output) RunBuildWithoutStreamingDependency(string dependency)
        {
            _omittedStreamingDependency = dependency;
            return RunBuild("-Release", "-Clean");
        }

        private ProcessStartInfo CreateBuildStartInfo(bool failTests, string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = RootPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(Path.Combine(RootPath, "build.ps1"));
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            startInfo.Environment["PATH"] = $"{_fakeDotnetDirectory};{startInfo.Environment["PATH"]}";
            startInfo.Environment["FISH_AUDIO_BUILD_TEST_REPO"] = RootPath;
            startInfo.Environment["FISH_AUDIO_BUILD_TEST_LOG"] = _invocationLogPath;
            startInfo.Environment["FISH_AUDIO_BUILD_TEST_FAIL_TESTS"] = failTests ? "1" : "0";
            startInfo.Environment["FISH_AUDIO_BUILD_TEST_OMIT_LOCALE"] = _omittedLocale;
            startInfo.Environment["FISH_AUDIO_BUILD_TEST_OMIT_OCTICONS"] = _omitOcticonsNotice ? "1" : "0";
            startInfo.Environment["FISH_AUDIO_BUILD_TEST_OMIT_FLAGS"] = _omitFlagIconsNotice ? "1" : "0";
            startInfo.Environment["FISH_AUDIO_BUILD_TEST_OMIT_AUDIO"] = _omitAudioDependency ? "1" : "0";
            startInfo.Environment["FISH_AUDIO_BUILD_TEST_OMIT_STREAMING"] = _omittedStreamingDependency;
            startInfo.Environment["FISH_AUDIO_BUILD_TEST_FRAMEWORK_LOG"] = _frameworkLogPath;
            startInfo.Environment["FISH_AUDIO_BUILD_TEST_FAIL_FRAMEWORK"] = "0";
            return startInfo;
        }

        private (int ExitCode, string Output) RunBuildCore(bool failTests, params string[] arguments)
        {
            var startInfo = CreateBuildStartInfo(failTests, arguments);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start PowerShell build-script test process.");
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, $"{standardOutput}{Environment.NewLine}{standardError}");
        }

        internal async Task<(int ExitCode, string Output)> RunFrameworkBuildAsync(Stopwatch clock, bool failFramework, params string[] arguments)
        {
            var startInfo = CreateBuildStartInfo(failTests: false, arguments);
            startInfo.CreateNoWindow = true;
            startInfo.Environment["FISH_AUDIO_BUILD_TEST_FAIL_FRAMEWORK"] = failFramework ? "1" : "0";
            _ = RemainingFixtureTime(clock);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("build-compatibility-process-start");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var completion = Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
            try
            {
                var warning = TimeSpan.FromSeconds(20) - clock.Elapsed;
                if (warning <= TimeSpan.Zero || await Task.WhenAny(completion, Task.Delay(warning)) != completion)
                    Console.Error.WriteLine("build-compatibility: same fixture process still running after twenty seconds.");
                await completion.WaitAsync(RemainingFixtureTime(clock));
                CaptureFrameworkOutput(stdout, stderr);
                _ = RemainingFixtureTime(clock);
                return (process.ExitCode, stdout.Result + Environment.NewLine + stderr.Result);
            }
            catch
            {
                var drain = Stopwatch.StartNew();
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(RemainingDrainTime(drain));
                    await Task.WhenAll(stdout, stderr).WaitAsync(RemainingDrainTime(drain));
                    CaptureFrameworkOutput(stdout, stderr);
                    _ = RemainingDrainTime(drain);
                }
                catch (Exception cleanup)
                {
                    Console.Error.WriteLine("build-compatibility: bounded process cleanup failed (" + cleanup.GetType().Name + ").");
                }
                throw;
            }
        }

        private void CaptureFrameworkOutput(Task<string> stdout, Task<string> stderr)
        {
            foreach (var item in new[] { ("build.stdout.log", stdout), ("build.stderr.log", stderr) })
            {
                var path = Path.Combine(RootPath, item.Item1);
                if (File.Exists(path) || !item.Item2.IsCompletedSuccessfully) continue;
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                output.Write(Encoding.UTF8.GetBytes(item.Item2.Result));
            }
        }

        private static TimeSpan RemainingDrainTime(Stopwatch clock)
        {
            var remaining = TimeSpan.FromSeconds(10) - clock.Elapsed;
            return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException("build-compatibility-fixture-drain-deadline");
        }

        internal string[] ReadFrameworkInvocations() => File.Exists(_frameworkLogPath) ? File.ReadAllLines(_frameworkLogPath) : [];

        internal void RetainUnexpectedFailure()
        {
            _retainUnexpectedFailure = true;
            try { Console.Error.WriteLine("build-compatibility: unexpected failure evidence retained as " + Path.GetFileName(RootPath) + "."); }
            catch { /* A diagnostic failure cannot replace the original fixture failure. */ }
            try
            {
                var configured = Environment.GetEnvironmentVariable("FISHAUDIO_COMPAT_EVIDENCE_ROOT");
                if (!string.IsNullOrEmpty(configured) && Path.IsPathFullyQualified(configured) && Directory.Exists(configured))
                    ZipFile.CreateFromDirectory(RootPath, Path.Combine(configured, Path.GetFileName(RootPath) + ".zip"));
            }
            catch { /* Preserve both the primary failure and the original workspace. */ }
        }

        internal bool FirstDotnetInvocationSawNoSeededArtifacts()
        {
            var states = ReadFirstDotnetInvocationSeedStates();
            return states.Length == ArtifactDirectories.Length
                && states.All(state => string.Equals(state, bool.FalseString, StringComparison.OrdinalIgnoreCase));
        }

        internal bool FirstDotnetInvocationSawEverySeededArtifact()
        {
            var states = ReadFirstDotnetInvocationSeedStates();
            return states.Length == ArtifactDirectories.Length
                && states.All(state => string.Equals(state, bool.TrueString, StringComparison.OrdinalIgnoreCase));
        }

        internal bool ArtifactDirectoryExists(string relativePath) =>
            Directory.Exists(Path.Combine(RootPath, relativePath));

        internal bool DotnetWasInvoked() => File.Exists(_invocationLogPath);

        internal string[] ReadDotnetInvocations() =>
            File.Exists(_invocationLogPath) ? File.ReadAllLines(_invocationLogPath) : [];

        internal bool RootPackageContains(string entryName)
        {
            var packagePath = Path.Combine(RootPath, $"{ProjectName}.spkg");
            using var archive = ZipFile.OpenRead(packagePath);
            return archive.Entries.Any(entry => string.Equals(entry.FullName, entryName, StringComparison.Ordinal));
        }

        private string[] ReadFirstDotnetInvocationSeedStates()
        {
            var firstInvocation = File.ReadLines(_invocationLogPath).First();
            return firstInvocation.Split('|', 2)[1].Split(',');
        }

        public void Dispose()
        {
            if (_retainUnexpectedFailure) return;
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }

    private static bool InvokesDebugPackagingTarget(string invocation) =>
        invocation.Contains("-t:PackageAsSpkg", StringComparison.Ordinal)
        || invocation.Contains("-t PackageAsSpkg", StringComparison.Ordinal);
}
