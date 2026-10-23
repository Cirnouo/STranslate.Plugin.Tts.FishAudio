# Versions, changelog and publication

This is the canonical release policy. It adapts the [reference repository's policy](https://github.com/Cirnouo/debugging-cdp-targets/blob/859808b66a7cf691f34a12344b209feee384c705/docs/policies/releases.md) to the STranslate package and host contract. Enabling automation does not authorize a tag or publication. Create an annotated release tag or GitHub Release only when the user has explicitly authorized that publication; ordinary commit, push and PR authorization is separate.

## Version ownership and host compatibility

[plugin.json](../../STranslate.Plugin.Tts.FishAudio/plugin.json) owns the distributable version. The SDK dependency version and settings schema are independent. Do not add a second version owner, automatically bump a version, or rewrite the manifest during packaging.

Use canonical stable `major.minor.patch` versions under [SemVer 2.0.0](https://semver.org/spec/v2.0.0.html), with no leading zero except a component equal to zero. Every component must also fit `System.Version`'s nonnegative Int32 range. The source manifest, packaged manifest and annotated `v<version>` tag must agree exactly. A newly published stable version must exceed the latest published stable version so installed plugins can upgrade; an already-published matching rerun remains a checked skip. A public release is immutable; prepare a new version rather than replacing its tag or asset.

Current STranslate 2.0.10 upgrades require `System.Version.TryParse` and an incoming version greater than the installed version. Its marketplace constructs the release download tag directly from the manifest version. Therefore this repository's installable publication accepts only three-component stable versions: no prerelease suffix, build metadata, fourth component, whitespace or implicit normalization. The reference repository's prerelease support cannot be transferred safely by stripping a suffix or mapping it to a fourth component. A future prerelease channel needs its own researched host/version design. See [DD-063](../adr/current/DD-063.md).

## Changelog editing workflow

Before editing `CHANGELOG.md`:

1. Verify the latest **published stable/full release** using authoritative GitHub Releases records or the releases API. Inspect enough history to establish the baseline, excluding drafts and prereleases. Local tags, commits and dated changelog entries alone do not prove publication. Resolve uncertain status before choosing categories.
2. Compare the final delivered behavior against that baseline. `Unreleased` records cumulative **net changes**, not individual commits, the previous experiment or the previous prerelease. Consolidate repeated work on one capability into its final behavior. If an unreleased capability is withdrawn, remove its entry rather than adding `Removed`.
3. Use the applicable canonical English headings, in this order: `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed`, `Security`. Omit empty categories. Write the entries in Chinese. Do not use translated category names or an `Engineering` category. Keep `Unreleased` first, followed by dated releases newest first, with valid ISO calendar dates, following [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/).
4. Record the verified baseline, evidence inspected, category reasoning and remaining limits in the PR's `Verification` section. Passing tests do not establish this semantic judgment.

`Fixed` describes a defect present in the published baseline. Correcting a feature introduced during the same unreleased cycle belongs in that feature's final `Added` or `Changed` entry. Do not list abandoned approaches, temporary dependency repairs, test-race fixes, repeated visual tuning, commit narratives or internal directory cleanup merely because they appear in development history. Commits, ADRs and evidence retain that history. A lasting developer capability can be a meaningful project change; describe the delivered capability rather than its repair sequence.

Preserve already-published sections and their original categories, dates, wording and ordering. During authorized stable release preparation, archive the final cumulative net changes in exactly one nonempty `## [<version>] - YYYY-MM-DD` section and remove delivered entries from `Unreleased`. The publisher reads that section; it never edits the changelog or invents a release date.

## Release notes

The release title is the exact tag. The body is:

```markdown
## What's Changed

<the matching changelog section's body, retaining its categories and Markdown>

## New Contributors

<generated contributor bullets, only when nonempty>

**Full Changelog**: <latest published stable tag...current tag comparison URL>
```

Omit the entire contributors section when there are no qualifying contributor bullets. Do not append GitHub's generated PR change list. When there is no published stable baseline, link the current tag's commit history. Release-note scaffolding is English; the changelog body remains Chinese.

Extraction ignores headings inside fenced code blocks. Require exactly one matching real H2 version/date heading, a valid calendar date and nonempty bullet changes; stop at the next real H2 heading. Do not silently select the first duplicate or treat a fake fenced heading as a release.

## Tag-triggered publication

Only a newly created, nondeleted, nonforced annotated stable tag push can publish. Manual runs, lightweight tags, moved/deleted tags and invalid versions must not publish. Verify the original pushed tag object, remote tag object and peeled commit, checkout HEAD, event SHA, exact manifest/package version and ancestry of the current remote main. Recheck remote tag identity before draft/final writes and before accepting a published-release skip.

Run the complete reusable validation workflow at the exact tagged commit before publishing, including Windows Release build, all offline regressions, package verification, API-probe safety and release-tool self-tests. Ordinary PR/main validation never publishes. Main's actual required checks and CodeQL scans are governed by [repository governance](repository-governance.md).

Use read-only workflow defaults and grant `contents: write` only to the publication job. Use the built-in `GITHUB_TOKEN`, full-history checkout of the verified SHA and `persist-credentials: false`. Queue runs for the same tag rather than canceling a publication in progress. Do not introduce another secret for normal publication.

Create a draft first, or resume an existing matching draft after checking its identity. Upload and verify the exact validated `.spkg` **while the release is still a draft**, before immutable publication. Validation first canonicalizes only the fresh package's ZIP wrapper: fixed ordering/timestamps/attributes and no compression, with entry names/payload bytes unchanged and unsafe or colliding names rejected. Verify the result before preserving its attempt-specific artifact. Publication consumes the successful validation's output identity and never rebuilds or transforms it. Inspect the packaged manifest and verify asset identity, size and digest; a matching filename alone is insufficient. Published releases cannot be repaired by overwriting their assets. A rerun may skip an already-published matching release only after validating tag, classification and the expected package; missing or conflicting assets fail safely.

Publish stable releases with `prerelease: false` and `make_latest: legacy`; validate the returned draft/published identity, draft state and classification. API, upload, identity or validation failures stop publication. Never delete a release or tag as failure cleanup or bypass validation to make publication succeed. Repository release immutability is configured by an explicitly authorized maintainer; the publisher does not toggle it.

Before creating an authorized tag, verify installation/update from the actual distributable and confirm package dependencies, licenses, version identity, matching changelog and all required gates. A passing fake publisher proves its protocol boundaries; it is not evidence that an actual Release or installation occurred.
