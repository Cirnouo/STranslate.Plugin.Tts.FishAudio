# Voice search, cards and numeric controls implementation plan

> For agentic workers: use superpowers:subagent-driven-development task by task. Each task includes focused RED/GREEN evidence, a full runner pass before committing, self-review and a task review.

**Goal:** Implement the approved compact voice-search adjustment, consistent dated voice cards and precise numeric interactions.

**Architecture:** FishAudio owns immutable request criteria and provider parsing. Presentation owns drafts, committed search state, popup/card UI and transient error lifetime. Configuration and the existing metadata cache carry optional selected-voice date metadata without changing their authorization or persistence boundaries.

**Tech stack:** Windows/WPF .NET 10, STranslate.Plugin 1.0.15, existing executable regression runner.

**Spec:** [Approved design](../specs/2026-10-24-voice-search-and-controls-design.md).

## Global Constraints

- Preserve unrelated changes, public plugin identity, host bindings and surrounding UTF-8/line endings. Work only in the managed feature worktree.
- Read root AGENTS and relevant canonical policies/specs/ADRs. Keep Main as composition root, SettingsStore active-generation leases, captured credential ownership, cancellation, stale rejection and original cleanup failures.
- Ordinary tests are offline: no real synthesis, API key or audio device. No credentials, private text, signed resource URLs or personal paths in public files/diagnostics.
- Register new regression scenarios in Program.cs. Run focused tests while iterating and the full executable runner before each task commit. Final combined verification is ./build.ps1 -Release -Clean -Test and inspection of its actual .spkg.
- Persistent UI text uses DynamicResource in all seven locales. User guides have five languages; maintenance docs are English and CHANGELOG prose Chinese. Published changelog baseline is v1.1.2, verified through GitHub Releases on 2026-10-24. Preserve published history.
- Use gpt-6.1-sol for subagents and xhigh for reviews/decisions. Implement tasks sequentially; do not concurrently edit shared XAML/ViewModel files. Implementers must not spawn helpers or reviewers.
- Commit, push, PR, CI monitoring, squash merge to remote main, local main fast-forward and managed-worktree archive are user-authorized. Retain the PR attachment to this chat. No manifest bump, tag or GitHub Release.

## Review Focus

- Actual pointer mapping and wheel event ownership must remain correct at endpoints, signed zero and prequalification partial deltas.
- Focused native text viewports must fit -10.00 plus the caret at fractional DPI, without layout shifts or clipping; the later Task 9 requires centered editing rather than cross-state right-edge equality.
- Old timer callbacks and identical replacement errors must not clear new messages.
- Nested native ComboBox popups must not be dismissed as outside clicks, and failed/stale searches must not publish mismatched filters/results.
- Optional date metadata must round-trip and authoritatively clear across every card/cache/startup/preview path without crossing credential or persistence boundaries.

### Task 1: Native slider point clicks and shared wheel dwell

**Files:** NumericSlider.cs / NumericWheelAdjustment.cs, four sliders in SettingsView.xaml, SettingsInputInteractionSuite.cs and runner registration if new scenarios are added; settings specification and DD-058; affected five-language wheel instructions.

**Interfaces:** Existing numeric control bindings/ranges/precision remain unchanged. Produce native click-to-point on all four controls and a shared 350-ms dwell used by slider and resting text.

- [ ] Read relevant owners; add meaningful compiled track-click cases and move fake-clock exact boundaries from 249/250 to 349/350 ms. Cover slider descendants, reentry/edit/unload/template reset, bounds, qualified ownership and no prequalification accumulation.
- [ ] Run focused regressions and record the expected RED before changing production code.
- [ ] Enable native IsMoveToPointEnabled for all four sliders and change the shared threshold. Preserve hundredths, positive top_p floor, signed-zero behavior, native drag/tooltips and existing auto-save counts.
- [ ] Update current spec/ADR and affected localized guidance; run focused GREEN and full runner.
- [ ] Self-review and commit this task, including the approved design/plan documents created by the controller. Suggested subject: fix(settings): move sliders to clicked positions.

