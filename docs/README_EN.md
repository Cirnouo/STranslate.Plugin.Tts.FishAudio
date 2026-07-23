<div align="center">
  <a href="https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio">
    <img src="images/icon.svg" alt="Fish Audio TTS" width="128" height="128" />
  </a>

  <h1>Fish Audio TTS</h1>

  <p>
    <a href="https://fish.audio">Fish Audio</a> Text-to-Speech plugin for <a href="https://github.com/ZGGSONG/STranslate">STranslate</a>
  </p>

  <p>
    <img alt="License" src="https://img.shields.io/github/license/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Release" src="https://img.shields.io/github/v/release/Cirnouo/STranslate.Plugin.Tts.FishAudio?style=flat-square" />
    <img alt="Downloads" src="https://img.shields.io/github/downloads/Cirnouo/STranslate.Plugin.Tts.FishAudio/total?style=flat-square" />
    <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square" />
    <img alt="WPF" src="https://img.shields.io/badge/WPF-Plugin-blue?style=flat-square" />
  </p>

  <p>
    <a href="../README.md">简体中文</a> | <a href="README_TW.md">繁體中文</a> | <b>English</b> | <a href="README_JA.md">日本語</a> | <a href="README_KO.md">한국어</a>
  </p>
</div>

---

<div align="center">
  <img src="images/overview.png" alt="Plugin overview" width="700" />
</div>

## 1 Feature Overview

- **High-quality synthesis**: Supports Fish Audio s2.1-pro-free / s2.1-pro / s2-pro / s1 synthesis models and 80+ languages
- **Voice selection**: Search by name or enter a voice ID directly; supports preview, selection, clearing, and paginated browsing
- **Synthesis controls**: Supports speed, volume, loudness normalization, MP3 bitrate, expressiveness, diversity, latency mode, text normalization, and context conditioning
- **Emotion markup**: Add Fish Audio emotion markers in text, such as `[laugh]` for S2-Pro or `(happy)` for S1
- **Voice community**: Browse public voices from the Fish Audio creator community, then quickly apply a matching voice through in-plugin search, preview, or voice ID setup
- **Localization**: Simplified Chinese, Traditional Chinese, English, Japanese, Korean

## 2 Quick Start

### 2.1 Installation

Installing from the STranslate plugin marketplace is recommended

**STranslate Plugin Marketplace**

1. Open STranslate
2. Go to **Settings -> Plugins -> Marketplace**
3. Search for or find **Fish Audio TTS**, then download and install it

**Manual install**

