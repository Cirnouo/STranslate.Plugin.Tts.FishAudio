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

## Current ownership

[migrated-cases.json](migrated-cases.json) declares 243 original owners and 249 NUnit successor leaves. The creation-date owner has three successors and the language owner has five; every other transferred owner has one. The 1,337 remaining original owners retain legacy registration. These are source ownership declarations: each new staged snapshot still requires complete hybrid validation and fresh identity reconciliation. Ordinary compatibility and ownership-fault tests are additional framework cases, outside those migration counts.

New ordinary tests use NUnit attributes and stable named parameter rows. The migration document is provenance, never a test registry. Direct transfers retain complete original method bodies, canonical IDs and immutable-reference ordinals. [StreamingPlaybackTests](../STranslate.Plugin.Tts.FishAudio.Tests/NUnitCases/Playback/StreamingPlaybackTests.cs) calls one original owned scenario per method. [HookAcceptanceSuite](../STranslate.Plugin.Tts.FishAudio.Tests/Suites/Runner/HookAcceptanceSuite.cs) uses a suite-local metadata-only TestCaseSource for its fourteen independently signed Git scenarios. Its original execution body and report admission remain; its build subprocess disables build servers so process ownership includes actual exit and both output drains. Discovery performs no Git work, fixture construction, Dispatcher setup or native warmup.

Mechanically transferred fixtures remain NonParallelizable. Architecture-contract and credential-scope/display cases, plus the two activity-counter reentrancy cases, require a fresh host with UiThreadInvokerOverride initially null. Execution metadata assigns each of these 23 leaves its own process. The six credential queued-publication cases retain their original finally-to-null cleanup; the architecture model-invoker case retains its saved-previous cleanup. These constraints do not establish arbitrary override nesting or thread safety. A later scheduler must honor them until reviewed ownership and actual parallel verification support a change.

## Assertion and lifecycle responsibility

Each mapping row records the original assertions and any ownership correction. Timer-owning By-ID, manual-credit and preview fixtures dispose their existing ViewModels. Held responses are released and the same started task is joined on failure where that obligation has been migrated. Cleanup attempts all owned stages while preserving the original failure; it does not substitute a completed task, add a warmup, reset global state or relax an execution budget. Byte-equivalent transfers and fixture ownership corrections are explicitly distinguished in the mapping.

[VoiceCreationDateSuite](../STranslate.Plugin.Tts.FishAudio.Tests/Suites/Cards/VoiceCreationDateSuite.cs) exposes three complete semantic scenarios. Its settings lease/save/rollback chain remains continuous. [VoiceLanguageSuite](../STranslate.Plugin.Tts.FishAudio.Tests/Suites/Voice/VoiceLanguageSuite.cs) exposes five complete scenarios; its By-ID owner releases the held reply, joins the captured page task, disposes its ViewModel and cleans up its owned directory without replacing the primary failure.

The invalid-URL and offline [PreviewSuite](../STranslate.Plugin.Tts.FishAudio.Tests/Suites/Preview/PreviewSuite.cs) preflight owners capture and await their actual command task before independent ViewModel disposal. These paths complete before their first await and never acquire a player; cleanup does not manufacture a player or claim a pending-response leak. The continuous [initialization write-lease owner](../STranslate.Plugin.Tts.FishAudio.Tests/Suites/Lifecycle/InitializationLifecycleSuite.cs) retains both live ViewModels through the rejected old mutation and accepted replacement mutation. Cleanup follows all four original assertions, independently retires the acquired runtime leases and disposes the returned ViewModels; it does not retire the host Settings object or replace the retired-lease oracle with disposal.

Each new ownership helper has separate ordinary fault tests; these tests do not replace a historical assertion owner or need a migration row.

The eleven startup-credit owners capture published initialization work before replacement or disposal, retain each original startup, reinitialization and credit-observer Task, release their held responses and publication windows, and join actual retirement continuations. The observer Task completes after its publication has been queued; it does not establish that an arbitrary UI callback has run. Current-state capture cannot observe an initialization state whose construction failed before publication.

The four discovery, two favorite, two credential-display and one streaming-supersession owners retain their original held work through failure cleanup. Their original assertions and operation order remain. The two [held-work fault cases](../STranslate.Plugin.Tts.FishAudio.Tests/NUnitCompatibility/HeldWorkOwnershipFaultCases.cs) inject failure after acquiring a real lookup or all three real playback calls, then check terminal ownership before an independent rescue. The streaming owner also joins its captured initialization retirement work. These representative fault cases do not establish arbitrary cleanup paths in the remaining legacy owners.

Migrated streaming leaves use exact framework case selection. Their old legacy aggregate, group and leaf selectors are retired. Actual executable tests require those selectors, including replay of a passed aggregate after source drift, to fail without choosing unrelated legacy work. The historical-descriptor test separately preserves generic alias/replay rules on synthetic registries.

## Verification scope and remaining work

The declared owners have focused framework evidence across separately frozen source trees. Those observations apply only to their recorded source, build and selection. They do not prove a different staged snapshot or replace its complete hybrid gate. Some earlier successful focused sessions did not complete optional diagnostic output; their results do not claim that auxiliary completion. Raw evidence and failed attempts are retained privately.

The signed foundation commit [5b2bfb38](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/commit/5b2bfb386f3fbcb57a89d965558200402bd3cb5a) includes only the initial fifteen transfers and supervision/keyboard-boundary work. Its real staged-tree gate passed the complete remaining legacy and both framework catalogs. [PR46 validation](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/actions/runs/40711640100) and [CodeQL](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/actions/runs/40711639985) passed on synthetic merge commit 987dd1655fafa8789666ed014733ec643c688d64. That evidence does not validate the later ownership declarations. Historical native Alt/Shift/table mismatch failures and the old timeout remain failed evidence; later passes do not establish their cause.

The first-window text owner remains legacy because the current child compatibility fixture and coverage verifier still require its catalog identity. A direct attribute transfer must migrate and revalidate that dispatch chain together.

The blocked-load and snapshot-across-reinitialization TTS owners (original ordinals 154 and 155), and the favorite-page cache-miss owner (182), retain legacy registration. Their assertion-failure paths still require release of the original load or HTTP response barriers and joins of all started operations. The TTS snapshot owner additionally needs its actual credit observer and initialization retirement work. A passing normal path does not establish that cleanup responsibility.

Three VoiceDiscoverySuite owners remain legacy: VoiceLookupRequestsCancelPreviousAndDisposeWorkAsync, VoiceLookupCompletionsAfterDisposeDoNotMutateStateAsync and DisposeRestoresDiscoveryBusyStateBeforeCancelCallbacksAsync. Twenty-seven initialization, twelve preview and seven cover-cache owners also retain legacy ownership. Their held response/save/construction barriers require release and a join of the same started task on failure. Reading PendingStartupTask after disposal may return Task.CompletedTask instead of the retired work; even the captured startup task does not join the separately discarded credit observer. The overlapping startup/manual/TTS owner at ordinal 103 retains that outstanding observer obligation. ViewModel disposal does not join the late clear-cache continuation. The bounded-preview aggregate requires a responsibility-preserving split rather than one aggregate NUnit wrapper. No remaining assertion is retired by these declarations.

Identity reconciliation leaves assertion conservation, lifecycle conservation and source correspondence to independent review. It establishes neither product coverage nor complete dependency closure, a reusable coverage baseline or test-impact selection. The [test-system specification](../../docs/specs/test-system.md) and [verification policy](../../docs/policies/verification.md) own those separate contracts.
