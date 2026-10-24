# Native UI typography and affordance corrections

Status: approved implementation design. Task1 is implemented on this branch; card typography, description spacing/tooltips and cursor work remain planned Task2 until its own implementation and verification. This is not a released-feature record.

## Intent and authority

The user's installed-host screenshots refine the approved voice UI. Preserve the existing native theme, data/search/playback behavior and compact layout while correcting visible numeric-unit alignment, secondary header typography, selected-description spacing and clickable cursor feedback. These are targeted corrections to the existing design, not a new application design. Popup Mica investigation is a separate deliverable; this document does not claim the current Acrylic popup matches Mica.

## Requirements

- Keep all four numeric controls 52 × 32 DIP, resting numbers right aligned and editable drafts centered. Keep Speed × and Volume dB at 11 DIP in the dynamic secondary brush, separate from editable text. Preserve precision, ranges, draft/commit/cancel, caret, wheel350ms and save ownership.
- Number and unit must align by their visible outlined bottoms in both resting and editing states. A shared typographic baseline alone does not satisfy this amended requirement. Measure the complete active displayed string, including a valid comma descender, with the actual text/font/culture/flow/formatting/DPI context. Empty or punctuation/sign-only partial drafts retain a stable committed-number anchor without altering their draft or settings.
- Retain native small-text rendering. Prefer a measured unit-only translation over arbitrary offsets or replacing native text with vector artwork. Keep the existing horizontal allocation and all complete glyph/font/brush/advance coverage; amend only the explicitly superseded vertical baseline oracle. Add independent actual-contour and physical-raster displacement sensitivity before accepting a change. Require exact native outline alignment in all contexts. Keep the one-row nonzero-alpha support bound for the default Ideal matrix; characterize Display-mode grid-fit support separately, including the observed two-row comma/× difference at 150%, without claiming universal pixel equality or forcing a text-rendering mode.
- Keep the 14-DIP title and shared baseline/ellipsis/35-percent initial author allocation. Both author and preceding middle dot become 11 DIP Normal secondary text on all four card locations. Preserve title-only Copy ID and author tooltip semantics.
- Selected and list cards must avoid unused description-to-language space. Preserve selected56/list48 portraits, long-description caps and trim-empty collapse. Compare identical actual multiline input before choosing the smallest layout correction; do not delete provider text or blindly reduce all margins.
- Hovering a nonempty description shows the complete original description through a native delayed tooltip on all four card locations. Preserve original line breaks and finite-width wrapping, use the host's existing tooltip delay and leave the description passive. Trim-empty descriptions have no tooltip; clipping the card text must not clip the tooltip's bound content.
- Within the plugin, enabled visible mouse-clickable controls show Hand. Inputs retain their native input cursor; hyperlink text retains its existing behavior. Plain metadata and nonclickable settings cards must not become Hand. Cover native generated parts such as dropdown items, toggle switches and slider/scrollbar tracks/thumbs. Preserve native styles/templates/commands and scope all cursor behavior to plugin-owned controls and overlays.
- Public identity/dependencies/version1.1.2/schema2 remain unchanged. No provider/synthesis, secret output, global host style changes or foreground/desktop automation are authorized by this work.

## Approaches and implementation calls

The earlier native shared-baseline implementation is retained as historical characterization; the latest visual request replaces its vertical product target. A hardcoded suffix offset cannot address differing ×/dB contours, fonts and decimal strings. Measure native text contour metrics and translate only the separate unit, retaining actual native text rendering. If physical evidence disproves the measurement, preserve the failure and reassess rather than tuning a screenshot-specific constant.

Use 11 DIP for author/dot as the smallest local adjustment consistent with existing secondary card text. For cursor feedback prefer a scoped routed-event/explicit action behavior that keeps theme templates intact; exclude SettingsCard when IsClickEnabled is false and protect input/hyperlink descendants. Avoid a root Hand cursor that would incorrectly mark all whitespace clickable. The implementer must demonstrate any generated-popup event/scope propagation rather than assume it.

## Verification and delivery

Meaningful compiled regressions run on the existing shared STA, preserving per-action deadlines and first-use raster ordering. Numeric coverage retains cultures/themes/actual root DPI and no-value-change contracts. Card coverage includes same-string one/multiline/blank descriptions, runtime changes, narrow widths and all four locations. Cursor coverage checks actual resolved targets, disabled and native template parts and unchanged host peers.

Each task produces an atomic commit, report and independent task review. The follow-up branch receives a final full ./build.ps1 -Release -Clean -Test, actual package inspection and broad review before authorized push/PR/squash/head/main CI. Exact-window background cua-driver acceptance is separately reported; geometric/readback tests cannot establish visible material, physical pointer or audible output. Keep the older four merged PRs attached. Final worktree archival uses the managed tool; its existing primary-checkout refusal must be resolved without forcing removal.
