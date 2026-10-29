# Framework evidence and real-project workspace probe

The normal framework entry is `tools/Test.ps1`; the [test-system specification](../../docs/specs/test-system.md)
owns discovery, full/focused execution, canonical identity and raw evidence. The
metadata admission and bounded workspace-probe commands described below do not
execute scenarios. This CLI also supplies the internal `framework-run` process
supervisor used by that driver; NUnit retains discovery, fixtures, assertions and
results.

All evidence command paths are absolute. Each command creates a fresh JSON output
and rejects an existing destination:

```text
dotnet <built FishAudio.Testing.dll> evidence-catalog --discovery <NUnit D_ dump> --output <new JSON>
dotnet <built FishAudio.Testing.dll> evidence-verify --catalog <catalog JSON> --selection <selection JSON> --result <NUnit XML> --trx <TRX> --output <new JSON>
dotnet <built FishAudio.Testing.dll> evidence-binary --assembly <DLL> --pdb <portable PDB> --output <new JSON>
```

Catalog admission requires runnable, uniquely identified leaf cases. Result
admission requires the exact selected leaf set to pass in both framework formats;
it does not infer pass from a process exit or a console count. Binary admission
reads only the two explicit files, matching the portable CodeView GUID, stamp and
age to the PDB before reporting MVID and content hashes. It never searches for
another symbol file. These records establish their stated metadata contracts,
separately from coverage collection and complete-baseline publication.

## Workspace capability probe

This command probes two semantic bindings in the actual regression project and
its product project reference. It does not run tests, construct a settings view,
select affected cases, establish a baseline or claim a complete test-impact graph.

Use the built CLI with an absolute path to the repository's
`tests/STranslate.Plugin.Tts.FishAudio.Tests/STranslate.Plugin.Tts.FishAudio.Tests.csproj`
and a new absolute JSON output path:

```text
dotnet <built FishAudio.Testing.dll> workspace-probe --project <absolute tests csproj> --output <absolute JSON> [--coverage]
```

The selected .NET SDK must satisfy the actual project's `global.json`; package
restore and Windows/WPF targeting support must already be available. Workspace
loading performs a design-time build and may execute project targets and source
generators or write intermediates. Use a disposable source snapshot when those
side effects must be isolated. The probe itself does not restore dependencies or
invoke a normal build. The default configuration is Release, AnyCPU and
net10.0-windows; `--coverage` additionally sets `FishAudioCoverage=true`.

The JSON reports the selected SDK and MSBuild file version, loaded Roslyn and
Locator versions, evaluated SDK/configuration metadata from the test compilation,
source project references, documents, syntax trees, generator documents, analyzers,
workspace diagnostics and compilation errors. Diagnostic messages and exception
messages are omitted; locations within the repository are relative, and external
paths retain only a safe filename. Requested properties and observed identities
are separate. The private workspace BuildHost's exact MSBuild version is not
exposed by this probe's public API and is explicitly reported as unavailable.

The required checks resolve `CompatibilityCases.SynchronousProductCall`'s product
call to the source declaration of `SettingsValidation.IsValidApiKeyFormat`, and
`SettingsView`'s constructor call to the generated partial source declaration of
`InitializeComponent`. Missing projects, unresolved checks, workspace failures,
compilation errors or exceptions produce exit code 1. Invalid arguments and output
write failures produce exit code 2. A completed passing probe returns 0. Existing
output files are never replaced. WPF markup failures can leave a loadable project,
so project loading alone cannot pass the probe. Other configuration branches,
XAML runtime bindings, execution coverage and complete dependency closure remain
outside its proof.

The pinned packages are [CSharp.Workspaces 5.9.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp.Workspaces/5.9.0),
[Workspaces.MSBuild 5.9.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/5.9.0)
and [Microsoft.Build.Locator 1.11.2](https://www.nuget.org/packages/Microsoft.Build.Locator/1.11.2).
Registration precedes workspace code in a separate non-inlined method, following
[Microsoft's registration rule](https://learn.microsoft.com/en-us/visualstudio/msbuild/find-and-use-msbuild-versions#register-instance-before-calling-msbuild).
Preserve the package's default BuildHost deployment; the Roslyn package owns its
[isolated host and support files](https://github.com/dotnet/roslyn/blob/35d9211b841e7613c1d2f8f5af6d628ace696c4c/src/Workspaces/MSBuild/Core/Microsoft.CodeAnalysis.Workspaces.MSBuild.csproj#L85-L124).
The probe does not copy or replace those files.
