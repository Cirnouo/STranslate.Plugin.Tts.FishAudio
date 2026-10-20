# Streaming verification policy

This policy applies to streaming changes. The [current TTS spec](../specs/tts-and-credit.md) describes native WebSocket playback; policy requirements are reusable verification rules, not claims that every platform/device scenario has been exercised.

## Protocol and ownership

Test transport fragmentation independently from application messages and codec/PCM boundaries. Exercise the selected protocol (currently MessagePack, not SSE): empty events, malformed/truncated payloads, oversize messages, unexpected event order, remote errors, normal finish and close without finish. Preserve one ordered audio stream; do not turn an error into an implicit second synthesis.

Define one session owner for send, receive, cancellation, device errors and terminal state. Verify cancellation while waiting for input, queue capacity, initial playback, output drain and close. Completion must distinguish normal provider finish, audio queue completion and device drain. Dispose is idempotent and late callbacks cannot touch replaced state.

## Audio correctness and resource bounds

Compare whole-input and arbitrarily split fixture output. For PCM, preserve complete sample frames across messages, reject a truncated final frame and validate rate/channels/bit depth/encoding/byte order. For codecs, test headers, partial frames, terminal metadata, corrupt frames and incompatible format changes. Do not add zero padding or drop bytes merely to align transport messages.

Bound message aggregation, owned payloads, queues, decoder residuals and output buffering. A fixed output buffer alone does not bound the pipeline. Include a single input message larger than available output capacity; writes must be sliced without partial-write duplication. Track maximum buffered bytes and verify order with a deterministic byte/sample pattern.

Test start thresholds using audio duration and an injected wall clock separately. Include a valid short prefix followed by a silent upstream, so start does not depend on an unavailable flush-complete event or the final session finish. A sender waiting for playback start must be released or failed deterministically on every terminal path.

Test underflow and later recovery, slow consumers, no-progress output, asynchronous device failure, cleanup failure and resampler/codec tails. Output start and first PCM are software milestones; they do not establish physical sound.

Install the clock/output pump before a synchronously available producer can monopolize decoding. Test both 250 ms of buffered audio and the 100 ms wall-clock threshold from first PCM with input blocked. A timeout checked only on the next input event is not independent startup. Exercise device position wrap and final real-sample position after underflow silence. Attempt cleanup independently for every resource, preserving the original failure; also test cleanup as the only failure and input that ignores cancellation.

## Bounded text preparation

Use a deterministic corpus with a shared prefix and increasingly long tails. A guarded cursor or poison suffix must prove that startup does not enumerate, normalize or serialize the full tail. Cover Unicode scalars, grapheme clusters, long words, markup delimiters and incomplete/oversized atomic spans. Flush only at boundaries that are safe for the selected protocol and markup grammar. Do not duplicate or discard text to manufacture a better first-audio measurement.

## Backpressure and liveness

Verify full queues followed by finish/error/close, cancellation during pressure, sender failure while receive waits and device failure while the producer waits. A receiver blocked on a bounded queue cannot simultaneously read later control messages from that same ordered connection. Document the acceptable delay and use independent cancellation/no-progress handling.

If active WebSocket PING/PONG timeouts are enabled, verify that required pending reads are maintained or that local pressure cannot cause a false timeout. Do not solve this by issuing concurrent receives or removing memory bounds. Keep server idle, local backpressure and output no-progress timers semantically separate.

Account overlapping encoded/PCM pressure as a union, not a sum or an entire buffer-occupied interval. Test first PCM separately from first encoded bytes. Freeze request values before await and prove that capture of the last published initialization remains available while replacement host load is blocked. Test successor cleanup ordering across at least three calls and all caller/supersession/retirement/disposal cancellation paths.

## Evidence and compatibility

Run core checks with fake transports and outputs. Use local fixtures for decoder/device checks and identify the runtime, device and output mode. Test coexistence with other application audio, no device, format rejection, device removal and cancellation isolation. Test package contents and dependencies on the supported host baseline.

Use [API evidence policy](api-evidence.md) for any explicitly authorized live run. An offline test proves the exercised client contract; it cannot prove provider first-audio latency, model compatibility or zero billing. Report measured boundaries and failed/untested cases honestly.
