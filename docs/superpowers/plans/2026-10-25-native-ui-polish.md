# Native UI typography and affordances implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Status:** Task1 and Task2 implementation/scoped reviews are complete. The whole-branch review found no separate production defect; its remaining finding is the failed integrated description-action envelope. The reserved test-only fix uses independently owned selected/active-mode pairs and its focused verification passed. The committed-fix full/package evidence and the one scoped final fix review remain the branch gates. Native Display-mode support quantization remains an explicitly recorded Task1 limitation.

**Goal:** Correct installed-host numeric-unit bottoms, secondary headers, selected-description gaps and clickable cursors.

**Architecture:** Preserve native text, cards, input and ownership. Apply measured unit-only presentation and locally scoped cursor behavior, with no provider/state/persistence redesign. Popup Mica is a separately researched follow-up, not an unverified claim in this plan.

**Tech Stack:** Windows/WPF .NET10, STranslate.Plugin1.0.15, existing iNKORE theme.

**Spec:** [native UI polish](../specs/2026-10-25-native-ui-polish-design.md).

## Global Constraints

- Public identity/dependencies/version1.1.2/schema2 remain unchanged.
- Numeric controls52×32DIP; static right/editor center; units11DIP secondary; existing validation/drafts/precision/wheel350ms/save ownership remain.
- Number-unit visible outlined bottoms align; no arbitrary per-unit offset, font substitution or native-text-to-vector rendering.
- Title14DIP; author/dot11DIP; existing baseline/ellipsis/35% allocation; portraits48/56DIP.
- Inputs/hyperlinks unchanged; Hand only for enabled visible clickable plugin targets; no host template/global cursor mutation.
- One source/WPF/build writer at a time; gpt-6.1-sol subagents, xhigh reviews/decisions; root sole background cua-driver GUI controller.
- No synthesis/tag/Release; preserve protected settings and unrelated files.

## Review Focus

1. Comma descenders and incomplete edit drafts retain intended bottom alignment without committing: Task1.
2. Runtime font/culture/actual DPI changes invalidate the measurement, preserving full native glyph ownership: Task1.
3. Native SettingsCard derives ButtonBase but is nonclickable by default; disabled/input/hyperlink targets must not acquire Hand: Task2.
4. Generated dropdown/scrollbar/thumb targets may live in separate logical/presentation trees: Task2.
5. Selected and list descriptions may have identical multiline content with different clipping/line allocation; preserve long content and empty folding: Task2.

### Task1: Numeric visible-bottom alignment

**Files:** InlineNumericEditor.xaml/.cs; focused numeric metric helper/converter; NumericTypographySuite/NumericPhraseReference/NumericGlyphObservation/InlineNumericEditorSuite as actually needed; Program for new runnable scenarios; settings spec, DD-058 and verification policy for the explicit contract amendment.

**Interfaces:** Consumes existing numeric value/draft/culture/font context. Produces a unit-only presentation metric with stable empty/sign-only fallback and no persistence/event change; Task2 preserves it.

- [x] Add an independently measured actual-native contour/raster regression for number/unit visible bottoms and observe a causal RED before product edits. Include dot/comma, signed value, empty/partial draft, runtime context and fixed ±3 physical-pixel sensitivity. Define acceptance before GREEN.
- [x] Implement the smallest measured unit translation while retaining native text and the complete existing horizontal/font/paint coverage. Require exact actual native outline equality in all contexts; retain strict default Ideal physical-support acceptance and characterize native Display grid-fit support separately. Replace only the superseded common-baseline vertical oracle; retain historical native-baseline characterization with an accurate label.
- [x] Run affected numeric/geometry/interaction tests on the existing runner and inspect compiled native-number/unit images in light/dark and actual WPF DPI. Preserve all failures and exact evidence boundaries.
- [x] Update owning current spec/ADR, commit atomically, report exact checks and self-review. Independent spec+quality task review accepted Task1 and its scoped follow-up before Task2.

### Task2: Secondary headers, selected gap and actionable cursor

**Files:** SettingsView.xaml; VoiceHeaderPanel only if actual geometry requires it; a small locally scoped cursor helper and compiled cursor suite; SearchAdjustmentControl/VoiceLanguageTags/InlineNumericEditor only where cursor scope requires it; VoiceCardLayoutSuite and Program; owning current specs/ADR.

**Interfaces:** Consumes Task1 native typography unchanged. Produces 11-DIP shared author/dot, bounded selected-description layout without spare space, and plugin-only cursor affordances retaining existing click/keyboard/wheel semantics.

- [x] Reproduce identical actual three-line Japanese metadata (line lengths12/28/49) in selected and list cards; inspect DesiredSize/ActualHeight and clipped visible lines. Add a gap regression plus one-line/long/blank/runtime cases, keeping existing line caps.
- [x] Shrink both shared header author/dot to11DIP and implement the demonstrated description allocation correction. Keep portrait/header/other metadata constraints unchanged.
- [x] Add native delayed full-description tooltips in all four locations; preserve full original line breaks despite card clipping and verify nonempty/blank/metadata-update cases without inventing a new tooltip timing preference.
- [x] Add meaningful QueryCursor regressions on actual compiled enabled/disabled controls, native nonclickable SettingsCard, plain metadata, inputs/hyperlinks, avatar actions, slider/toggle/dropdown/scrollbar parts and a host peer outside scope. Implement scoped behavior preserving all native templates/styles and lifecycle ownership.
- [x] Run affected card/cursor/overlay/numeric checks, update current specifications and commit atomically with report/self-review. Independent Task2 review and its scoped fix review are accepted. The cursor group uses nine separate five-second actions (45 seconds allowed), separating native parts/template and independently hosted lifecycle cases while retaining all assertions. Earlier aggregate and finite native-peer deadline failures remain recorded with their limits.

### Final branch gate and delivery

The reserved description-fixture fix separates selected/active Search/By-ID/Favorites source owners: twenty-four matrix actions plus one construction action, each retaining five seconds, allow 125 seconds instead of the earlier nine-action/45-second composition. All 192 observations, 144 native tooltip opens and complete runtime sequences remain. The original integrated timeout and the later finite fresh diagnostic retain their distinct evidence boundaries; no production or original-readiness change is inferred from them. The cursor group remains nine five-second actions/45 seconds.

- [ ] Freeze the committed description-fixture fix, run ./build.ps1 -Release -Clean -Test once at that HEAD, inspect its actual package/manifest/dependencies/7 locales/flags and preserve all evidence. The broad final review is complete; its same reviewer performs one scoped fix review after the remaining gate evidence.
- [ ] Use authorized push/PR association/actual required head CI/squash/main CI; no tag/Release. Update local main --ff-only after actual main success. Keep the four historical PR attachments.
- [ ] Exact-window background cua-driver native acceptance with a fresh private manifest-only package if needed. Never relabel failed/unavailable capture or unsupported physical input as success.
- [ ] Continue separately researched popup Mica work and final managed-worktree archive; preserve all ignored evidence before archival and retain PR associations.
