# Reproduce production streaming validation

`Program.cs` and `StreamingValidation.csproj` are the source used for the recorded production-DLL harness. The project filename is generic; source/project bytes are otherwise preserved. It references the built plugin, with no copied implementation stubs or project reference that rebuilds plugin code.

Use Windows and the .NET 10 SDK. First build the repository tests in Release to obtain both the production DLL and the complete test runtime, including `STranslate.Plugin.dll` from the host SDK. From the repository root:

```powershell
$repoRoot = (Get-Location).Path
dotnet build tests/STranslate.Plugin.Tts.FishAudio.Tests/STranslate.Plugin.Tts.FishAudio.Tests.csproj -c Release --nologo
$pluginDir = Join-Path $repoRoot '.artifacts/Release'
$dependencyDir = Join-Path $repoRoot 'tests/STranslate.Plugin.Tts.FishAudio.Tests/bin/Release/net10.0-windows'
dotnet build docs/evidences/streaming-2026-10-20/harness/StreamingValidation.csproj -c Release `
  "-p:ProductionPluginDir=$pluginDir" "-p:ProductionDependencyDir=$dependencyDir" --nologo
```

Use an ordinary test-project build rather than a clean script while relying on existing artifact directories. If you copy the outputs to an isolated directory, both properties may point to that copy as long as it contains the current production DLL and full dependencies. Rebuild the harness when changing the DLL to ensure the copy is current, and compare the request summary's DLL hash with the artifact being tested.

The executable deliberately uses the existing friend name `STranslate.Plugin.Tts.FishAudio.Tests` to access internal production types. Keep this executable and its copied dependencies isolated. **Do not install the harness or its friend assembly in STranslate's plugin directory.** The harness project and [offline benchmark](../prefix-benchmark/README.md) are sibling directories with separate build outputs, so their `Program.cs` files do not enter each other's compilation.

Runtime has exactly three positional arguments: `pcm|mp3`, `short|long|cancel`, and a new output directory. A valid invocation performs **one real production WebSocket session** and uses the real Windows WaveOut device. Supply `FISH_API_KEY` through the current process environment using local secret handling; never put a key in CLI arguments, a repository file, or recorded terminal output. The harness does not read host settings or persisted environment scopes.

For an authorized run:

```powershell
$harnessExe = Join-Path $repoRoot 'docs/evidences/streaming-2026-10-20/harness/bin/Release/net10.0-windows/STranslate.Plugin.Tts.FishAudio.Tests.exe'
$newOutput = Join-Path ([System.IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString('N'))
& $harnessExe pcm long $newOutput
```

Choose MP3 or cancellation by changing the first two arguments; each invocation needs a fresh output directory. The input is fixed neutral text, under 300 characters. Long and cancel use 215 characters and a withheld tail. The cancel case cancels only after the actual production playback callback. No retry, HTTP fallback, or alternate model is available. The hard guard requires exactly `s2.1-pro-free` and UTC strictly before `2026-12-01T00:00:00Z` before connection and every send. The linked deadline is the earlier of 120 seconds or that cutoff. The recorded cases occurred before this cutoff; after cutoff the harness fails closed and must not be repurposed to a paid model implicitly.

The source records actual WaveOut initialization, successful `Play` return and device position, then the production player's callback. It snapshots source high-water mark and sent-text count at callback entry before the native callback releases the tail sender. Native stage observations are delivered after session completion and keep their native timestamps; timeline entries use the harness's separate stopwatch. See the [evidence interpretation](../README.md) before comparing clocks or claiming drain/audibility.

Artifacts contain safe request metadata, timeline, all eight stage slots, complete raw incoming/outgoing MessagePack, and concatenated raw audio. A final incomplete incoming message, if any, is explicitly named `-incomplete.msgpack`. The binary/audio budget is 8 MiB; exceeding it aborts rather than silently truncating evidence. Each file write scans exact UTF-8/UTF-16 key bytes. Untrusted URL content or a key match causes whole-artifact redaction with an explicit flag; exception messages, stack traces, and arbitrary server reason strings never enter JSON diagnostics. Authorization is fully redacted in request summaries. Exit `0` requires the expected case outcome, evidence assertions, and no whole-artifact redaction; intentional cancellation can therefore exit `0`.

The original harness build used `System.IO` explicitly, completed with zero warnings/errors, and passed four invalid-argument checks that return before environment access. Separate offline safety checks used a synthetic credential to verify exact-key redaction in UTF-8/UTF-16LE/UTF-16BE, URL redaction, rejection of endpoint query strings, rejection of paid/case-variant model IDs, and rejection of an expired deadline. Those synthetic checks are not real API sessions and are not included in the live case index.
