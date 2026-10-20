# Fish Audio evidence probe

This .NET 10 console tool captures the plugin's seven API surfaces without changing voices, dictionaries, billing settings, or other account resources. Synthesis uses only the exact native `model: s2.1-pro-free` header and refuses to start at or after `2026-12-01T00:00:00Z`. The local cutoff follows this project's UTC policy; the official promotion does not specify a timezone. A free-tier response can still fail or be rate limited.

Run from the repository root:

```powershell
dotnet run --project tools/FishAudioProbe -- --self-test
dotnet run --project tools/FishAudioProbe -- --output C:/private/fish-evidence --group public
```

`public` is the default group and does not read `FISH_API_KEY`. It reads the anonymous engine catalog, anonymous voices, and public voices with the plugin's `Bearer dummy` convention. Other groups require `FISH_API_KEY` in the child process environment. The tool accepts no key argument and never prints the key. Provision the environment through your existing secret handling; do not put credentials in command history or repository files.

```powershell
dotnet run --project tools/FishAudioProbe -- --output C:/private/fish-evidence --group authenticated
dotnet run --project tools/FishAudioProbe -- --output C:/private/fish-evidence --group http-tts
dotnet run --project tools/FishAudioProbe -- --output C:/private/fish-evidence --group ws-tts
```

`all` explicitly opts into every group, including free synthesis. `--case ID` can be repeated to select cases within a group. Voice-list and detail reads still run as dependencies to obtain genuine voice IDs and resource URLs; selecting a case does not bypass these reads. For example:

```powershell
dotnet run --project tools/FishAudioProbe -- --output C:/private/fish-evidence --group public --case public-sample-detail-full --case public-sample-detail-range
```

## Cases

| Group | Case IDs | Purpose |
| --- | --- | --- |
| public | `catalog-all`, `catalog-speech` | Anonymous full catalog and documented speech filter. |
| public | `voices-anonymous` | Separate anonymous list observation. |
| public/authenticated | `<prefix>-voices-default`, `<prefix>-voices-search` | List with `score` sorting and a title search. |
| public/authenticated | `<prefix>-voices-page-2`, `<prefix>-voices-no-result` | A second page and an extremely unlikely title; the actual body establishes whether it is empty. |
| public/authenticated | `<prefix>-voice-detail-1`, `-2`, `-missing` | Two IDs obtained from actual lists and a nonexistent ID. |
| public/authenticated | `<prefix>-sample-full`, `-sample-range` | List-returned sample, full and `Range: bytes=0-1023`. |
| public/authenticated | `<prefix>-sample-detail-full`, `-sample-detail-range` | Latest detail-returned sample, including a signed URL when returned. |
| public/authenticated | `<prefix>-cover-full` | Returned cover path on the allowlisted CDN. |
| authenticated | `balance-valid`, `balance-repeat`, `balance-missing-auth`, `balance-invalid-auth` | Own balance, repeat, omitted authentication, and fixed synthetic invalid authentication. |
| http-tts | `http-pcm-en`, `http-pcm-en-repeat` | Repeat `Hello.` with PCM at 44.1 kHz. |
| http-tts | `http-mp3-zh`, `http-mp3-zh-repeat` | Repeat `你好。` with MP3 at 44.1 kHz / 128 kbps. |
| http-tts | `http-invalid-format` | Short request with invalid format, while retaining the exact free model header. |
| ws-tts | `ws-pcm`, `ws-mp3` | First-audio-gated text streaming with normal stop. |
| ws-tts | `ws-pcm-segmented` | Same short prefix, then about 250 characters in three separate text events after actual first audio, then stop. |
| ws-tts | `ws-cancel` | Client abort after actual first audio; classified as intentional cancellation. |
| ws-tts | `ws-missing-auth` | Missing-authentication handshake with the free model header still present. |

`<prefix>` is `public` or `authenticated`. Synthesis-only groups use `synthesis-discovery-*` for prerequisite voice reads. Voice IDs and asset URLs are never invented for successful cases. Missing dependencies produce `skipped-dependency`, not fixtures or successful captures. The nonexistent detail ID is a deliberately harmless negative case.

## Capture layout

Every invocation creates a unique directory and appends a pointer to the root index. Reusing `--output` preserves earlier runs:

