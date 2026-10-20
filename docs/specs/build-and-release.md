# Build, package and release behavior

Status: current implementation. The plugin targets .NET 10 Windows/WPF, references STranslate.Plugin 1.0.15 and keeps package versions independent of a surrounding repository's centralized version policy. The verified compatibility baseline for the new SDK contract is STranslate v2.0.10; no unsupported minimum-host-version manifest field is invented.

Preview and formal streaming audio depend on NAudio.Core and NAudio.WinMM 2.3.0. Streaming MessagePack 3.1.12 also requires MessagePack.dll, MessagePack.Annotations.dll and Microsoft.NET.StringTools.dll. Package verification includes these private runtime assemblies alongside the plugin and five locale resources without bundling the host SDK/UI framework. Normal regression tests inject outputs and do not require a physical audio device.

[build.ps1](../../build.ps1) defaults to Debug and exposes only -Release as its configuration switch. -Test runs the executable regression project after building and checking the package. Debug packaging invokes the SDK target; Release uses automatic packaging. Output directories isolate Debug and Release, and the distributable is copied to the repository root.

An ordinary build cleans repository/plugin/test intermediates first and retains newly produced intermediates. -Clean defers that cleanup to finally on success or failure; it leaves an already copied root .spkg intact. -CleanOnly performs the same cleanup and exits. A retained package is not proof that a later build succeeded.

The standard combined verification command is `./build.ps1 -Release -Clean -Test`. The regression runner invokes suites explicitly from [Program.cs](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Program.cs). Normal tests are offline and require no Fish Audio credential or synthesis request.

[Validation](../../.github/workflows/validation.yml) runs on pull requests and pushes to main with read-only repository permissions, Windows, .NET 10 and the combined Release command. It does not publish. [Release](../../.github/workflows/dotnet.yml) runs on v* tag pushes, performs the same verification, extracts only the corresponding CHANGELOG version section and publishes the root package with the tag as release title.

Validation also runs the [API probe](../../tools/FishAudioProbe/README.md) offline self-tests after the package check. These exercise free-model and deadline guards, redaction and bounded evidence capture through local fixtures; CI does not read an API Key or contact Fish Audio.

New changes belong in Unreleased; published version sections retain historical net changes. Unreleased need not remain empty. Checks must cover the final intended file state after all code/documentation/changelog edits; see [verification policy](../policies/verification.md).

Verification: [build script suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/BuildScriptSuite.cs), [repository contract suite](../../tests/STranslate.Plugin.Tts.FishAudio.Tests/Suites/RepositoryContractSuite.cs). Rationale: [DD-023](../adr/current/DD-023.md), [DD-028](../adr/current/DD-028.md), [DD-042](../adr/current/DD-042.md), [DD-043](../adr/current/DD-043.md).
