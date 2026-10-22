# Voice action glyph acceptance — 2026-10-22 UTC

## Build and regression evidence

STranslate 2.0.10 loaded private validation version `1.1.2.16`. The source SPKG was 524,606 bytes, SHA-256 `417060a9095ec6d1768573bc1fcb2894e674696c7744483085e6c4916391b735`. The version-only validation hash was `7ae988f09ff0feb0b1a2b4814aa92b744c20137edfe25d5115bac68be353fa64`; every non-manifest ZIP entry remained identical. Its installed DLL matched the packaged DLL, SHA-256 `7dcd8f2fb51ca07d0eed0ad851429e23d6fbbcdc604e4bb179dd6e7251891a4c`.

The final `./build.ps1 -Release -Clean -Test` passed with zero warnings/errors, package inspection, all regressions and cleanup. The 665-file public-tree manifest remained unchanged through the run; its hash was `3f339938398397bb7201482c3ddc2fa1743ab094480b9c8173cba9c7199aac86`. The release log hash was `1c07feea217d013f3c654bcbd59746e1274e07e2e4b1b91639e3076c4fdbc012`.

An earlier full Release attempt failed because a legacy test required a ContentPresenter inside the icon surface. Independent review also found that the first broad raster classifier counted the opaque card background. Neither result is treated as passed final coverage. The corrected test resolves the actual glyph/surface and retains hover/focus/pager checks; raster isolation paints the actual drawing onto transparency at its root position and DPI. A fixed-crop test detects an intentional three-physical-pixel displacement, whereas the broken classifier detected zero.

The corrected [raster suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/VoiceCardInkSuite.cs) covers 100/125/150/200% output scales, Ideal/Display text formatting and the four card locations/states. It keeps exact native contours and continuous centers, relative physical-pixel comparisons with half-pixel quantization, and the strict known 125% Display Cross/star failure. Controlled white ink isolates alignment from color/contrast. These fixtures do not establish every physical theme/device combination.

## Installed-host observations

The selected voice card and a real By-ID result for the same public voice both showed the star, Play and Cross strokes on the same visual row. The former visibly high Cross was absent in these two locations. The result template is shared by Search, By ID and Favorites; this host pass did not reload every list or exercise every Tick/Stop/spinner/theme combination. Those states have separate compiled-control coverage.

Clicking the selected title still produced the floating `声音 ID 已复制` host notification. Pasting that value into By ID and querying returned the matching card without changing the applied voice. No TTS command was invoked. The query was a read-only public metadata operation.

A private comparison confirmed all twelve latest pre-check settings remained unchanged, with PCM/schema 2, no retired proxy fields, an empty favorite-ID array, and unchanged hashes/modification times for the two sampled cover files. Computer control was released immediately after the check. This is visual/UI acceptance, not an acoustic or billing measurement.

Rationale: [DD-060](../adr/current/DD-060.md). Earlier [drawing-bound acceptance](favorites-controls-host-2026-10-22.md) did not settle the later reported physical-pixel offset; this follow-up records the narrower renderer correction and its new verification.