```text
manifest.json
run-<UTC>-<GUID>/
  manifest.json
  cases/<case-id>/
    case.json
    request.json
    response-headers.json
    body.json or body.bin
```

The root manifest has `schemaVersion` and `runs[]` pointers. Each run manifest records limits, the pinned synthesis policy, and all case results. `case.json` records UTC boundaries, HTTP status, outcome, request-sent state, sanitized source-URL status, and body metadata. Body metadata includes original and stored byte counts, SHA-256 for both representations, sanitization, completeness, and read failure. JSON is reserialized and scrubbed; its stored hash can differ even without secret-bearing fields. A partial read retains the actual received prefix.

WS cases also contain `sent-*.msgpack`, `received-*.msgpack`, per-received-message metadata, extracted `audio-*.bin`, aggregate `audio.bin`, and `events.json`. Events record UTC, monotonic elapsed milliseconds, sequence order, receive fragments, complete MessagePack messages, and the corresponding audio artifact. A .NET receive fragment is a transport observation and does not expose the original wire frame boundaries. An interrupted message has `complete: false`; successfully reassembled messages have `complete: true`. Audio is extracted only from decoded nonempty `audio` fields. No tail text is sent before the first such field arrives. A successful ordinary session requires a `finish` event with `reason: stop`.

Failed WS upgrade bodies are saved when the HTTP transport exposes them. Otherwise `handshakeBodyCapture` explicitly records that no body was captured. Intentional abort uses `cancelled-by-case`; a timeout or server error has a different outcome. An empty failed audio aggregate is not a successful synthesis result.

## Bounds and redaction

Cases run sequentially with no application retry, at most 120 seconds and 8 MiB of received body/message bytes per case. WS connection, first-audio wait, receive, and sends share a cancellation deadline equal to the earlier of the case bound and free cutoff. Each WS message also rechecks UTC immediately before sending, so a connection opened before expiry cannot authorize fresh text after it. The injected `TimeProvider` permits deterministic offline cutoff tests. Redirects and cookies are disabled. API requests are restricted to the documented allowlist. Asset requests use only API-returned URLs on allowlisted HTTPS hosts and never forward account authorization. Relative covers use the observed public CDN base. A returned URL outside the allowlist is refused for review.

Headers, nested JSON, logs, exceptions, signed URL queries, and MessagePack strings are scrubbed. Exact UTF-8 environment-key bytes are also removed from binary captures, including nested MessagePack binary fields; ordinary audio bytes without that exact sequence remain unchanged. URI user information and signed query credentials are removed before persistence. Wallet response `_id` and `user_id` are removed without changing public voice-owner IDs. Original hashes retain integrity information without retaining those credentials. If a MessagePack message needs scrubbing it is repacked, and original/stored hashes disclose the change. Signed URLs in stored evidence are intentionally unusable for authenticated replay.

`--self-test` is offline, does not read environment credentials, and does not call the network. It checks paid-model refusal, the UTC cutoff, nested/header/log/URI redaction, MessagePack audio preservation, first-audio gating, bounded partial reads, real-body capture through a synthetic transport, authorization isolation, and append-only run indexing. Temporary synthetic fixtures are deleted and are not delivered as network evidence.

Exit code zero means the selected run finished recording; inspect case outcomes to determine coverage and success. An HTTP error remains a real failed call. Successful free synthesis proves the captured request at that time, not an ongoing billing guarantee, latency SLA, decoder behavior, or paid-model capability.

See [the API references](../../docs/references/README.md) and [the collected evidence index](../../docs/evidences/2026-10-20/manifest.json).

## Offline evidence analysis

The companion script verifies stored artifact hashes and analyzes the already captured bytes. It makes no service requests and uses no API key:

```powershell
python tools/FishAudioProbe/analyze-evidence.py docs/evidences/2026-10-20
```

The positional argument is the evidence root. Python and `ffprobe`/`ffmpeg` must be available on PATH. The script validates all case-body stored hashes, then writes `audio-analysis.json` in that root. PCM statistics and decoding can test a proposed representation, durations, channel/rate information, and codec validity. They do not establish physical playback, audible speech intelligibility, or a cloud guarantee of PCM endianness. See the dated evidence README for the captured results.