### Task 2: Compact right-aligned numeric fields and speed unit

**Files:** InlineNumericEditor.xaml(.cs), Speed editor binding in SettingsView.xaml; InlineNumericEditorSuite.cs and relevant layout/input suites; settings spec and DD-058/DD-059 amendments.

**Interfaces:** Consume Task 1's numeric controls. All fields/display buttons are 52 DIP wide, 32 DIP high, Padding 2,0 and fixed 1-DIP borders. Resting numbers right-align; Task 9 supersedes the original editor alignment with centered drafts. Units are separate (Speed ×, Volume dB, otherwise collapsed auto column). Keep current outer settings-card layout and 180-DIP sliders.

- [ ] Add compiled geometry/caret regressions before production changes: same resting/edit bounds, actual right edges, -10.00/-10,00 viewport and caret fit, focus brush without thickness/layout shifts, units outside editable text and no-unit column collapse.
- [ ] Record focused RED. Fix the hardcoded centered ContentPresenter, use its padding, reserve transparent resting border, right-align TextBox, shrink the numeric field and use auto-width units; keep native clear hidden locally.
- [ ] Preserve draft isolation/grammar, culture fallback, Enter/blur one-save, Escape cancellation, outside-click commit, slider replacement and persistence/reload. Check seven UI locales, narrow/normal widths, both themes and 100/125/150% DPI.
- [ ] Update spec/ADR, run focused GREEN and full runner, self-review and commit. Suggested subject: feat(settings): compact numeric editors and add speed unit.

### Task 3: Five-second By-ID error lifecycle

**Files:** VoiceDiscoveryCoordinator.cs / SettingsViewModel.cs or a focused Presentation-owned transient-error helper; VoiceDiscoverySuite.cs and clock infrastructure; browsing spec and user recovery guidance where applicable.

**Interfaces:** Existing VoiceIdError binding and lookup validation remain. Inject TimeProvider at the owning boundary and make transient publication cancellable/versioned; no durable setting.

- [ ] Add fake-time RED cases for 4999/5000 ms, identical-message replacement, edit/new-lookup immediate clear, older expiry after newer error, successful lookup, disposal and cancellation-ignoring responses.
- [ ] Implement one owned five-second expiry per occurrence. Retire on replacement/clear/disposal and recheck generation/owner in deferred UI publication. Only error text expires, not input/results; preserve lowercase raw-ID draft validation.
- [ ] Run focused GREEN and full runner, update the browsing spec, self-review and commit. Suggested subject: fix(voices): expire by-id validation messages.

### Task 4: Compact search adjustments and immediate Apply refresh

**Files:** FishAudio request criteria/API, VoiceDiscoveryCoordinator, SettingsViewModel, SettingsView.xaml(.cs), a focused search-adjustment control/overlay ownership helper and VoiceLanguageTags as needed; seven language dictionaries; fake HTTP and compiled popup suites; browsing spec, provider reference, ADR and five-language guides.

**Interfaces:** Add an internal immutable criteria class (query text, nullable language, sort enum) and pass it through the internal search API; API key remains separate. Replace committed-query-only state with successful committed criteria. Preserve public facade bindings and six raw provider records per page.

