# Streaming acceptance in STranslate 2.0.10

This record separates installed-host observations from the [production-DLL harness and raw API/device captures](streaming-2026-10-20/README.md). STranslate 2.0.10 on Windows invoked the installed plugin's `Main.PlayAudioAsync` entry point with the fixed neutral text `Hello. This is a streaming playback check.` Both MP3 and PCM completed through the native playback and device-drain stages. The configured model was exactly `s2.1-pro-free`; no alternate model or automatic synthesis retry is reported.

The three sanitized observation files below preserve both successful runs and the initial PCM failure. No API Key, Authorization value, host settings file, machine-local path, or full host log is published. Their `testKeyEqualityChecked: true` field records an equality check without retaining either credential value. The validation-only manifest versions distinguish installations used during testing; they are not published release versions.

## Current outcomes

| Format and validation manifest | Actual host outcome | Evidence |
|---|---|---|
| MP3, `1.1.2.6` | Short-text streaming completed through all eight native stages, including playback and device drain. | [Sanitized MP3 observation](streaming-host-2026-10-20/host-mp3-streaming.json) |
| PCM, `1.1.2.7`, first attempt | Connection timed out before `Connected`; cleanup then raised a separate WPF thread-access exception. This is a failed host attempt. | [Immutable first PCM attempt](streaming-host-2026-10-20/host-pcm-first-attempt.json) |
| PCM, `1.1.2.8`, after the cleanup fix | Short-text streaming completed through all eight native stages and device drain after opening and closing the settings view. No thread-access exception was observed. | [Sanitized successful PCM observation](streaming-host-2026-10-20/host-pcm-streaming.json) |

Installed-host short-text playback and the settings observations below are accepted within this recorded scope. Long-text tail gating and cancellation remain isolated production-harness observations; no installed-host long-text or cancellation case is claimed. Native WebSocket streaming is the formal TTS path, not a setting enabled by a separate streaming toggle.

## MP3 host completion

The MP3 invocation began around `2026-10-20T14:13:24Z`. The sanitized record was collected at `14:16:30.985Z`; its host log timestamps are in UTC+08:00. Native stage entries are emitted together at completion, so their log wall times are not the individual stage times. The stage's elapsed value is the relevant monotonic measurement.

| Native stage | Elapsed seconds |
|---|---:|
| Request | 0.000 |
| Connected | 0.915 |
| PrefixSent | 0.960 |
| FirstAudio | 4.791 |
| Finish | 4.792 |
| FirstPcm | 4.803 |
| PlaybackStarted | 4.886 |
| Drained | 8.615 |

This short final input can send stop without waiting for playback, allowing the provider's finish to arrive before the first decoded PCM or output start. The observed ordering is therefore consistent with the production short-input path. The native `PlaybackStarted` and `Drained` stages establish that the installed production player reached real output startup and its device-tail completion condition. No physical listening assessment, microphone capture, or guaranteed cloud latency is claimed.

## Historical PCM failure and cleanup defect

The first PCM invocation began around `2026-10-20T14:24:51Z`; the record was collected at `14:28:42.843Z`. Its retained stage set contains only `Request` at zero. The host subsequently reported `TTS request failed (TimeoutException)` around `14:25:06.688Z`. No `Connected`, incoming audio, playback, finish, or drain was recorded for that attempt.

During the same failed operation's cleanup, a WPF `InvalidOperationException` reported cross-thread access. The retained stack passes through `RelayCommand.NotifyCanExecuteChanged`, `SettingsViewModel.set_IsApiKeyInputLocked`, `OperationActivityCounter.PublishObservableState`/`End`, the API-key operation lease cleanup, and `Main.PlayAudioAsync`. This identifies a separate client cleanup/notification path that must marshal UI state correctly when a background streaming operation ends.

The connection timeout occurred before audio transfer and remains classified as a connection failure. The cleanup correction marshals the operation-counter UI notifications onto the UI dispatcher. A regression using a real STA WPF command subscription reproduced the thread-access failure before the correction and passed after it. That red/green regression validates the client cleanup path separately from the network observation; it does not explain or repair the earlier upstream connection timeout.

The first-attempt JSON is preserved unchanged. The subsequent successful PCM run below is a separate invocation on the corrected client, not a replacement for the historical failure or evidence of an automatic retry. The timeout's transport cause remains unknown.

