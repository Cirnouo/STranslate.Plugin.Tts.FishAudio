# Documentation policy

README and user-guide content is maintained in the five existing languages: Simplified Chinese, Traditional Chinese, English, Japanese and Korean. The Simplified Chinese user guidance supplies the common structure. CHANGELOG is written in Chinese. All other documentation is written in English so that terminology, provenance and constraints are consistent across maintainers; quoted raw requests, responses and source text retain their original language. Persistent UI resources remain localized in all five locales.

The table defines the current layout. The root README is Simplified Chinese; localized overviews remain at `docs/README_*.md`, with same-language detailed guides under `docs/user-guide/`. Overviews retain the project icon and badges; other documentation media belongs under `docs/assets/` when needed.

| Location | Purpose |
|---|---|
| `README.md` and `docs/user-guide/` | User entry and detailed setup/configuration guidance in five languages. |
| `CONTRIBUTING.md` | The designated English unified developer and contribution entry; links to canonical policies/specs rather than duplicating them. |
| `CODE_OF_CONDUCT.md`, `SECURITY.md`, `SUPPORT.md` and `.github/ISSUE_TEMPLATE/` | Community conduct, human vulnerability reporting, support routing and public issue intake. Keep report routes consistent with actual repository settings; do not invent contact details, support windows or response deadlines. |
| `CHANGELOG.md` | Chinese user-visible net changes under Unreleased and immutable historical release sections. |
| `docs/glossary.md` | Canonical domain terms without implementation recipes. |
| `docs/specs/` | Current implemented behavior, ownership, compatibility and known limitations. |
| `docs/policies/` | Reusable maintenance, evidence and verification rules. |
| `docs/adr/current/` | Decisions with active constraints, including explicitly scoped amendments. |
| `docs/adr/history/` | Superseded decisions and the rationale needed to understand them. |
| `docs/adr/README.md` | Complete numbered ADR register and replacement relationships. |
| `docs/DESIGN_DECISIONS.md` | Legacy forwarding index with preserved old anchors. |
| `docs/references/` | English provider API/SDK contracts, source versions and representation limits, separate from implemented product behavior. |
| `docs/evidences/` | Dated, sanitized reproducible observations and raw evidence; original request/response language is preserved. |
| `docs/assets/` | Documentation icons and future media assets, with stable relative references when files move. |

Keep facts in their appropriate home. A provider may support a format or endpoint that the plugin does not implement. A successful metadata GET does not establish synthesis, decoder, playback or billing behavior. A proposed design, experiment, unmerged branch or test fixture must not be described as a shipped feature.

Every specification states its implementation baseline and links to relevant code and tests. Update it in the code change that changes behavior. Use explicit labels for current behavior, limitations, proposals and observed provider behavior; do not mix them in one normative paragraph.

ADR numbers are permanent and never reused. Move a fully replaced record to history, preserve its rationale and add successor links in both directions. A partially amended decision stays current with a precise current interpretation; identify which date, format rule or ownership detail was replaced. Do not classify decisions solely by their age. The legacy index carries links rather than duplicate decision bodies.

Maintain relative repository links and preserve existing inbound anchors when moving a document. Cite immutable upstream source revisions or versioned packages for precise API assertions. Keep personal machine paths, temporary output, credentials, full signed media URLs, local tool state and conversation transcripts out of reusable documents.

Do not create a second glossary or copy policy text into each specification. Link to the canonical document. Existing historical references to removed symbols or contributor files are provenance and must be labeled as such, not turned into requirements to restore those files.

Published changelog sections describe the version that shipped. Follow the [release policy](releases.md#changelog-editing-workflow) before editing CHANGELOG: verify the latest published stable baseline, use canonical English category headings with Chinese prose, and record cumulative net changes rather than development iterations. Do not retroactively rewrite a published section to describe a later refactor. Historical release checks must not require Unreleased to remain empty. Verify the final documentation state after the last edit, including changelog updates, under the [verification policy](verification.md).

## User guidance boundaries

Organize guides around user tasks: first reading, choosing a voice, understanding model/cost choices, adjusting speech, selecting a format and recovering from failure. Lead with the next useful action, keep optional setup after the first successful path, and use short steps or compact comparison tables where they reduce reading effort. Keep the five versions aligned in tasks and consequences rather than forcing a line-for-line translation.

Explain information the reader needs to act or decide: prerequisites, cost and cancellation limits, local-only favorites, meaningful defaults/ranges, non-obvious interactions and concrete recovery steps. A hidden interaction such as the wheel hover delay belongs in a short instruction. Intuitive icons do not need an inventory of glyph shapes, button borders or accessibility labels. Keys and Voice IDs normally come from copy/paste, so direct readers to the official source and explain recovery rather than teaching their character grammar.

Do not turn a user guide into a UI state machine or developer contract. Keep rendering matrices, cache partitions/TTL, callback generations, schemas, transport fields, buffer sizes, test expectations and storage layouts in their canonical English specs, policies or references. Preserve useful user consequences without those implementation details. Troubleshooting states an action that helps the affected task and distinguishes different causes; it must not recommend deleting valid settings or favorites for temporary network problems. Scope free-only precautions to readers who require zero spending without implying that paid use is unavailable.

Preserve existing inbound anchors, cost/network facts and the approved README layout when rewriting guidance. Render-check Markdown emphasis and tables in every language, including CJK punctuation boundaries, and verify links. A screenshot or fixture is not a reason to document all visible controls.

## Instruction progressive disclosure

Read this section before editing maintained AGENTS/Skill instructions, their instruction references or their loading/layout rules. It adapts the [reference repository's progressive-disclosure policy](https://github.com/Cirnouo/debugging-cdp-targets/blob/859808b66a7cf691f34a12344b209feee384c705/docs/policies/documentation.md#instruction-progressive-disclosure). Applying that practice to AGENTS is local repository policy, not a requirement attributed to an upstream Skill format.

Keep a usable entry, with the common workflow and critical cross-task boundaries inline. Root AGENTS owns ordinary preservation, formatting, authorization, branch basics, architecture/security, toolchain and verification boundaries. CONTRIBUTING is the human setup and contribution entry. Neither is a substitute for the other, and neither should duplicate complete domain procedures. Applicable ancestor and local AGENTS instructions accumulate; a local file does not replace its parents.

Move details because they are conditional and have a clear canonical owner, not to satisfy a word, line, byte or token target. Do not turn AGENTS into a pure index, split a complete procedure across tiny prerequisite files, preload every policy at startup, or create competing rule catalogues. A brief common guardrail may link to a complete owner without copying its procedure.

Every conditional owner must be directly reachable from the entry and have an explicit before-use trigger naming the operation and timing: for example, before editing CHANGELOG, changing streaming playback or creating/editing a PR. Read the owner before the governed action or preparation. A navigation list alone is insufficient. Reuse an already-read owner while it remains current.

Use `docs/policies/` for normative repository rules, `docs/specs/` for current implemented behavior, ADRs for decision rationale and `docs/glossary.md` for shared terms. C# coding conventions belong in policies; the behavior of a particular C# subsystem belongs in its domain spec. Provider contracts belong in `docs/references/`; captured observations belong in `docs/evidences/`. Keep future documentation media in `docs/assets/`. Do not move factual provider research into a second set of instruction references merely to imitate another repository's directory names.

When instruction ownership or links change, check the direct entry-to-owner links, triggers, overlapping obligations and preserved historical pointers. Keep reusable instructions independent of a specific workstation, agent session or private artifact path. Preserve official quoted/source evidence and captured bytes rather than rewriting them as normative instructions.
