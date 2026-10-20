# Documentation policy

README and user-guide content is maintained in the five existing languages: Simplified Chinese, Traditional Chinese, English, Japanese and Korean. The Simplified Chinese user guidance supplies the common structure. CHANGELOG is written in Chinese. All other documentation is written in English so that terminology, provenance and constraints are consistent across maintainers; quoted raw requests, responses and source text retain their original language. Persistent UI resources remain localized in all five locales.

The table defines document roles and the target layout. Some destinations are introduced by later documentation changes; listing a destination here does not claim that it already exists. Until the user-guide/assets migration, localized user READMEs remain at `docs/README_*.md` and existing screenshots remain under `docs/images/`.

| Location | Purpose |
|---|---|
| `README.md` and `docs/user-guide/` | User entry and detailed setup/configuration guidance in five languages. |
| `CONTRIBUTING.md` | The designated English unified developer entry when introduced; links to canonical policies/specs rather than duplicating them. |
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

Published changelog sections describe the version that shipped. Add new work to Unreleased; do not retroactively rewrite a published section to describe a later refactor. Historical release checks must not require Unreleased to remain empty. Verify the final documentation state after the last edit, including changelog updates, under the [verification policy](verification.md).
