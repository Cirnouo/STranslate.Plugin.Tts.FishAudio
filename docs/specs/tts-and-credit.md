# Formal TTS and account credit

Status: current implementation. [Main](../../STranslate.Plugin.Tts.FishAudio/Main.cs) and [FishAudioApi](../../STranslate.Plugin.Tts.FishAudio/FishAudio/FishAudioApi.cs) define the current path.

Formal TTS rejects blank text, then performs network → non-empty API Key → API Key shape preflight. Accepted shapes are 32 lowercase hex characters or `sk-fish-` plus 43 ASCII letters/digits/underscore/hyphen. No remote validation request or applied-key state precedes synthesis. The supplied key is forwarded unchanged as a Bearer credential; shape validation does not authenticate it.

The plugin sends one `POST https://api.fish.audio/v1/tts` through host `PostAsBytesAsync`, with a 60-second timeout, `model` header and `format: mp3`. The body contains the full text, configured bitrate, temperature, top_p, normalize, latency, request-local conditioning and prosody speed/volume. Loudness normalization is conditional on the TTS model. A non-empty VoiceId becomes reference_id; an empty one omits that property. User text/markup is passed through without a client text-segmentation or phoneme parser.

Only after the response has been fully materialized as bytes does the plugin invoke the captured host AudioPlayer.PlayAsync(byte[], token). SDK 1.0.15 supports explicit AudioData/PCM for other callers, but this plugin's production path does not use it. There is no incremental decoder, output buffer, WS session or streaming first-audio measurement here. An HTTP chunked response alone would not change this complete-buffer behavior.

The optional ViewModel locks API Key input across request and playback. Successful playback starts a silent credit refresh; failures use existing logging/error extraction or localized timeout UI. Caller cancellation propagates without the plugin's generic error snackbar. An operation uses its original initialization snapshot throughout; see [lifecycle](architecture-and-lifecycle.md).

Credit uses `GET /wallet/self/api-credit`, a Bearer credential, the same preflight and a 15-second timeout. The response credit field is deserialized as a string. Manual refresh displays request latency for four seconds and shows interactive errors. Startup refresh is non-blocking and silent; post-TTS refresh is silent. Results apply only while their key, ViewModel and initialization/credit cycle remain current. Overlapping activity must not unlock input early.

A random/default voice fallback does not establish free synthesis. Provider deduction timing and a temporarily unchanged balance cannot establish cost. This spec records implemented request behavior; no successful live synthesis or actual audible output is asserted.

Verification: [TTS suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/TtsSuite.cs), [API suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/FishAudioApiSuite.cs), [credit suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/CreditRefreshSuite.cs), [startup credit suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/StartupCreditSuite.cs). Rationale: [DD-009](../adr/current/DD-009.md), [DD-030](../adr/current/DD-030.md), [DD-039](../adr/current/DD-039.md), [DD-046](../adr/current/DD-046.md).
