# Four-buffer output acceptance in STranslate — 2026-10-22 UTC

## Installed validation package and source baseline

The installed `.17` validation package was built from main commit `e89639c0f1742e91bdb614802ceae580635c7701` plus the output-headroom change and its then-current tests/documentation. Its identities below describe that installed build, not a later rebuilt integration package.

The audio change passed `./build.ps1 -Release -Clean -Test` with zero warnings/errors, package inspection, the complete offline runner and cleanup. The 723 frozen public files remained unchanged through that run; manifest SHA-256 was `39bd676cd8e707588e092aea10346a09a5f1b0a44a1dc451258fbd926c4c9bba`. The release log hash was `76ccd0cdb5f0edfbaa93d6a6eeb3769646077fbfe3aeceb7bdab328d45fd0c32`.

The source SPKG was 524,612 bytes, SHA-256 `6cd8d0e0f8921fa238c2ca62a25b13641da77b6882b79cc85a4191eef5c75549`. Its version-only validation package `1.1.2.17` had hash `6d0a867d6d9037c89f6063c7a59c99d3ba4c566ec6ca380f219922acedb35213`; all non-manifest ZIP entries were identical. STranslate 2.0.10 was restarted, and the installed DLL matched the package, SHA-256 `f9fabd16cc9e953e1c2c9ae4117b9f3bbc1bb3c19ea07df7432d2c2773e07108`.

A private comparison confirmed the latest twelve settings remained unchanged, with PCM/schema 2, no retired proxy fields, the user's latest favorite-ID array retained, and unchanged sampled cover files. The user had added a favorite after earlier empty-Favorites acceptance; it was preserved rather than removed as a test artifact. Computer control was released after installation.

The change was subsequently integrated onto main commit `cec35792cccf51c1d86d738c09f19a5d8812c30a`, which adds the separate pressure-clock test-fixture correction without changing production audio code. That integration passed a new complete Release build/test/package/cleanup run with exit code 0 and zero warnings/errors. Its 724-file pre-run manifest hash was `8f9dddfd5dc3ca9bc473110a46086e51aed6018104d8661b5dc81b762eaefb85`, and the completed log hash was `4fbefb5bee0d5de14dc92822e0a73de844aa1f5448e75946eed9921cb20c1f5f`. All frozen files were verified unchanged after the run, before this provenance clarification. The rebuilt integration package has not been reinstalled or listened to; the host report below remains specific to `.17`.

## Installed-host listening report

While version `1.1.2.16` was installed, the user still heard electrical noise and stuttering when reading this new English sample:

> Two test issues have been fixed and passed independent review. The tests now draw the icon onto a transparent background and then verify the strokes; a deliberate 3-pixel downward shift can also be accurately detected, and the original focus, hover, and page-turning validations are still retained. The final full Release check is currently running; once it passes, the corrected version will be installed for host visual acceptance.

Version `.16` contained the glyph correction and still used the old 100 ms/two-buffer audio configuration. After `.17` was installed and restarted, the user re-read the same sample and reported that the electrical noise/stuttering had disappeared. The retained settings selected `s2.1-pro-free`, PCM, the same voice, speed 1, volume 1 dB, temperature 0.2 and top_p 0.25.

This is the user's qualitative installed-host report. There is no microphone/loopback recording or raw response capture for this particular host run, and it does not establish reliability on every device or workload. The separate [same-source offline comparison](pcm-output-2026-10-22.md) preserves byte/timing evidence supporting the narrow output-queue change. Nominal queue capacity increased by 100 ms while approximately 50 ms per buffer, source bytes and software startup/drain rules remained unchanged.
