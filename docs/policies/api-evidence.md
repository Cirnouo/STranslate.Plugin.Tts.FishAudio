# API evidence and zero-cost testing policy

Provider documentation, repository implementation, offline tests and live observations are different evidence classes. State which class supports each assertion. Prefer official versioned documentation, published package metadata and immutable source revisions. Record the retrieval time and source version for facts that can change.

A useful live evidence record identifies the endpoint/protocol, non-secret request parameters, expected model, timestamp, tool/runtime version, response status/shape, size and relevant measured milestones. Record failed attempts and their limitations. Preserve enough sanitized input/output to reproduce the conclusion without storing a credential or granting another maintainer permission to repeat a billable request.

## Default and strict free execution

Normal regression tests and CI are offline. Fake HTTP/WebSocket services, deterministic clocks and local audio fixtures validate request contracts, framing, cancellation, buffering and device-independent failures without synthesis calls.

For a strictly zero-cost task, do not perform real synthesis unless current provider evidence establishes the exact permitted free operation and the requested run explicitly authorizes it. If eligibility cannot be established, stop that live test and record the limitation; offline verification can continue. A model name containing `free`, a local promotional cutoff, missing `reference_id`, a successful response or an unchanged balance is insufficient on its own.

When a real free-only synthesis run is explicitly authorized:

- Require the exact permitted model and current free-eligibility evidence before connecting or sending text. Check all model-bearing headers/payload fields against that requirement.
- Do not silently substitute a paid model, format, endpoint or second synthesis after a rejection, timeout, disconnect, partial audio or playback failure. Fail the run and preserve sanitized evidence.
- Bound text, attempts and session lifetime. Cancellation and early connection closure do not prove that provider billing was prevented.
- Keep credentials in a scoped runtime input, not source, command arguments, filenames or committed settings. Restore the test environment after the run.
- Separate public metadata/sample GET checks from credentialed wallet checks and synthesis. A successful public sample download proves reachability and bytes, not TTS generation or physical playback.

## Redaction and publication

Remove Authorization values, API Keys, cookies, proxy credentials, tokens and full signed query strings before writing logs or artifacts. A stable redaction placeholder is enough; do not preserve secret prefixes or suffixes to make a report look precise. Retain non-secret host names, field names, value types, status codes, durations, byte counts and sanitized hashes where they support the claim.

Use synthetic placeholders in examples. Redact exception text as well as structured request logs. Treat text sent for synthesis as potentially private; use a fixed neutral test corpus rather than user content in shareable evidence. Check generated artifacts before committing them.

Keep byte-hashed captures outside Git text normalization (`docs/evidences/** -text` in `.gitattributes`). JSON is still a byte artifact once its stored hash is recorded. Verify hashes from a fresh checkout with automatic newline conversion enabled, rather than checking only the original capture directory; LF/CRLF conversion must not invalidate another maintainer's evidence.

Measurements must name their boundary: headers, first transport message, first audio payload, first decodable frame, first PCM, output start, normal provider finish or drain. Network/audio-payload success must not be reported as audible success. Physical sound claims require a stated device or loopback measurement method and its limitations. See [the glossary](../glossary.md) and [streaming verification](streaming-validation.md).
