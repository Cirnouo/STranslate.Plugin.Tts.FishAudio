<div align="center">
  <a href="https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio"><img src="assets/icon.svg" alt="Fish Audio TTS" width="80" height="80" /></a>
  <h1>Fish Audio TTS</h1>
  <p><a href="https://fish.audio">Fish Audio</a> speech synthesis for <a href="https://github.com/ZGGSONG/STranslate">STranslate</a>.</p>
  <p>
    <img alt="License" src="https://img.shields.io/github/license/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Release" src="https://img.shields.io/github/v/release/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Downloads" src="https://img.shields.io/github/downloads/Cirnouo/STranslate.Plugin.Tts.FishAudio/total?style=flat-square" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" />
    <img alt="WPF" src="https://img.shields.io/badge/WPF-Plugin-blue?style=flat-square" />
  </p>
  <p><a href="../README.md">简体中文</a> · <a href="README_TW.md">繁體中文</a> · <b>English</b> · <a href="README_JA.md">日本語</a> · <a href="README_KO.md">한국어</a></p>
</div>

> [!TIP]
> Register with a `.edu` email and complete student verification to receive **US$5 in credits**. [Apply](https://fish.audio/students/)

> [!TIP]
> S2.1 Pro is free through **2026-11-30 UTC**. Select `s2.1-pro-free` in the plugin. [Promotion details](https://fish.audio/blog/s2-1-pro-free-api/)

> [!IMPORTANT]
> S1 retires on **December 31, 2026**. After that date, API requests using model `s1` will be served by S2.1 Pro at the same price. Switch to `s2.1-pro` before then. [Official details](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live)

Eligibility and promotion conditions are subject to the official Fish Audio pages.

## Features

- **Voice discovery**: Search by name, browse pages and preview voices, look up a voice ID before applying it, and star voices to keep local favorites.
- **Streaming speech**: Send text in segments while receiving and playing audio; long text can start before the whole synthesis finishes.
- **Audio formats**: PCM is the default; choose MP3 and its bitrate for slower connections.
- **Synthesis controls**: Adjust model, speed, volume, loudness normalization, expressiveness, diversity and latency mode.
- **Account and cache**: View and refresh API credits and manage the cover-image cache shared by all voice entry points.
- **Interface languages**: 简体中文, 繁體中文, English, 日本語 and 한국어.

## Installation

### Plugin marketplace (recommended)

1. Open STranslate and go to **Settings → Plugins → Marketplace**.
2. Search for **Fish Audio TTS**, then download and install it.

### Manual installation

1. Download `STranslate.Plugin.Tts.FishAudio.spkg` from [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases).
2. Import it through **Settings → Plugins → Install** in STranslate.

## Quick start

1. Enter your [Fish Audio API Key](https://fish.audio/app/api-keys) in the plugin settings.
2. Search and preview voices, then use the tick for **Apply**; you can also query an ID with its magnifier or choose from **Favorites**. The cross clears an applied voice.
3. Choose a synthesis model and audio format, adjust parameters as needed, then read text aloud in STranslate.

## User guide

[Read the complete user guide →](user-guide/README_EN.md)

Detailed guidance covers installation, first setup, voice selection, every setting, PCM / MP3, networking and proxies, and troubleshooting.

[Configuration](user-guide/README_EN.md#configuration) · [Audio formats](user-guide/README_EN.md#audio-formats) · [Network and proxy](user-guide/README_EN.md#network-and-proxy) · [Troubleshooting](user-guide/README_EN.md#troubleshooting)

## FAQ

**Q: Does it charge if I don't set a voice?**

A: Charging depends on the selected synthesis model. Fish Audio uses a random voice when none is set; paid models still consume API credits, while free models follow the official promotion conditions.

**Q: Does preview playback charge credits?**

A: No. The plugin plays the public audio supplied with the voice and does not call TTS, so preview also works without an API Key.

**Q: Why doesn't the balance change immediately after playback?**

A: Fish Audio deductions may be delayed. Refreshing immediately after playback may briefly show the previous balance.

**Q: Do I need an API Key before searching for voices?**

A: Voice search, lookup by ID and preview work without an API Key. Synthesis requires a valid API Key; paid models also require sufficient API credits.

**Q: Will clearing the cache affect my selected voice?**

A: No. It only removes cached voice cover images. Your voice ID and selected voice information remain; covers load again when next displayed.

## Official Fish Audio resources

For accounts, the voice community, model capabilities, parameter guidance and billing rules, consult the official Fish Audio pages.

[Website](https://fish.audio) · [API keys](https://fish.audio/app/api-keys) · [Voice community](https://fish.audio/app/discovery) · [Documentation](https://docs.fish.audio) · [API pricing](https://docs.fish.audio/developer-guide/models-pricing/pricing-and-rate-limits)

## Development

Start with the [development and contribution guide](../CONTRIBUTING.md) for setup, required reading, build verification and submission requirements.

Quick verification: on Windows with the .NET 10 SDK, run:

```powershell
.\build.ps1 -Release -Clean -Test
```

The package is written to `STranslate.Plugin.Tts.FishAudio.spkg` in the repository root.

## Feedback and contributions

[Report an issue or suggestion](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/issues) · [Changelog](../CHANGELOG.md) · [Contribute](../CONTRIBUTING.md)

## License

[MIT License](../LICENSE)
