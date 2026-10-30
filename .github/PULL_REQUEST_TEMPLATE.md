## Problem

Describe the concrete problem or link the issue.

## Resulting behavior

Explain the final change and relevant compatibility, resource or security implications.

## Verification

List checks actually run, their results, the final file state they cover and remaining host/device/provider/publication limits.

- [ ] The installed protocol-3 pre-commit gate and final commit-msg receipt guard passed for the exact staged tree in `bootstrap/full/hybrid` mode, including both complete NUnit catalogs; its tree identity and full PR CI results are reported separately.
- [ ] Any focused diagnostic runs state their selected scope and remaining not-run coverage; they do not replace the commit gate.
- [ ] Relevant offline probe/release-tool checks pass when changed.
- [ ] The current distributable and its runtime dependencies were checked when applicable.
- [ ] Changelog changes identify the latest published stable baseline, authoritative release evidence and why the categories describe final net changes.

## Documentation

- [ ] Relevant current specs, ADRs and localized user guidance are updated when applicable.
- [ ] Unreleased contains meaningful cumulative changes rather than development iterations; published history is preserved.
- [ ] The title/commits follow the repository's conventional subject policy.

<!-- Link resolved issues with Closes/Fixes/Resolves #NUMBER. Do not append this PR's own (#NUMBER) to its raw title. The repository squash default is PR_TITLE + BLANK; the final merged subject retains GitHub's (#PR-number) suffix, including when explicitly supplied through a CLI. Enabling publication automation does not authorize creating a tag or Release. -->
