using FishAudioProbe;

if (args.Contains("--self-test", StringComparer.Ordinal)) return SelfTests.Run();
if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
{
    Console.WriteLine("FishAudioProbe --output PATH [--group public|authenticated|http-tts|ws-tts|all] [--case ID ...]");
    Console.WriteLine("Default: public (catalog anonymous, public voices use Bearer dummy). Credentials: FISH_API_KEY environment only.");
    Console.WriteLine("--self-test: offline safety/capture tests. No key is read; no network is used.");
    return args.Length == 0 ? 2 : 0;
}
var redactor = new Redactor("");
try
{
    var options = CliOptions.Parse(args);
    var key = options.Group == "public" ? null : Environment.GetEnvironmentVariable("FISH_API_KEY");
    redactor = new Redactor(key ?? "");
    if (options.Group != "public" && string.IsNullOrWhiteSpace(key))
        throw new InvalidOperationException("This group requires FISH_API_KEY. Nothing was sent.");
    if (options.Group is "all" or "http-tts" or "ws-tts")
        ProbePolicy.EnsureSynthesisAllowed(ProbePolicy.FreeModel, DateTimeOffset.UtcNow);
    var store = new EvidenceStore(options.Output, options.Group, redactor);
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
    Console.CancelKeyPress += cancel;
    try { await new ProbeRunner(options, store, redactor, key).RunAsync(cancellation.Token); }
    finally { Console.CancelKeyPress -= cancel; store.Flush(); }
    Console.WriteLine($"Run manifest: {Path.Combine(store.RunDirectory, "manifest.json")}");
    Console.WriteLine("Execution finished. Inspect every case outcome; exit 0 does not turn failed/skipped cases into successful evidence.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(redactor.Clean($"Probe stopped: {ex.GetType().Name}: {ex.Message}"));
    return 1;
}
