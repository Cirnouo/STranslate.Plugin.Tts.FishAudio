# Verification and release policy

Choose verification that exercises the changed contract. The repository uses an executable regression runner, not a test-discovery framework: new regression scenarios must be invoked by its Program entry point. Keep normal tests deterministic and offline; do not make a credential or provider availability a prerequisite for CI.

- Use fake network/storage/audio boundaries for request shape, cancellation, timeouts, stale results and failure cleanup. Use fixed local fixtures when real decoding is relevant.
- For documentation moves, validate local targets, preserved anchors and the existing repository assertions affected by the move. Do not add large sets of source/text assertions that merely mirror the documents.
- For behavioral changes, test meaningful before/after cases and affected compatibility paths. Repeat or broaden checks when the final changes, a failure or an unresolved risk justify it.
- Build/package validation must inspect the package produced by the current sources and configuration, including required dependencies and resource files. A leftover package is not evidence of a successful current build.
- Validate the final state intended for review and commit. If code, tests, a spec, resources or CHANGELOG changes after a successful check, rerun the affected checks before claiming final verification. Report exactly what was run and any remaining limits.

On Windows with .NET 10, the standard combined release verification is `./build.ps1 -Release -Clean -Test`. Debug is the default; `-Test` runs regressions and `-Clean` cleans intermediates after the operation. See [build and release behavior](../specs/build-and-release.md) for the implementation contract.

Published release-contract checks protect the content and ordering of historical version sections. They must not require the Unreleased section to stay empty or demand that an old architecture remain the current implementation forever. Adapt location assertions when a document is moved while retaining its historical meaning. Put new user-visible changes in Unreleased; tagged publication extracts only the matching version section.

Distinguish a completed command from a passing check, and a passing offline check from live service/audio success. A final report states what changed, the checks and final file state they cover, failures or skipped work, and material limitations. The [API evidence policy](api-evidence.md) governs all real network or synthesis claims.
