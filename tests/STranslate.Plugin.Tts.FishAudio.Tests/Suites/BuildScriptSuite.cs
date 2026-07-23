using System.Diagnostics;
using System.IO.Compression;
using static TestAssertions;

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

    internal static void ConfigurationParameterIsRejected()
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

    internal static void ReleaseSwitchSelectsReleaseWithoutDebugPackaging()
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

    internal static void OrdinaryBuildCleansEveryArtifactDirectoryBeforeBuilding()
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

    internal static void CleanBuildCleansEveryArtifactDirectoryAfterBuilding()
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

    internal static void CleanBuildCleansAfterFailedRegressionTests()
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

    private sealed class BuildScriptTestRepository : IDisposable
    {
        private readonly string _fakeDotnetDirectory;
        private readonly string _invocationLogPath;

        private BuildScriptTestRepository(string rootPath, string fakeDotnetDirectory, string invocationLogPath)
        {
            RootPath = rootPath;
            _fakeDotnetDirectory = fakeDotnetDirectory;
            _invocationLogPath = invocationLogPath;
        }

        internal string RootPath { get; }

        internal static BuildScriptTestRepository Create()
        {
            var rootPath = CreateTempDirectory();
            var projectDirectory = Path.Combine(rootPath, ProjectName);
            var testProjectDirectory = Path.Combine(rootPath, "tests", TestProjectName);
            var fakeDotnetDirectory = Path.Combine(rootPath, "fake-dotnet");
            var invocationLogPath = Path.Combine(rootPath, "dotnet-invocations.log");

            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(testProjectDirectory);
            Directory.CreateDirectory(fakeDotnetDirectory);
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
                        $entryNames = @('plugin.json', "$projectName.dll")
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

            return new BuildScriptTestRepository(rootPath, fakeDotnetDirectory, invocationLogPath);
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

        private (int ExitCode, string Output) RunBuildCore(bool failTests, params string[] arguments)
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

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start PowerShell build-script test process.");
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, $"{standardOutput}{Environment.NewLine}{standardError}");
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
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }

    private static bool InvokesDebugPackagingTarget(string invocation) =>
        invocation.Contains("-t:PackageAsSpkg", StringComparison.Ordinal)
        || invocation.Contains("-t PackageAsSpkg", StringComparison.Ordinal);
}