1. Open the [Releases](https://github.com/Cirnouo/STranslate.Plugin.Tts.FishAudio/releases) page
2. Download the latest `STranslate.Plugin.Tts.FishAudio.spkg`
3. In STranslate, go to **Settings -> Plugins -> Install**
4. Select the downloaded `.spkg` file

### 2.2 Get and Set an API Key

> [!TIP]
> Register with a `.edu` email and complete student verification to receive 5 USD in credits. Entry: [Fish Audio Students](https://fish.audio/students/).

Register with an email address and sign in to Fish Audio, open [Developer > API Keys](https://fish.audio/app/api-keys), click **Create new key**, then copy the key

<!-- Screenshot: images/fish-audio-api-keys.png
     Content: Fish Audio API Keys page, highlighting where to create or copy an API Key. Hide any real API Key. -->
<div>
  <img src="images/fish-audio-api-keys.png" alt="Fish Audio API Keys page" width="700" />
</div>

Paste it into the plugin settings **API Key** input

<div>
  <img src="images/plugin-api-key-input.png" alt="STranslate Fish Audio Plugin API Key input" width="360" />
</div>

### 2.3 Buy API Credits

> [!TIP]
> The S2.1 Pro model is free for a limited time through `2026-07-31` UTC. Select `s2.1-pro-free` in the plugin to use it. Details: [Fish Audio S2.1 Pro: Free Text-to-Speech API for Developers](https://fish.audio/blog/s2-1-pro-free-api/)

Fish Audio TTS consumes Fish Audio API credits. You can purchase credits at [Developer > Console](https://fish.audio/app/developers/billing/)

<!-- Screenshot: images/fish-audio-billing.png
     Content: Fish Audio Billing/Balance/Purchase Credits entry, highlighting where to buy or top up credits. -->
<div>
  <img src="images/fish-audio-billing.png" alt="Fish Audio API credit purchase entry" width="700" />
</div>

### 2.4 Get a Voice

The voice determines the timbre used for the final reading.

The Fish Audio voice community contains public voices shared by creators. You can browse and preview voices on the website by name, character, or language, then return to the plugin to search for the same voice or copy its voice ID directly. Entry: [Creative > Discover](https://fish.audio/app/discovery)

<div>
  <img src="images/fish-audio-voice-community.png" alt="Fish Audio voice community" width="700" />
</div>

### 2.5 Set a Voice

The plugin offers **Search by Name** and **Set Directly by ID**

#### 2.5.1 Search by Name

Enter a voice name on the plugin's **Search** tab, then click the search icon or press `Enter`. The result list supports preview and pagination; click **Select** to apply the voice.

<div>
  <img src="images/plugin-voice-search.png" alt="STranslate Fish Audio Plugin voice search" width="360" />
</div>

#### 2.5.2 Set Directly by ID

Open the target voice detail page in the voice community, click the more menu (`...`) in the page action area, then choose `Copy Model ID`. Fish Audio currently still uses "Model ID" for this menu item; the copied value is the voice ID required by the plugin.

<!-- Screenshot: images/fish-audio-voice-id.png
     Content: Fish Audio voice detail page, highlighting where to copy the voice ID. Do not expose account information. -->
<div>
  <img src="images/fish-audio-voice-id.png" alt="Get a voice ID from a Fish Audio voice page" width="550" />
</div>

Return to the plugin's **By ID** tab, paste the ID, then click the confirm button. The plugin loads the voice information and applies that voice.

<div>
  <img src="images/plugin-voice-id.png" alt="STranslate Fish Audio Plugin voice ID input" width="360" />
</div>

## 3 Configuration

<details>
<summary><b>Parameter Reference</b> (click to expand)</summary>

| Setting | Default | Description |
| :-- | :--: | :-- |
| API Key | - | Fish Audio API key, required. |
| Voice ID | Random Voice | Search to select or enter manually. When empty, a random voice is used. |
| Synthesis model | `s2.1-pro-free` during the free period, then `s2.1-pro` | Through 2026-07-31 UTC: `s2.1-pro-free`, `s2.1-pro`, `s2-pro`, `s1`; starting 2026-08-01 UTC, the free model is hidden. |
| MP3 bitrate | `192 kbps` | Available values: `64`, `128`, `192`. |
| Speed | `1.0` | Range `0.5` to `2.0`. |
| Volume | `0 dB` | Range `-10 dB` to `+10 dB`. |
| Loudness normalization | On | Available for `s2.1-pro-free`, `s2.1-pro`, and `s2-pro`; shown disabled for `s1`. |
| Expressiveness | `0.7` | Range `0` to `1`. |
| Diversity | `0.7` | Range `0` to `1`. |
| Latency mode | Quality first | Quality first / Balanced / Low latency. |
| Text normalization | Off | Converts numbers, unit symbols, and similar content into text that is easier to read aloud. |
| Context conditioning | On | Uses earlier chunks from the same synthesis to maintain voice consistency; it does not reference previously generated audio. |

</details>

## 4 Advanced Usage

### 4.1 Emotion Control

Fish Audio controls emotion through inline markers in the text, with no extra API parameters required. Write the markers directly into the text to be synthesized. Common uses include changing sentence emotion, adjusting tone intensity, or inserting vocal effects such as laughter, gasps, and whispers.

**S2 series models** (recommended) use square brackets and natural-language descriptions. Sentence-level emotion is usually placed at the start of a sentence, while tone or sound-effect markers can be placed where they should take effect:

```text
[angry] This is unacceptable!
I can't believe [gasp] you actually did it [laugh]
[whisper] This is a secret
```

**S1** uses parentheses and a fixed tag set, usually placed at the start of a sentence:

```text
(happy) The weather is great today!
(sad)(whispering) I miss you
```

Use one main emotion per sentence when possible, and avoid stacking too many markers in very short text. For the complete tag list, composition guidance, and troubleshooting notes, see the official documentation: [Emotion Control](https://docs.fish.audio/developer-guide/core-features/emotions).

### 4.2 Fine-grained Control

Fine-grained control specifies the exact pronunciation of a word, character, or phrase. It is useful for names, brands, polyphonic Chinese characters, technical terms, or Japanese pitch-accent control. Wrap the target pronunciation with `<|phoneme_start|>` and `<|phoneme_end|>`, and keep normal punctuation and surrounding text outside the tags.

English uses CMU Arpabet, usually replacing a single word:

```text
I am an <|phoneme_start|>EH1 N JH AH0 N IH1 R<|phoneme_end|>.
```

Chinese uses pinyin with tone numbers, one tag per syllable:

```text
我是一个<|phoneme_start|>gong1<|phoneme_end|><|phoneme_start|>cheng2<|phoneme_end|><|phoneme_start|>shi1<|phoneme_end|>。
```

Japanese uses OpenJTalk-style romanized phonemes and pitch numbers, usually wrapping a short phrase:

```text
<|phoneme_start|>ha0shi1ga0<|phoneme_end|>見えます。
```

For the full format, symbol tables, and generation tools, see the official documentation: [Fine-grained Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control), [English Phoneme Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/english), [Chinese Phoneme Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/chinese), [Japanese Phoneme Control](https://docs.fish.audio/developer-guide/core-features/fine-grained-control/japanese).

## FAQ

**Q: Does it charge if I don't set a voice?**

A: Yes. When no voice is set, Fish Audio generates audio with a random voice and still consumes API credits

**Q: Does preview playback charge credits?**

A: No. Preview playback uses the voice's public audio and does not call the TTS API, so it also works without a configured API Key

**Q: Why doesn't the balance change immediately after playback?**

A: Fish Audio's balance deduction may be delayed. Refreshing the balance immediately after playback may briefly show the old balance

**Q: Do I need to configure an API Key before voice search?**

A: Voice search, voice lookup by ID, and preview playback can all be used without a configured API Key. Real synthesis still requires a correctly formatted, usable API Key and available balance

**Q: Will clearing the cache affect the selected voice?**

A: No. Clearing the cache only removes voice cover image cache files. The voice ID and selected voice information remain intact, and the cover is reloaded the next time it is shown

## Build

```powershell
# Standard build (Debug + .spkg package)
.\build.ps1

# Clean build
.\build.ps1 -Clean

# Clean build and run regression tests
.\build.ps1 -Clean -Test

# Release build
.\build.ps1 -Configuration Release
```

Build output is written to `STranslate.Plugin.Tts.FishAudio.spkg` in the repository root

<details>
<summary><b>Environment Requirements</b></summary>

- .NET 10.0 SDK
- Windows (WPF project)

</details>

## Credits

- [STranslate](https://github.com/ZGGSONG/STranslate) - a ready-to-use translation and OCR tool
- [Fish Audio](https://fish.audio) - the speech synthesis API provider
- [iNKORE WPF Modern UI](https://github.com/iNKORE-NET/UI.WPF.Modern) - a modern UI control library for WPF

## License

[MIT](LICENSE)
