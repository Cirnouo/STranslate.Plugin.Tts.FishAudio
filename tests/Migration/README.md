# Original legacy ID reference

[legacy-catalog.json](legacy-catalog.json) freezes the original ordered 1,580 legacy case IDs and partitions at Git tree `ebbae71fc69aaf5b47d74e4dcbdcc6b6cf4f058c`. The reference commit is [PR2 main commit `ec28a59e1a9f4d5debaf2998f668bb3f7988aeca`](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/commit/ec28a59e1a9f4d5debaf2998f668bb3f7988aeca), whose tree matches that tested snapshot exactly.

The cases were copied in their existing order from the complete successful Release gate report. All 1,580 outcomes in that report were `passed`. The requested report bytes matched its latest immutable generation. Its recorded source identity is retained separately:

| Field | Recorded value |
|---|---|
| Gate report source commit | `98c53efc3abd5923acf3a97468a572b2c11551bf` |
| Gate report tested tree | `ebbae71fc69aaf5b47d74e4dcbdcc6b6cf4f058c` |
| Snapshot / dirty | `true` / `false` |
| Configuration / SDK | `Release` / `10.0.403` |
| Raw gate report SHA-256 | `e1caf60196bb62df52a5cc8b0350cff6ab6dd383741f9f2de3ddbcef7b7e8b43` |

The gate's recorded source commit and the main reference commit are distinct observations connected by the exact tested tree. The raw report remains private; the JSON contains its digest and sanitized source provenance, without local paths or local run identifiers.

[Main validation attempt 2](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/actions/runs/40591866441/attempts/2) subsequently confirmed the exact main commit. Its aggregate admitted the complete 1,580-case legacy catalog using the latest actual execution for each partition: core attempt 2 and the four native partitions' attempt 1 evidence. [Main attempt 1](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/actions/runs/40591866441/attempts/1) failed and remains retained evidence. The successful recovery did not establish a cause or a source fix for that earlier timeout.

| Original partition | Cases |
|---|---:|
| `core` | 309 |
| `native-settings` | 94 |
| `native-input` | 175 |
| `native-popups` | 147 |
| `native-cards` | 855 |
| **Total** | **1,580** |

## Schema and use

Schema version 1 contains `referenceKind: "git-tree"`, `sourceReferenceCommit`, `sourceTree`, provenance, and an ordered `cases` array. Every case has exactly `id` and `partition`. The order comes directly from the historical report; it is not regrouped or sorted by partition. No delegate, callable owner, test registration, runtime object or current execution result is stored in this reference.

Keep this file unchanged throughout migration. Historical owner transfers belong in separate migration records, reconciled against fresh legacy and NUnit discovery by the [identity-conservation reader](../../tools/FishAudio.Testing/Evidence/MigrationConservation.cs). Ordinary new NUnit cases remain automatically discovered from framework attributes and named rows; they are not appended here and need no migration record.

The reference supports an ID-conservation check: each original ID must retain an unambiguous legacy owner or have explicitly declared NUnit successors. The [pinned reader](../../tools/FishAudio.Testing/Evidence/PinnedLegacyReference.cs) authenticates exact source-owned LF/CRLF raw-byte representations without normalization or caller-supplied digests. A neighboring digest cannot authenticate a changed reference. Current discovery/source binding belongs to the admission caller.

[migrated-cases.json](migrated-cases.json) records 15 original owners transferred to ordinary NUnit attributes: three API Key/Voice ID format owners, nine streaming-settings owners and three streaming-text owners. The settings and text cases keep their existing static suite classes, method bodies, helpers and canonical IDs; only the registered entry methods carry test attributes. The first-window case remains a legacy owner while child compatibility still binds to it. The request-path scenario and native settings-card cases remain legacy obligations. The full hybrid gate checks all original identities against the complete current legacy and framework catalogs, rather than accepting a matching total count alone.

Identity reconciliation leaves assertion conservation, lifecycle conservation and source correspondence to independent review. Neither this file nor a passing ID-conservation check establishes product coverage, complete dependency closure, a reusable coverage baseline or test-impact selection. The [test-system specification](../../docs/specs/test-system.md) and [verification policy](../../docs/policies/verification.md) own those separate contracts.