- [ ] Write RED fake-HTTP cases for explicit score/task_count/created_at; All omits language; ten scalar codes en/zh/de/ja/fr/es/ko/ar/ru/pt; escaping and captured credentials. Cover Apply unchanged still exactly one page-one GET, visible query capture, draft/Reset/Close zero calls, paging committed criteria, failed Apply retention, replaced responses and credential/disposal retirement.
- [ ] Implement request snapshots and atomic current-owner successful publication. Search/Enter use the applied options with visible text; Apply uses drafts with visible text, closes and immediately invokes one search. New searches cancel/replace old ones; failure retains the last successful results/options/page.
- [ ] Implement the embedded 28-DIP icon action with 4-DIP inset and reserved input padding, a 324-DIP clamped native popup, single-select flag ComboBox, Close/Reset/Apply and sort radios. Display titles are Relevance first/Most used first/Newest first; zh-cn uses 相关优先/热门优先/最新优先. Display titles only, without helper descriptions. This incorporates the user's later wording refinement; update seven locales coherently. Reset defaults All + score. Options are session-only state.
- [ ] Share one overlay owner with language overflow; handle the ComboBox child popup, Escape/focus return, outside press, tab changes, scroll, deactivation, move/resize/DPI, rebind/unload and subscription cleanup. Enter/blur must not duplicate Apply's request.
- [ ] Add compiled tests for selected and dropdown flags, exact presets, text/caret viewport, nested popup interaction, keyboard, normal/narrow containment, seven locales, both themes/fractional DPI. Register all new scenarios.
- [ ] Update specs/reference/ADR, seven resources and five guides. Distinguish observed descending provider behavior from documented contract and use score's chosen UI label without inventing its formula.
- [ ] Run focused GREEN and full runner, self-review and commit. Suggested subject: feat(search): add language and relevance sort controls.

### Task 5: End-to-end creation dates and all voice-card layout

**Files:** ModelEntity plus a property-scoped tolerant date converter; VoiceMetadataCache; CachedVoiceInfo/migration/copy paths; visible card models and discovery/favorite/preview/startup/selected propagation; two voice-card XAML sites and date presentation; relevant suites/resources/specs/ADRs/guides/CHANGELOG.

**Interfaces:** Nullable DateTimeOffset CreatedAt normalized to UTC across DTO/cache/snapshots/cards. Existing settings schema stays 2, favorites stay ID-only. Display system-local yyyy-MM-dd; tooltip includes complete timestamp/offset; missing/invalid shows — and authoritative null clears older values.

- [ ] Add RED cases for search/detail mapping, property-scoped malformed/wrong-type JSON date handling (consume complete arrays/objects), offsets/UTC, absent/null authoritative clearing, immutable cache copies, favorite placeholder reset/hit/refresh, selected apply/startup/preview propagation, old schema-2 roundtrip, malformed/duplicate nested dates, leased clone/save/rollback and read-only schemas.
- [ ] Carry CreatedAt through every existing metadata producer/consumer, including PreviewRefreshCoordinator's direct update and startup conditional persistence. Add no metadata disk cache or bookmark fields; preserve TTL/credential/card ownership.
- [ ] Rebuild both shared list and selected voice layouts: preserve avatars 40/48 DIP and existing actions, put actions top-right at title, then description, responsive language row, author, popularity-left/date-right full content row. Keep loading/error placeholders and action identity/state behavior. Titles/authors ellipsize; descriptions keep bounded lines; native theme resources only.
- [ ] Add compiled layout/interaction tests for all four card locations, absent data, long text, language overflow, action commands/progress/focus, 320/400/480/640 widths, seven locales, light/dark and 100/125/150% DPI. Test local date boundaries deterministically and full tooltip semantics.
- [ ] Update current specs/ADRs/resources/guides and consolidate Chinese Unreleased against published v1.1.2 without modifying published history or manifest version.
- [ ] Run focused GREEN and full runner, self-review and commit. Suggested subject: feat(voices): show creation dates in refreshed voice cards.

## Final verification and delivery

