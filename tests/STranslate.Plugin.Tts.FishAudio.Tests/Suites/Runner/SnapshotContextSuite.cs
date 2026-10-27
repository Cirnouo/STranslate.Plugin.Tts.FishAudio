using System.Text.Json;
using System.Security.Cryptography;
using static TestAssertions;

internal static class SnapshotContextSuite
{
    internal static void Run()
    {
        var original = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        var directory = CreateTempDirectory();
        Exception? primary = null;
        try
        {
            var context = Path.Combine(directory, "context.json");
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", context);
            ExpectFailure(() => FindRepoFile("AGENTS.md"), "An absent snapshot context cannot borrow a checkout file");
            WriteContext(directory, Array.Empty<object>());
            ExpectFailure(() => RegressionSource.CaptureAsync().GetAwaiter().GetResult(),
                "A foreign root cannot relabel the executing runner's source");
            WriteContext(Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName, Array.Empty<object>());
            ExpectFailure(() => RegressionSource.CaptureAsync().GetAwaiter().GetResult(),
                "An ancestor of the runner directory is not an exact snapshot source owner");
            var ownedRoot = RegressionSource.FindSourceRoot() ?? throw new InvalidDataException("Missing fixture source owner.");
            WriteContext(ownedRoot, Array.Empty<object>());
            ExpectFailure(() => FindRepoFile("AGENTS.md"), "A missing tracked snapshot input cannot escape into a checkout");
            ExpectFailure(() => FindRepoPath("../AGENTS.md"), "Snapshot paths reject parent traversal");
            var missing = FindRepoPath("absent-owned-input.cs");
            AssertEqual(Path.Combine(ownedRoot, "absent-owned-input.cs"), missing,
                "Negative existence checks remain bound to the explicit snapshot root");
            var ownedName = "snapshot-input-" + Guid.NewGuid().ToString("N") + ".txt";
            var ownedPath = Path.Combine(ownedRoot, ownedName);
            Exception? inputFailure = null;
            try
            {
                File.WriteAllText(ownedPath, "owned");
                ExpectFailure(() => FindRepoPath(ownedName), "Existing unlisted files cannot become repository inputs");
                WriteContext(ownedRoot, new object[] { new { path = ownedName, blob = new string('c', 40),
                    sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(ownedPath))) } });
                AssertEqual(ownedPath, FindRepoFile(ownedName), "A listed input with matching bytes resolves");
                File.WriteAllText(ownedPath, "changed");
                ExpectFailure(() => FindRepoFile(ownedName), "Changed input bytes fail the source contract");
                ExpectFailure(() => FindRepoPath(ownedName), "The path API cannot bypass input byte validation");
            }
            catch (Exception failure) { inputFailure = failure; throw; }
            finally { try { File.Delete(ownedPath); } catch when (inputFailure is not null) { } }

            var link = Path.Combine(ownedRoot, "snapshot-junction-" + Guid.NewGuid().ToString("N"));
            Exception? linkFailure = null;
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                foreach (var argument in new[] { "/c", "mklink", "/J", link, directory }) start.ArgumentList.Add(argument);
                using var process = System.Diagnostics.Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                Exception? processFailure = null;
                try
                {
                    AssertEqual(true, process.WaitForExit(10000), "A local junction fixture completes");
                    Task.WhenAll(output, error).GetAwaiter().GetResult();
                    AssertEqual(0, process.ExitCode, "The local junction fixture is available");
                }
                catch (Exception failure) { processFailure = failure; throw; }
                finally
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            if (!process.WaitForExit(10000)) throw new IOException("Junction fixture did not terminate.");
                        }
                        Task.WhenAll(output, error).GetAwaiter().GetResult();
                    }
                    catch when (processFailure is not null) { }
                }
                WriteContext(ownedRoot, Array.Empty<object>());
                ExpectFailure(() => FindRepoPath(Path.GetFileName(link)), "A terminal directory junction cannot escape snapshot ownership");
            }
            catch (Exception failure) { linkFailure = failure; throw; }
            finally { try { if (Directory.Exists(link)) Directory.Delete(link); } catch when (linkFailure is not null) { } }

            void WriteContext(string root, object[] files) => File.WriteAllText(context, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, root, tree = new string('a', 40),
                sourceCommit = new string('b', 40), files
            }));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", original);
            try { Directory.Delete(directory, recursive: true); }
            catch when (primary is not null) { }
        }
    }

    private static void ExpectFailure(Action action, string message)
    {
        var failed = false;
        try { action(); }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException) { failed = true; }
        AssertEqual(true, failed, message);
    }

    internal static void VerifierRequiresCompleteCatalog()
    {
        var original = Environment.GetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT");
        var directory = CreateTempDirectory();
        Exception? primary = null;
        try
        {
            var contextPath = Path.Combine(directory, "snapshot.json");
            var tree = new string('a', 40);
            File.WriteAllText(contextPath, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, root = RegressionSource.FindSourceRoot(), tree,
                sourceCommit = new string('b', 40), files = Array.Empty<object>()
            }));
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", contextPath);
            var cases = RegressionCatalog.Create();
            var complete = Report();
            var valid = Save(complete, "valid.json");
            if (RegressionReport.BuildMetadata("RegressionBuildConfiguration") != "Release")
            {
                ExpectFailure(() => RegressionSnapshotVerifier.Run(["--verify-snapshot-report", valid, tree]),
                    "A non-Release verifier cannot authorize Release commit evidence");
                return;
            }
            AssertEqual(0, RegressionSnapshotVerifier.Run(["--verify-snapshot-report", valid, tree]),
                "Complete same-source ordered catalog evidence passes metadata verification");
            foreach (var mutation in new[] { "subset", "reordered", "not-run", "source", "sdk", "unfinished" })
            {
                var report = Report();
                switch (mutation)
                {
                    case "subset": report.Cases.RemoveAt(report.Cases.Count - 1); break;
                    case "reordered": report.Cases.Reverse(); break;
                    case "not-run": report.Cases[0].Outcome = "not-run"; break;
                    case "source": report.Source.Tree = new string('c', 40); break;
                    case "sdk": report.Sdk = "0.0.0"; break;
                    case "unfinished": report.Complete = false; break;
                }
                var path = Save(report, mutation + ".json");
                ExpectFailure(() => RegressionSnapshotVerifier.Run(["--verify-snapshot-report", path, tree]),
                    "Incomplete or mismatched evidence cannot satisfy a full commit gate");
            }

            RegressionReport Report() => new()
            {
                Source = SnapshotContext.Read()!.Identity(), Complete = true, FinishedUtc = DateTimeOffset.UtcNow,
                Cases = cases.Select(item => new RegressionCaseResult { Id = item.Id, Partition = item.Partition, Outcome = "passed" }).ToList()
            };
            string Save(RegressionReport report, string name)
            {
                var path = Path.Combine(directory, name);
                report.Create(path);
                report.Write(path);
                return path;
            }
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            Environment.SetEnvironmentVariable("FISHAUDIO_SNAPSHOT_CONTEXT", original);
            try { Directory.Delete(directory, recursive: true); }
            catch when (primary is not null) { }
        }
    }
}
