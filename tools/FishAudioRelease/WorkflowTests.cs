using System.Text.RegularExpressions;

namespace FishAudioRelease;

internal static class WorkflowTests
{
    internal static void Run(string root)
    {
        var validation = File.ReadAllText(Path.Combine(root, ".github", "workflows", "validation.yml")).Replace("\r\n", "\n");
        var release = File.ReadAllText(Path.Combine(root, ".github", "workflows", "dotnet.yml")).Replace("\r\n", "\n");
        SelfTest.Equal(true, validation.Contains("value: ${{ jobs.build-test-package.outputs.package-artifact }}", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("package-artifact: ${{ steps.package-artifact.outputs.name }}", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("verified-package-${{ github.sha }}-${{ github.run_id }}-${{ github.run_attempt }}", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("name: ${{ steps.package-artifact.outputs.name }}", StringComparison.Ordinal));
        SelfTest.Equal(true, release.Contains("name: ${{ needs.verify.outputs.package-artifact }}", StringComparison.Ordinal));
        SelfTest.Equal(false, release.Contains("github.run_attempt", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("PR_TITLE: ${{ github.event.pull_request.title }}", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("$subjects += $env:PR_TITLE", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("PR commit identities must be exact SHA values", StringComparison.Ordinal));

        var gate = validation[Position(validation, "\n  build-test-package:")..];
        var canonical = Position(gate, "      - name: Canonicalize package container and verify unchanged payload identity");
        var verify = Position(gate, "      - name: Validate release event, annotated tag and packaged manifest offline");
        var upload = Position(gate, "      - name: Preserve exact verified release package");
        SelfTest.Equal(true, canonical < verify && verify < upload);
        var verifyStep = gate[verify..upload];
        SelfTest.Equal(true, Regex.IsMatch(verifyStep, @"(?m)^\s+run: dotnet run --project tools/FishAudioRelease -c Release -- --verify\s*$"));
        SelfTest.Equal(true, gate[upload..].Contains("uses: actions/upload-artifact@", StringComparison.Ordinal));
        SelfTest.Equal(true, Position(gate, "Require every validation prerequisite to succeed") < Position(gate, "Checkout exact validation commit"));
        SelfTest.Equal(true, Position(gate, "VerifyHybridCi.ps1") < Position(gate, "run: .\\build.ps1 -Release"));
        SelfTest.Equal(true, gate.Contains("if: ${{ always() }}", StringComparison.Ordinal));
        SelfTest.Equal(true, gate.Contains("needs: [supply-chain-security, commit-messages, hybrid-validation]", StringComparison.Ordinal));
        SelfTest.Equal(true, gate.Contains("HYBRID_RESULT: ${{ needs.hybrid-validation.result }}", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains(".\\tools\\TestHybrid.ps1 -RepositoryRoot", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains(".\\tools\\Assert-HybridSession.ps1 -RepositoryRoot", StringComparison.Ordinal));
        SelfTest.Equal(true, validation.Contains("hybrid-report-${{ github.sha }}-${{ github.run_id }}-${{ github.run_attempt }}", StringComparison.Ordinal));
        SelfTest.Equal(false, validation.Contains("VerifyFrameworkCi.ps1", StringComparison.Ordinal));
        var hybrid = validation[Position(validation, "\n  hybrid-validation:")..Position(validation, "\n  build-test-package:")];
        SelfTest.Equal(true, Position(hybrid, "Run complete hybrid validation") < Position(hybrid, "Independently admit complete hybrid session"));
        SelfTest.Equal(true, Position(hybrid, "Independently admit complete hybrid session") < Position(hybrid, "Record this hybrid attempt owner"));
        SelfTest.Equal(true, hybrid.Contains("timeout-minutes: 90", StringComparison.Ordinal));
        SelfTest.Equal(true, gate.Contains("GetRegressionJobAttempts.ps1", StringComparison.Ordinal));
        SelfTest.Equal(true, gate.Contains("actions: read", StringComparison.Ordinal));
        SelfTest.Equal(true, gate.Contains("github-token: ${{ github.token }}", StringComparison.Ordinal) &&
            gate.Contains("run-id: ${{ github.run_id }}", StringComparison.Ordinal) && gate.Contains("merge-multiple: false", StringComparison.Ordinal));
        var caller = release[Position(release, "\n  verify:")..Position(release, "\n  publish:")];
        SelfTest.Equal(true, caller.Contains("contents: read", StringComparison.Ordinal) && caller.Contains("actions: read", StringComparison.Ordinal));
        SelfTest.Equal(false, validation.Contains("contents: write", StringComparison.Ordinal));
        Console.WriteLine("Validation attempt artifact output and final release gate contracts passed.");
    }

    private static int Position(string text, string value)
    {
        var index = text.IndexOf(value, StringComparison.Ordinal);
        SelfTest.Equal(true, index >= 0);
        return index;
    }
}