- [ ] Complete each task review, then a fresh xhigh whole-branch review. Fix material findings and rerun affected checks.
- [ ] After implementation, use cua-driver in the local STranslate host for actual appearance/interaction acceptance. Root is the only desktop controller; inspect live schemas, use exact-window snapshot/action/verify, preserve private evidence, restore settings and avoid synthesis. Finish this acceptance before whole-branch final signoff and PR creation.
- [ ] Run ./build.ps1 -Release -Clean -Test on the final intended state and inspect the fresh .spkg manifest, private runtime dependencies, seven resources and flag assets. Run relevant offline probe/release-tool checks already required by repository CI; record evidence boundaries.
- [ ] Repartition verified integration commits into four sequential PRs: controls + By-ID error + numeric typography; search (Task 4 + its diagnostics + Task 8); dated cards + Likes + softer heart; avatar preview + retained playing. The controller owns branch operations, template-compliant PR creation/attachment, per-PR frozen builds, reviews and required CI. Squash each after success with the PR-number suffix and verify each merged subject/association.
- [ ] Monitor main CI at the merge commit. Fast-forward the original local main with --ff-only, preserving unrelated work. Archive the managed worktree, preserving all four PR attachments, then verify attachments and local/remote commit identity. Do not tag or publish a release.

## Approved separate follow-ups after Task 8

- **Task 9 — Numeric typography:** keep resting numbers right aligned, center editor text, and retain separate units on the number's typographic baseline. Measure native fonts, line boxes and glyph baselines before selecting a fix; original dB ink extents do not by themselves prove baseline misalignment. Remove the extra unit margin, derive unit line metrics from the numeric font, use the native caption size for × and retain the numeric size for dB (superseded by Task 15's both-smaller/secondary units). Preserve numeric dimensions, precision, drafts, validation, wheel dwell, zero canonicalization, separate unit hit/event ownership and persistence. Extend the existing 48 shared-STA culture/theme/actual-DPI actions without changing first-use raster order or five-second action deadlines. Check same-font phrase spacing and both states; the controller inspects representative compiled renders before freeze and owns installed-host acceptance. Keep this numeric-only commit replayable into delivery group 1 before search/card/avatar work. Task 7 records this request without changing numeric controls.
- **Task 10 — Accepted preview lifetime:** retain already-started search preview audio through new searches, Apply and paging/result replacement, while stale pending preparations remain unable to start output. Preserve credentials, disposal and explicit stop/replacement, truthful current-card progress projection and existing unrelated removal contracts. This is a separate ownership change; Task 7 retains current card-removal cancellation.
- **Task 11 — Softer shared heart:** refine the existing closed heart vector to fuller rounded lobes/notch/bottom for outline and filled states, matching the user reference. Preserve all Like domain/ID/command/state and geometry limits, separate avatar/title centers, historical exact reference-star oracle and displacement sensitivity. Task 7 retains Task 6's current heart shape.

- **Task 15 — Secondary numeric units:** following the newer references, keep the existing measured baseline/layout and set both × and dB to 11 DIP with the dynamic secondary text brush. Preserve the native primary number, all 52 × 32-DIP fields, right resting/centered editing, inert external units, drafts, precision, 350-ms wheel ownership, zero handling and persistence. Retain the old baseline/compact-gap oracles and compare actual glyph fonts, brushes, prefix advance and unit extent with a literal native single-line reference under matching actual child DPI/text options/phase. Report allocation gap and literal space advance separately, retain a fixed +3-physical-pixel reference displacement probe, and inspect same-scale light/dark dot/comma static/edit pairs before freeze. Extend the existing 48-action matrix and affected input/storage checks without new deadlines, warmups, retries or raster-order changes. Root accepted the minimal candidate; no shared renderer or numeric behavior change is needed. Freeze source/docs, inspect a fresh Release package and matching proof DLL, self-review and commit atomically. This numeric-only follow-up joins delivery group 1 after Task 9.

Tasks 9–11 require their own focused regressions, representative compiled render inspection where relevant, frozen full release/test/package verification, self-review and atomic conventional commit. Tasks 12 and 15 use focused RED/GREEN and affected checks for their changed contracts, then frozen Release/current-package/artwork/proof-assembly inspection and an atomic commit; neither repeats the already-passing Task 11 whole integration runner. Each actual delivery PR must run its own final `./build.ps1 -Release -Clean -Test`, including groups 1 and 4. None authorizes synthesis, publication or changes to schema/version/public identity.
