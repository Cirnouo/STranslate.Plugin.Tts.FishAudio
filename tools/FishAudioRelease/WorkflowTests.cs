namespace FishAudioRelease;

internal static class WorkflowTests
{
    internal static void Run(string root)
    {
        var validation = File.ReadAllText(Path.Combine(root, ".github", "workflows", "validation.yml"));
        var release = File.ReadAllText(Path.Combine(root, ".github", "workflows", "dotnet.yml"));
        SelfTest.Equal(true, validation.Contains("value: ${{ jobs.build-test-package.outputs.package-artifact }}", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("package-artifact: ${{ steps.package-artifact.outputs.name }}", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("verified-package-${{ github.sha }}-${{ github.run_id }}-${{ github.run_attempt }}", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("name: ${{ steps.package-artifact.outputs.name }}", StringComparison.Ordinal));
        SelfTest.Equal(true, release.Contains("name: ${{ needs.verify.outputs.package-artifact }}", StringComparison.Ordinal));
        SelfTest.Equal(false, release.Contains("github.run_attempt", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("PR_TITLE: ${{ github.event.pull_request.title }}", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("$subjects += $env:PR_TITLE", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("PR commit identities must be exact SHA values", StringComparison.Ordinal));
        var canonical = validation.IndexOf("--canonicalize-package", StringComparison.Ordinal);
        var verify = validation.IndexOf("--verify", StringComparison.Ordinal);
        var upload = validation.IndexOf("uses: actions/upload-artifact@", StringComparison.Ordinal);
        SelfTest.Equal(true, canonical >= 0 &&
            canonical < verify &&
            verify < upload);
        Console.WriteLine("Validation attempt artifact output mapping tests passed.");
    }
}
