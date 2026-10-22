# PCM output queue comparison — 2026-10-22 UTC

The [portable selected-artifact bundle](pcm-output-2026-10-22/REPORT.txt) preserves one real exact-free-model synthesis, its raw native WebSocket messages, actual provider PCM and traces, and a separate offline same-source output comparison. Run `python docs/evidences/pcm-output-2026-10-22/verify.py` from the repository root. It needs only Python's standard library, with no key, network, installed host or audio device.

## What the capture established

The 950-character user-supplied English sample was synthesized once with exact `s2.1-pro-free`, PCM and the recorded parameter snapshot. All 6,053,526 received PCM bytes reached the provider unchanged. The only 5,814 padding bytes followed the real 68.634-second tail. There was no digital clipping or speech-time software-ring padding. The first fifteen seconds sounded clean to the user when played as an unchanged PCM-to-WAV copy through another playback path.

These observations did not establish whether the original audible symptom occurred during that synthesis. Byte equality alone cannot identify an upstream quality problem or a downstream device problem. The capture contains no acoustic loopback or microphone recording.

## Same-source offline comparison

Two separate offline experiments then replayed the same first fifteen seconds through the production player, decoder, ring and adapter with the same source-availability schedule. A used the existing default; B injected only the alternate WaveOut buffer configuration. Both used the same production assembly. No additional synthesis, key or network access was involved.

| Observation | A: 100 ms / two buffers | B: 200 ms / four buffers |
|---|---:|---:|
| Per-device buffer | 50 ms / 4,410 bytes | 50 ms / 4,410 bytes |
| Real PCM delivered | 1,323,000 bytes, exact source match | 1,323,000 bytes, exact source match |
| Speech-time ring padding | 0 | 0 |
| Trailing read-ahead padding | 8,820 bytes | 17,640 bytes |
| Refill gaps over 75 ms after initial prefill/first refill | 34 | 0 |
| Reported device-position plateaus over 10 ms after startup | 34 | 0 |
| Playback callback to Stop | 15,383.35 ms | 15,050.39 ms |
| User's listening report | Same electrical noise | Reported noise absent |

The comparison supports additional queued output headroom on the exercised setup. It is one sequential, unblinded pair; it does not prove an exact driver mechanism or reliability across all loads and devices. Refill and device-position records are API observations, not measurements at the speaker.

## Resulting implementation choice

The production factory now uses 200 ms/four buffers, retaining approximately 50 ms per buffer. Its device-free regression inspects the actual configured NAudio object and preserves caller synchronization-context restoration. Decoder, source bytes, codec, volume, software startup, text gate, ring bounds and drain logic remain unchanged. This adds nominal queue capacity of 100 ms; it does not imply exactly 100 ms more observed startup latency.

The bundle's [manifest](pcm-output-2026-10-22/manifest.json) separates exact raw copies, a sanitized request-summary projection and omitted/reconstructable originals. The original capture summary remains unchanged and therefore lists some intentionally omitted original files. The verifier reconstructs upstream PCM from raw MessagePack, verifies actual provider bytes and real-tail drain, reconstructs both omitted replay renders from their exact source prefix and zero suffix, and checks their original hashes. The [field map](pcm-output-2026-10-22/request-summary-field-map.json) documents removal of local source/instrumentation context.

Rationale: [DD-061](../adr/current/DD-061.md). Current behavior: [incremental playback](../specs/streaming-playback.md). The earlier capture and A/B used the previous production assembly; they are evidence for the narrow configuration choice, not a claim that a later package was the one captured.
