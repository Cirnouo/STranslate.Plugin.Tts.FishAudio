namespace FishAudioProbe;

internal static class ProbePolicy
{
    internal const string FreeModel = "s2.1-pro-free";
    internal static readonly DateTimeOffset CutoffUtc = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
    internal const int MaximumBytes = 8 * 1024 * 1024;
    internal static readonly TimeSpan CaseTimeout = TimeSpan.FromSeconds(120);

    internal static void EnsureSynthesisAllowed(string model, DateTimeOffset now)
    {
        if (!string.Equals(model, FreeModel, StringComparison.Ordinal) || now.ToUniversalTime() >= CutoffUtc)
            throw new InvalidOperationException("Synthesis refused: only the exact free model before the UTC cutoff is permitted.");
    }

    internal static void EnsureApiUri(Uri uri)
    {
        if (uri.Host != "api.fish.audio" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || uri.Scheme is not ("https" or "wss"))
            throw new InvalidOperationException("Unexpected API destination refused.");
    }

    internal static bool IsAllowedAsset(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort
        && uri.UserInfo.Length == 0
        && (uri.Host.EndsWith(".fish.audio", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase));
}
