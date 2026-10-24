# Development and contributions

This is the developer entry point for Fish Audio TTS for STranslate. For installation and configuration, use the [user overview](README.md) and [user guide](docs/user-guide/README.en.md). This project targets Windows/WPF and .NET 10, uses STranslate.Plugin 1.0.15, and maintains the public plugin identity, settings compatibility and localized UI contracts.

The root [AGENTS.md](AGENTS.md) supplies common repository boundaries and a conditional reading map. Follow the canonical owners below for the operation at hand instead of loading or copying every policy into each task.

## Start with the current contract

Read the [domain glossary](docs/glossary.md) and [current specification index](docs/specs/README.md) before changing behavior. Each specification links its code owners and regression suites:

- [Architecture and initialization lifecycle](docs/specs/architecture-and-lifecycle.md): state publication, settings write leases and operation snapshots.
- [Settings and model policy](docs/specs/settings-and-model-policy.md): schema interpretation, normalization, format migration, removal of obsolete proxy fields and time-dependent defaults.
- [Formal TTS and credit](docs/specs/tts-and-credit.md): entry preflight, replacement, cancellation and account activity.
- [Native streaming session](docs/specs/streaming-session.md), [text preparation](docs/specs/streaming-text.md) and [incremental playback](docs/specs/streaming-playback.md): protocol, ownership, resource bounds and output completion.
- [Voice browsing and preview](docs/specs/voice-browsing-and-preview.md): search windows, magnifier lookup versus tick Apply/cross Clear, ID-only local favorites, shared selected/browse-card state, metadata, sample URLs and covers. Tick/cross state synchronizes by voice ID across the selected card, Search, By ID and Favorites; any matching cross clears selection while favorites remain independent. Input Paste controls sit inside the right edge without overlapping text. [DD-054](docs/adr/current/DD-054.md) records the favorite/selection ownership tradeoff. Regressions cover [favorite state](tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/FavoriteVoicesSuite.cs), [ID-file storage and metadata cache](tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/FavoriteStorageCacheSuite.cs), [settings integration](tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/FavoriteSettingsIntegrationSuite.cs) and [settings layout](tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/SettingsLayoutSuite.cs). Follow the domain spec for the ordered-ID file and 300-entry LRU / fifteen-minute monotonic client TTL; do not reintroduce bookmark metadata into host settings. Signed-URL refresh bypasses metadata caching, and clearing shared persistent covers preserves IDs and metadata.
- [Build and release](docs/specs/build-and-release.md): package content, command modes, CI and tagged publication.

Use the [ADR register](docs/adr/README.md) for rationale and current/history relationships. Historical decisions explain replaced behavior; they are not instructions to restore it.

## Maintenance policies

Apply the canonical policies rather than copying their rules into a new contributor checklist:

| Policy | Use it for |
|---|---|
| [C# maintenance](docs/policies/csharp.md) | Ownership, cancellation, concurrency, settings I/O, localization and diagnostics. |
| [Documentation](docs/policies/documentation.md) | Document roles, language, links, ADR amendments and historical releases. |
| [Verification](docs/policies/verification.md) | Meaningful tests, final-state checks and distributable dependencies. |
| [Versions and releases](docs/policies/releases.md) | Published stable baseline, net changelog categories, release notes and guarded tag publication. |
| [Repository governance](docs/policies/repository-governance.md) | Conventional subjects, PRs, squash references, real required checks and main rules. |
| [Streaming verification](docs/policies/streaming-validation.md) | Framing, decoding, backpressure, startup, drain and failure cleanup. |
| [API evidence and zero-cost testing](docs/policies/api-evidence.md) | Live-run authorization, exact free-model guards, redaction and measured boundaries. |

Provider contracts belong in [API references](docs/references/README.md); actual observations belong in the [evidence index](docs/evidences/README.md). A provider capability, passing offline scenario and physical audio measurement support different conclusions. The [probe guide](tools/FishAudioProbe/README.md) documents the evidence tool; ordinary development does not require a credential or live synthesis.

## Prepare and build

Use Windows, the .NET 10 SDK and PowerShell. Clone the repository, create a focused branch from current main, and inspect existing changes before editing. The distributable version is declared in [plugin.json](STranslate.Plugin.Tts.FishAudio/plugin.json); the `STranslate.Plugin` SDK dependency version is separate.

Run the combined verification from the repository root:

```powershell
.\build.ps1 -Release -Clean -Test
```

It builds Release, checks the current distributable, runs the executable regression runner and cleans intermediates afterward. The copied `STranslate.Plugin.Tts.FishAudio.spkg` remains in the repository root. A package left by an earlier run is not evidence that the current command passed.

For local iteration:

```powershell
# Debug is the default
.\build.ps1

# Debug plus regression runner, with cleanup afterward
.\build.ps1 -Clean -Test

# Cleanup only
.\build.ps1 -CleanOnly
```

The regression project uses an explicit [Program entry point](tests/STranslate.Plugin.Tts.FishAudio.Tests/Program.cs), not test discovery. Register meaningful new scenarios there. Normal tests use fakes and local fixtures and should remain deterministic and offline. For probe changes, run its documented offline self-tests. See the verification policies for device/host acceptance, final-state reruns and package runtime checks.

## Make a focused change

Keep changes scoped to the behavior being addressed, preserving established public bindings and host contracts unless replacement is explicit. Add regression coverage for the changed failure or compatibility boundary; do not add source-text assertions that simply restate an implementation. Keep private runtime dependencies in the actual plugin package without copying a second host SDK or UI framework.

Update the relevant current specification in the same change. Record a new ADR when a lasting tradeoff or ownership choice needs rationale, using the next unused permanent number; amend or supersede older decisions precisely. New settings need complete migration, conversion, normalization, copy/rollback, UI and round-trip handling. Follow the C# policy for those contracts.

Maintain all seven host UI locales (Simplified Chinese, Traditional Chinese, English, Japanese, Korean, Russian and Turkish) for persistent UI text. README and user guides have Simplified Chinese, Traditional Chinese, English, Japanese and Korean versions; other maintenance documents are English, and new user-visible net changes go in Chinese under `CHANGELOG.md` Unreleased with applicable English categories. Verify the latest published stable baseline under the release policy before editing it. Published release sections retain their historical content. Check relative links and assets after moving documentation.

## Community and reporting

Follow the [code of conduct](CODE_OF_CONDUCT.md). Use [SUPPORT.md](SUPPORT.md) for questions and ordinary bug reports; report suspected vulnerabilities privately under [SECURITY.md](SECURITY.md).

## Report an issue

Search [existing issues](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/issues) first. A useful report includes plugin and STranslate versions, Windows version, concrete reproduction steps, expected/actual behavior and whether the problem involves search, preview or formal speech. Include the model, audio format, relevant system-network conditions and output device. Formal speech always uses the system default proxy; there is no plugin proxy mode/address UI. Obsolete stored proxy fields are ignored and canonically removed within schema 2, without another version bump.

Sanitize diagnostics before posting. Do not include API Keys, Authorization headers, proxy passwords, full signed URLs, private voice text or account identifiers. A successful search does not prove the streaming route works; a network audio event does not establish audible output. State what you observed and how.

## Submit a pull request

Open a focused PR against main using the [PR template](.github/PULL_REQUEST_TEMPLATE.md) and link the issue when one exists. Explain the concrete problem, resulting behavior, compatibility implications and relevant validation. Include commands/results, what file state they cover, and any skipped host/device/live checks. Keep the PR's own number out of its title; the final squash subject retains `(#PR-number)`, including when explicitly supplied through a CLI. Screenshots are useful for a rendered UI change when they show the changed behavior and contain no secrets; they are not required for unrelated code changes.

Run affected checks after the final code, resource, documentation and changelog edits. Keep generated packages, build intermediates, credentials and local runtime artifacts out of the commit. Respond to review with evidence; update the PR description if its final scope changes. The standard PR/main validation workflow verifies builds and tests; tagged release publication is a separate maintainer action.

Contributions are under the repository's [MIT license](LICENSE).
