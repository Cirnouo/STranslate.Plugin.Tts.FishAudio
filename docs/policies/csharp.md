# C# maintenance policy

This policy applies to production code and regression tests. Preserve observable contracts unless a change explicitly replaces them and updates its specification and ADR.

- Follow the surrounding C# style: file-scoped namespaces, nullable references, descriptive PascalCase public members, camelCase locals and `_camelCase` private fields. Use `var` where the type is clear and explicit types where they clarify an API boundary.
- Keep units visible in names or types. Use `TimeSpan` for durations, monotonic elapsed time for latency, and `DateTimeOffset` normalized to UTC for calendar policy and signature expiry. Do not compare local calendar dates to UTC cutoffs.
- Use [the glossary](../glossary.md) for Voice, TTS model, transport message, audio chunk and PCM frame. Preserve provider DTO field names and established public namespaces/bindings for compatibility.
- Keep `Main` as composition root and public plugin entry. Put request construction in FishAudio, settings interpretation/storage in Configuration, initialization ownership in Lifecycle, and WPF interaction in Presentation. Add an interface where it defines ownership or permits a meaningful failure test, not for every class.
- Route production settings I/O through `SettingsStore`. New fields must be included in conversion, migration, normalization, cloning/copying, ViewModel initialization, auto-save and round-trip tests. Do not introduce raw host-settings file access.
- Keep durable user intent separate from disposable metadata/media caches. Auxiliary plugin data needs bounded reads, atomic writes and the same active-generation authorization as settings. Bookmark mutations must not insert retained display snapshots into a remote-data cache or renew their freshness.
- Separate accepted authorization from the lifetime of displayed information. A credential edit can retire pending work while retaining existing cards. Retained display metadata must not acquire a new credential scope or remote-cache freshness merely because a key changes; subsequent requests and queued publication still check their captured owners.
- Capture the dependencies and settings needed by an operation at a defined boundary. Preserve write-lease and stale-generation checks when changing asynchronous workflows.
- Keep credential-bearing snapshots private and avoid record-generated value printing. Never log raw transport exceptions when they can embed API Keys, proxy credentials/addresses, signed URLs or user input; use fixed messages and safe type/stage fields.
- Pass cancellation tokens through network calls, reads, waits and playback. Distinguish caller cancellation from timeouts and device/transport failures; do not turn an intentional cancellation into a generic user error.
- Own and dispose each stream, timer, cancellation source, decoder and device explicitly. Cover failure during construction, operation and cleanup. Preserve the original failure when cleanup also fails.
- Do not invoke observable setters, UI dispatch, host I/O, cancellation callbacks or arbitrary delegates while holding a state lock. Document acquisition order when more than one gate is required.
- Keep WPF callbacks on their owning dispatcher. Apply `ConfigureAwait(false)` deliberately to non-UI work; do not add it mechanically across presentation code.
- Where the contract requires immediate input persistence, explicitly set binding direction and update trigger and test the compiled control before focus changes. SDK dependency-property defaults can otherwise silently delay updates.
- Share ID-based favorite/selection state across cards while retaining originating card identity for asynchronous work. An equal ID does not authorize a removed object to publish or execute against its replacement. Publish visible state only after persistence succeeds.
- Dispatch a versioned activity publisher as a unit rather than queueing a captured lock/unlock value. Re-read current desired state when queued work runs. Verify background completion and retirement with a real WPF command listener; property-only test doubles can miss dispatcher ownership failures.
- Treat external bytes and JSON as untrusted input. Validate shape and size before expensive processing, preserve protocol order, and fail malformed/truncated data with a diagnostic that does not expose secrets.
- Persistent UI text uses XAML `DynamicResource`. Transient errors may use translated strings at action time. Update all seven supported UI locales (en, zh-cn, zh-tw, ja, ko, ru and tr) when changing visible behavior.
- Prefer behavior tests with fake transports, clocks, storage and audio devices. Avoid tests that duplicate implementation statements or grep source layout unless a real packaging or repository contract requires it.

See [verification](verification.md), [API evidence](api-evidence.md) and the [architecture specification](../specs/architecture-and-lifecycle.md).

Numeric display precision, pointer interaction steps and provider validity are separate contracts. Keep edit drafts separate from persisted values, and verify that a valid off-tick value survives the complete UI/save/reload path. Do not silently quantize precise input merely because a slider uses coarser ticks.

Validate the complete proposed input draft, including selection replacement, before accepting composition or clipboard paste. Do not rely on MaxLength to truncate malformed clipboard data into a different valid value. Retain independent commit/domain validation, and keep lexical editing constraints separate from final numeric bounds or complete identifier requirements.
