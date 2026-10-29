# Contributor instructions

Preserve unrelated changes and inspect the working tree before editing. Applicable ancestor and local AGENTS files apply together. Read the conditional owners below only when their operation is relevant; this entry supplies the common boundaries.

Use UTF-8 and the surrounding indentation and line endings. Keep changes focused, preserve the public plugin identity and host bindings, and use a conventional branch such as `feat/streaming-topic`, `fix/preview-topic` or `chore/release-topic`, without a `codex/` prefix. Classify commits by their actual change. Commit/push/PR work and release publication require the applicable user authorization; configuring automation does not authorize a release tag.

This is a Windows/WPF .NET 10 plugin using STranslate.Plugin 1.0.15. `Main` is the composition root; keep domain ownership and lifecycle boundaries intact. Persist settings through `SettingsStore` and its active-generation write lease. Capture operation settings, propagate cancellation, reject stale results, own disposable resources and preserve the original failure when cleanup fails.

Keep credentials, private text, signed resource URLs and personal machine paths out of public files and diagnostics. Ordinary behavioral regressions are offline and require neither a Fish Audio key nor an audio device; CI dependency audits and code scanning have their own external service boundaries. Never silently retry synthesis or select another model. Real synthesis needs the API evidence policy's explicit cost controls. Preserve raw evidence bytes and their `.gitattributes` treatment; do not run blanket text normalization over captured data.

Use meaningful behavior/compatibility regressions at fake network, clock, storage and audio boundaries. Add ordinary NUnit cases with framework attributes and stable named parameter rows; they require no central registration. Existing legacy scenarios retain their catalog, assertions and process supervision during migration. Every commit uses the installed pre-commit gate to verify an isolated snapshot of the exact staged tree; `bootstrap/full/hybrid` checks the Release package, complete legacy catalog and both complete NUnit catalogs. The final commit-msg guard rejects index, retained hybrid-evidence or physical-inventory changes after validation without repeating tests. The verification policy and [test-system specification](docs/specs/test-system.md) own hook upgrades, case identities and later baseline/impact-analysis boundaries. There is no test gate at pre-push. The full combined Windows check remains `./build.ps1 -Release -Clean -Test`. Report failures and verification limits accurately. A leftover package or software output-start signal is not proof of a successful current build or audible output.

## Read before the relevant action

| Before | Canonical owner |
|---|---|
| Editing C# behavior, settings, concurrency or UI bindings | [C# policy](docs/policies/csharp.md), the affected [current domain specification](docs/specs/README.md), and its linked ADRs; use [glossary terms](docs/glossary.md). |
| Designing or modifying tests, choosing verification or claiming completion | [Verification policy](docs/policies/verification.md). |
| Changing streaming protocol, text preparation or playback | [Streaming validation policy](docs/policies/streaming-validation.md) and the affected streaming specs. |
| Calling real Fish Audio APIs, handling keys or publishing response evidence | [API evidence policy](docs/policies/api-evidence.md). |
| Editing README, guides, maintenance docs or moving their assets | [Documentation policy](docs/policies/documentation.md); README and guides have five languages, changelog prose is Chinese, other maintenance docs are English. |
| Editing AGENTS/Skill instructions, instruction references or loading/layout rules | [Instruction progressive disclosure](docs/policies/documentation.md#instruction-progressive-disclosure). |
| Editing CHANGELOG, preparing a release, tagging or publishing | [Release policy](docs/policies/releases.md), including the published stable baseline and net-change workflow. |
| Creating/editing a PR or preparing its squash message | [PR template](.github/PULL_REQUEST_TEMPLATE.md) and [repository governance](docs/policies/repository-governance.md). |
| Changing workflow checks, branch protections or rulesets | [Repository governance](docs/policies/repository-governance.md); verify actual successful check contexts before requiring them. |

For the human setup/contribution path, use [CONTRIBUTING](CONTRIBUTING.md). Specs describe current implementation; references describe provider contracts; dated evidence describes observations; current/history ADRs explain active/replaced decisions. Follow those owners instead of copying their procedures into this entry.