## PCM host completion after cleanup correction

The successful PCM invocation began around `2026-10-20T14:34:53Z`; its sanitized record was collected at `14:35:55.728Z`. Validation manifest `1.1.2.8` loaded the corrected production DLL identified by SHA-256:

```text
bf191c2af6e87e93175cd3f3991053f22ed8846910d5fa3a98d2dbd2656e743c
```

The settings view was opened and then closed before the host entry-point invocation. This matters because the earlier cleanup failure involved the view's WPF command subscription. The successful run contains all eight native stages and records `threadAccessExceptionObserved: false`.

| Native stage | Elapsed seconds |
|---|---:|
| Request | 0.000 |
| Connected | 0.700 |
| PrefixSent | 0.745 |
| FirstAudio | 4.158 |
| FirstPcm | 4.160 |
| Finish | 4.220 |
| PlaybackStarted | 4.268 |
| Drained | 8.140 |

The native FirstPcm-to-PlaybackStarted interval was approximately 108 ms. This is one installed-host observation; it is not a network-latency guarantee, a format comparison, or a physical listening assessment. As with MP3, the short input permits finish before playback startup. The final native drain stage supplies the output-completion evidence.

## Installed settings observations

The successful PCM record also retains these sanitized observations from the validation sequence:

| Setting behavior | Observed result |
|---|---|
| Key editing while the password field retained focus | Clearing and re-entering the key saved the current value before focus changed; the credential value itself is omitted. |
| Legacy schema-2 configuration before manual format selection | Validation `1.1.2.6` preserved MP3 and its 192 kbps preference. |
| PCM selected in validation `1.1.2.7` | The MP3 bitrate control was disabled for PCM while retaining the 192 kbps preference. |
| Restart into validation `1.1.2.8` | PCM remained selected after the host restart. |

These observations cover immediate key persistence, migration/preservation of an existing format preference, format-specific bitrate availability, and PCM persistence across restart. No streaming enable/disable toggle was involved.

## Reusable observation method

For another authorized installed-host check, build and install the intended plugin package using the repository's normal validation process. Record the source package/plugin hash and validation manifest version separately. In the plugin settings, choose the intended audio format; native streaming is the formal TTS path. Use the exact permitted free model while its UTC availability guard is valid. Supply the test credential through local secret handling; do not export the settings file, credential field, or unfiltered logs. Open and close the settings view before invocation when checking cleanup notifications associated with view command subscriptions.

Invoke the installed host's normal TTS action with a fixed neutral short sentence. After the operation completes or fails, retain only the matching native stage entries and the minimal sanitized failure context. Record the host version, format, configured model, validation version, invocation/collection UTC, and the host log's timestamp offset. A successful HTTP/WebSocket connection alone is insufficient: completion needs the relevant production playback and drain observations. Keep intentional cancellation and failed connection attempts as distinct outcomes.

If testing long-text tail ordering or cancellation, add a separately labeled host case with explicit observations. These short MP3/PCM results must not stand in for those checks. See the [isolated harness instructions](streaming-2026-10-20/harness/README.md) for source-read/callback instrumentation and the distinction between native stage and harness stopwatch origins.

## Integrity

The sanitized source JSON files were copied without further transformation. Their hashes describe the published bytes, including retained escaped carriage returns in the selected log lines. The only stack source path is the package's deterministic `/_/src/...` mapping; no user profile or local checkout path is retained. The repository's `docs/evidences/** -text` attribute protects these bytes during checkout.

| Published file | SHA-256 |
|---|---|
| `host-mp3-streaming.json` | `6aed28e1407ad1cdb4aa9f528807123286b76309c9501d394395cf898f44d943` |
| `host-pcm-first-attempt.json` | `4e03360143264d7c7063291682e736bfd18436fc05d013eacc203dc0ccfcb08c` |
| `host-pcm-streaming.json` | `5b5ad21f0fd4a385dcbcefefdbc612fb339471e261d23353e80cd1fb55ece98d` |

This host record remains separate from the raw-capture archive. Its successful PCM run uses the corrected DLL above; the original isolated captures retain their earlier DLL identity and original bytes. The archive index points here for installed-host short-text and settings acceptance, while long-text/cancellation evidence remains explicitly scoped to the isolated production harness.
