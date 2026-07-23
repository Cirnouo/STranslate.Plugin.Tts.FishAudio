namespace STranslate.Plugin.Tts.FishAudio.Configuration;

internal static class FishAudioModelPolicy
{
    internal const string S21ProFreeModel = "s2.1-pro-free";
    internal const string S21ProModel = "s2.1-pro";
    internal const string S2ProModel = "s2-pro";
    internal const string S1Model = "s1";

    internal static readonly DateTimeOffset FreeModelCutoffUtc = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    internal static IReadOnlyList<string> GetAvailableModels(DateTimeOffset nowUtc) =>
        IsS21ProFreeAvailable(nowUtc)
            ? [S21ProFreeModel, S21ProModel, S2ProModel, S1Model]
            : [S21ProModel, S2ProModel, S1Model];

    internal static bool IsS21ProFreeAvailable(DateTimeOffset nowUtc) =>
        nowUtc.ToUniversalTime() < FreeModelCutoffUtc;

    internal static string GetDefaultModel(DateTimeOffset nowUtc) =>
        IsS21ProFreeAvailable(nowUtc) ? S21ProFreeModel : S21ProModel;

    internal static bool SupportsNormalizeLoudness(string model) =>
        string.Equals(model, S21ProFreeModel, StringComparison.Ordinal)
        || string.Equals(model, S21ProModel, StringComparison.Ordinal)
        || string.Equals(model, S2ProModel, StringComparison.Ordinal);
}
