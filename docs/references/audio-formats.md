# Audio formats and incremental decoding

The native [HTTP format guide](https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech) defines the following output choices. The ordinary [WS protocol](https://docs.fish.audio/api-reference/endpoint/websocket/tts-live) uses the same request fields and returns binary audio in the chosen format. These are cloud API facts, not promises imported from self-hosted Fish Speech.

| Format | Documented rate / channels / depth | Incremental implementation implication |
| --- | --- | --- |
| PCM | 8/16/24/32/44.1 kHz; default 44.1; 16-bit mono. | No compressed decoder startup; roughly 705.6 kbps at 44.1 kHz. Representation details below need verification. |
| WAV | Same rates, depth, and channels as PCM. | Parse a real container; streaming header and length strategy are unspecified. |
| MP3 | 32/44.1 kHz; default 44.1; mono; 64/128/192 kbps. | Keep a continuing decoder and buffer incomplete frames. Smaller network payload than PCM. |
| Opus | 48 kHz; mono; -1000 auto or 24/32/48/64 kbps. | Native ordinary endpoint does not fully specify its returned container/packetization. Decoder integration needs actual validation. |

The schema uses **bps** values for `opus_bitrate` and **kbps** values for `mp3_bitrate`. The Opus schema defaults to `-1000` while format prose calls 32 kbps the default; avoid silently equating those declarations. Format-specific prose, rather than the unbounded integer type alone, supplies documented sample-rate choices.

## What remains unverified

The cloud reference says 16-bit mono for PCM, but does **not** explicitly publish raw PCM signedness or byte order. Signed 16-bit little-endian is a plausible implementation hypothesis, not a documented Fish cloud guarantee. Validate it against a real free response using sample statistics, duration/plausible amplitude checks, a correctly configured decoder, and actual playback. Document the observation before fixing a playback interpretation. The probe preserves the original audio bytes so this can be assessed separately.

The native sources also do not specify WAV header rewriting or Opus's exact container. Do not assume every WAV has a 44-byte header, that every output byte stream is a standalone file, or that Opus necessarily means Ogg. [RIFF/WAVE parsing](https://learn.microsoft.com/en-us/windows/win32/xaudio2/resource-interchange-file-format--riff-) and [Ogg Opus framing](https://www.rfc-editor.org/rfc/rfc7845) are relevant only after that actual representation is established.

## Boundaries and format choice

HTTP read chunks, HTTP transfer chunks, WS wire frames, .NET receive fragments, MessagePack messages, audio-event chunks, codec frames, and PCM sample boundaries are different layers. Preserve order across arbitrary splits. Reassemble a full WS message before MessagePack decoding; feed extracted audio through one continuing decoder. For 16-bit mono PCM, retain a leftover byte across calls rather than dropping it. MP3 frame decoding can depend on earlier data through the bit reservoir, so opening a new decoder per audio event is unsafe. Sources: [RFC 9112 chunked transfer](https://www.rfc-editor.org/rfc/rfc9112.html#name-chunked-transfer-coding), [RFC 6455 fragmentation](https://www.rfc-editor.org/rfc/rfc6455.html#section-5.4), [MP3 dependency discussion in RFC 3119](https://www.rfc-editor.org/rfc/rfc3119), [PCM block alignment](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/ns-mmeapi-waveformatex).

The official [ordinary streaming guide](https://docs.fish.audio/features/realtime-streaming) recommends MP3 or PCM as streaming-friendly. Neither is universally the fastest end to end. PCM avoids compressed decoding but increases transfer volume: 44.1-kHz, 16-bit mono is 88,200 bytes/second, about 5.29 MB/minute. MP3 at 128 kbps is about 0.96 MB/minute and needs frame parsing and decoder state. Server encoding, network path, initial buffering, device buffering, and silence all affect audible start.

The chosen plugin direction is PCM for new configurations and MP3 compatibility for existing v1 configurations. Keep the documented 44.1-kHz default; there is no source-backed reason to reduce it to 24 kHz as a universal optimization or to claim 24 kHz is the cloud model's native fidelity. PCM's byte order still requires the explicit real-response validation above. The bounded probe tests free PCM and MP3; WAV/Opus and paid-model format behavior remain unverified. No format selection can turn best-effort free service timing into an audible-first-word SLA.
