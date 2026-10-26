# Bounded streaming text preparation

Status: current Unreleased implementation. Owners: [StreamingTextReader](../../STranslate.Plugin.Tts.FishAudio/Streaming/StreamingTextReader.cs) and the sender in [NativeStreamingTtsSession](../../STranslate.Plugin.Tts.FishAudio/Streaming/NativeStreamingTtsSession.cs).

The input string already exists at the host boundary. The streaming reader visits it lazily through an indexed source; it does not tokenize, normalize, scan or serialize the whole tail to prepare startup. The first window visits at most 100 Unicode scalars, and later windows visit at most 300. UTF-16 surrogate pairs remain intact. These are scalar limits, not UTF-16 code-unit counts or grapheme-cluster guarantees; combining sequences may span events. The provider receives original text and markup without client phoneme conversion or duplicated control tags.

Within one window, a safe sentence terminator has priority, followed by newline, whitespace, then the last safe boundary. If a chosen boundary is earlier than the scanned window end, the suffix is reread later; no text is lost or duplicated. An ordinary long word or sentence can be split at a scalar boundary. Thus a long unpunctuated tail does not force a complete-input startup scan.

## Safe flushes and markup limits

The reader carries state across windows for bracket directions `[ ... ]`, angle-delimited tokens, `<|phoneme_start|>...<|phoneme_end|>` and the paired tags `whisper`, `emphasis`, `soft`, `fast`, `slow`, `stress` and `forceful`. Recognized open spans, incomplete angle tokens and nested bracket directions delay a flush until closed. Speaker tags are atomic; unknown tokens, including experimental language tags, pass through unchanged and only their angle-delimited token is protected. Recognition uses at most 32 UTF-16 code units of token state; an oversized token remains unrecognized without a token-sized allocation.

This is a conservative boundary guard, not a complete provider markup parser or an escaping/nesting grammar. The provider does not publish such a complete grammar. An open recognized span can require multiple bounded text events before any safe flush. An incomplete or control-only input can end without a safe readable prefix. A readable prefix requires content outside the protected constructs that is neither whitespace, punctuation nor control. No invalid/ambiguous input is rewritten to manufacture a first-word claim.

## Send gate

The sender sends each text event in order and flushes at each safe readable boundary. After the first such flush, when input remains, it waits for successful local output `Play` before inspecting the next window. Receive, decode and the audio pump continue independently during that wait. This makes ordinary startup preparation independent of a huge unread tail. It does not promise a single 100-scalar first flush for an open atomic span.

Short complete input sends stop without waiting for playback. Final incomplete/control-only input also passes through and stops; subsequent failure or the first-PCM deadline handles absent playable audio. There is no documented flush-complete acknowledgement, so neither the sender nor output waits for one. Terminal failure/cancellation releases the playback-start wait through the session token.

Provider queueing, markup interpretation and generated initial silence can still delay audible speech. First prefix sent, first network audio, first PCM and output start are distinct. No fixed first-audible-word SLA follows from the local scalar windows.

Verification: [text suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/Streaming/StreamingTextSuite.cs) uses a guarded suffix, shared-prefix large tails, surrogate pairs, boundary priorities, long spans and incomplete/control-only inputs; [session suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/Streaming/StreamingSessionSuite.cs) checks playback gating and terminal paths. Rationale: [DD-051](../adr/current/DD-051.md).
