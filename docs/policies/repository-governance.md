# Repository governance

This policy adapts the effective main rules of [debugging-cdp-targets](https://github.com/Cirnouo/debugging-cdp-targets) to the Fish Audio repository. Product-specific JavaScript, Chrome and macOS checks do not become nonexistent required Fish checks.

## Branch and pull requests

Use a conventional lowercase branch with a focused kebab-case topic and no `codex/` prefix. Use Conventional Commit subjects, for example `fix(preview): cancel stale downloads` or `docs(guides): explain audio formats`. Keep commits independently reviewable. The final squash subject is `<PR title> (#<PR number>)`, retaining GitHub's PR reference. Keep that suffix out of the PR title itself. When a CLI supplies an explicit merge subject, include the suffix; an explicit bare title overrides GitHub's normal generated reference. Read back the merged subject and its PR association.

Use the [PR template](../../.github/PULL_REQUEST_TEMPLATE.md) to describe the problem, final behavior, validation and relevant documentation. Include closing keywords in the PR description for an issue actually resolved by the change. Report final commands/results and real-platform limits. When changing the changelog, include the verified published stable baseline and category reasoning under [the release policy](releases.md#changelog-editing-workflow).

Require PRs into main, linear history and squash-only merging. The required approval count is zero, matching the reference solo-maintainer policy; review evidence remains part of the contribution workflow. Repository merge defaults use the PR title and an empty generated body. Curate any intentionally supplied squash body rather than copying a development transcript.

## Main rules

The intended active branch ruleset applies only to `refs/heads/main`, with no exclusions or bypass actors, including administrators:

- Disallow deletion and non-fast-forward updates.
- Require linear history and PRs with squash as the only allowed merge method.
- Require actual GitHub Actions checks with strict up-to-date status enforcement, including on branch creation.
- Require CodeQL scanning and block high/critical security alerts; ordinary nonsecurity alerts are not a separate block.

Match the reference's remaining PR-review flags without adding an unrelated approval requirement. Keep auto-merge and automatic branch deletion disabled. A ruleset can enforce these rules without legacy/classic branch protection; inspect effective rules rather than interpreting a classic-protection 404 as an unprotected branch.

## Enable checks before requiring them

Implement and successfully run the target's real Windows build/test/package, commit-message, NuGet supply-chain and C#/Actions CodeQL checks on PR and main before making their exact emitted contexts required. Record the actual check name and GitHub Actions integration ID. Do not copy the reference's Node, portable macOS or Real Chrome context strings into this repository.

Review the initial CodeQL results before enabling the scanning gate. A completed workflow, skipped job, unavailable audit service or missing scanner result is not evidence of a successful scan. Normal checks must not need Fish Audio credentials or a physical sound card. Keep publication scoped to the authorized tag workflow, separate from PR/main validation.

Before changing remote settings, read the current repository metadata, configured rulesets, effective main rules and legacy protection. Preserve recoverable pre-change JSON privately, construct portable write payloads without server-generated identity fields, and retain unrelated settings. Apply only the authorized scope, then read back the repository settings, full ruleset and effective main rules. Report the applied check mapping and any deliberate host-specific adaptation. Do not temporarily grant yourself a bypass to merge failing changes.
