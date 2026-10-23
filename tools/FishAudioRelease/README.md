# Fish Audio release tooling

This BCL-only .NET 10 console tool implements the [release policy](../../docs/policies/releases.md). It has no Fish Audio API dependency, reads no host profile and never changes a version, tag or changelog.

Run the deterministic offline protocol tests:

```powershell
dotnet run --project tools/FishAudioRelease -c Release -- --self-test
```

The self-test uses an in-memory Git/GitHub boundary and a fake HTTP message handler. It never reads `GITHUB_TOKEN`, creates Git commits, calls GitHub or publishes a release. Coverage includes canonical stable versions, fenced Markdown and duplicate/date/empty sections, new-tag event admission, source/package manifest agreement, draft upload/download/finalization order, draft resume, published-package skip, digest mismatch, remote movement at each write boundary, local tag rejection, injected upload/download/publication failures, and credential isolation on asset redirects.

`--canonicalize-package` operates offline on the freshly built root package and verifies its manifest after transformation. It never rebuilds or changes payload bytes. The publisher downloads the completed validation artifact and never canonicalizes or rebuilds it.

`--verify` requires the GitHub tag-push environment and event file, checks the exact annotated tag, original pushed tag object, peeled commit, checkout HEAD, current `origin/main` ancestry, dated changelog and current root `.spkg`. It makes no HTTP requests. CI runs this after the complete Windows Release build and offline tests, then preserves that exact package as a workflow artifact. Before release verification, CI canonicalizes only the ZIP container: ordinal entry order, fixed timestamp/attributes and stored payload bytes. Entry names and uncompressed contents are preserved; duplicate/case-colliding or unsafe names fail. This removes ZIP metadata variation across reruns while any changed DLL/resource still changes the verified package bytes. Each successful validation attempt exposes its unique artifact name through the reusable workflow output; a publication-only rerun consumes that original successful validation output.

`--publish` is exclusively the publication job entry point. It accepts only the built-in `GITHUB_TOKEN` environment variable and a newly created, nondeleted, nonforced tag push. It revalidates the downloaded verified package, constructs curated release notes using GitHub's latest stable release and optional contributor bullets, creates/resumes a matching draft, uploads and downloads the asset to verify its SHA256, rechecks the remote annotated tag, then publishes the immutable stable release. It never deletes a tag/release, replaces an asset or repairs a published release. A matching published release skips only after package and remote-tag verification. Failure leaves the draft for inspection and a safe rerun.

Only exact canonical `vM.m.p` stable tags with Int32 components are accepted. STranslate 2.0.10 upgrades use `System.Version`, and the marketplace constructs the download tag from the package manifest. Prerelease/build suffixes, leading zeros, fourth components and suffix-stripping mappings are deliberately rejected. `plugin.json` remains the sole package version owner. The SDK's string metadata type does not establish host prerelease compatibility.

The adapter uses fixed GitHub API/upload origins and disables automatic redirects. GitHub asset downloads may return HTTP 302; the tool follows only an HTTPS `release-assets.githubusercontent.com` location, without forwarding authorization. Signed URLs, token values and API response bodies are withheld from diagnostics. The HTTP protocol behavior follows the [GitHub release-asset API](https://docs.github.com/en/rest/releases/assets).

Workflow action revisions are immutable pins. The checkout/upload/CodeQL pins follow the reviewed reference workflow at `859808b66a7cf691f34a12344b209feee384c705`; setup-dotnet v5.3.0 and download-artifact v7 were resolved from their official repositories. CodeQL C# uses manual Windows .NET 10 compilation of all four maintained projects, following [CodeQL compiled-language build guidance](https://docs.github.com/en/code-security/reference/code-scanning/codeql/build-options-for-compiled-languages) and its [.NET 10 support matrix](https://codeql.github.com/docs/codeql-overview/supported-languages-and-frameworks/). Separate CodeQL checks need successful main/PR scans and finding review before becoming required governance checks.
