using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

internal sealed record RegressionFailureFrame(string? SourceFile, int Line, string Type, string Method);
internal sealed record RegressionFailureInfo(IReadOnlyList<RegressionFailureFrame> Frames, string? NativeKeyboardContext);
internal sealed record RegressionFailureSidecar(int Version, string RequestId, string RunId, string CaseId,
    RegressionSourceIdentity Source, RegressionFailureInfo Failure);

// Failure-only diagnostics. Never format exceptions, parse remote stack text or
// serialize arbitrary Data; these records are not authoritative test outcomes.
internal static class RegressionFailureEvidence
{
    private static readonly int[] ControlledKeys = [0x01, 0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5];
    private static readonly Regex ContextPattern = new(
        @"\Aphase=(before-action|after-action|restore); controlledChangedCount=([0-9]{1,2}); controlledChanges=\[(.*)\]; spaceChanged=(True|False); otherChangedCount=([0-9]{1,3})\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));
    private static readonly Regex ChangePattern = new(
        @"\A0x([0-9A-F]{2}):0x([0-9A-F]{2})>0x([0-9A-F]{2})\(down=(False|True)>(False|True),low=0x([0-9A-F]{2})>0x([0-9A-F]{2})\)\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100));

    internal static RegressionFailureInfo Capture(Exception error)
    {
        var frames = new List<RegressionFailureFrame>(12);
        try
        {
            foreach (var frame in new StackTrace(error, fNeedFileInfo: true).GetFrames() ?? [])
            {
                var method = frame.GetMethod();
                if (method?.DeclaringType is not { } type || type.Assembly != typeof(RegressionFailureEvidence).Assembly) continue;
                var typeName = type.FullName;
                if (!SafeName(typeName) || !SafeName(method.Name)) continue;
                var filename = frame.GetFileName() is { } path ? Path.GetFileName(path) : null;
                if (filename is not null && (filename.Length > 128 || filename.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')))) filename = null;
                frames.Add(new(filename, filename is null ? 0 : Math.Max(0, frame.GetFileLineNumber()), typeName!, method.Name));
                if (frames.Count == 12) break;
            }
        }
        catch { /* Missing symbols or extensible exception behavior remain partial evidence. */ }
        string? context = null;
        try
        {
            if (error.Data[NativeKeyboardStateFixture.FailureContextKey] is string value && ValidContext(value)) context = value;
        }
        catch { /* Diagnostic access never replaces the original exception. */ }
        return new(frames, context);
    }

    internal static string SidecarPath(RegressionChildRequest request, int index) => request.ManifestPath + $".failure-{index + 1:D6}.json";

    internal static void TryWrite(RegressionChildRequest request, int index, Exception error)
    {
        try
        {
            RegressionProcessProtocol.WriteNew(SidecarPath(request, index), new RegressionFailureSidecar(
                1, request.RequestId, request.RunId, request.CaseIds[index], request.Source, Capture(error)));
        }
        catch { /* Best-effort evidence cannot replace or recover a failed case. */ }
    }

    private static bool SafeName(string? value) => value is { Length: > 0 and <= 512 } && !value.Any(char.IsControl);

    private static bool ValidContext(string value)
    {
        if (value.Length > NativeKeyboardStateFixture.MaximumFailureContextLength) return false;
        var match = ContextPattern.Match(value);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count > 10 ||
            !int.TryParse(match.Groups[5].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var other) || other > 245) return false;
        var changes = match.Groups[3].Value.Length == 0 ? [] : match.Groups[3].Value.Split(',');
        // Commas inside each entry separate down and low fields, so rejoin pairs.
        if (changes.Length != count * 2) return false;
        var previous = -1;
        for (var index = 0; index < changes.Length; index += 2)
        {
            var change = ChangePattern.Match(changes[index] + "," + changes[index + 1]);
            if (!change.Success) return false;
            var key = int.Parse(change.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var ordinal = Array.IndexOf(ControlledKeys, key);
            if (ordinal <= previous) return false;
            previous = ordinal;
            var expected = int.Parse(change.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var actual = int.Parse(change.Groups[3].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (expected == actual || change.Groups[4].Value != ((expected & 0x80) != 0).ToString() ||
                change.Groups[5].Value != ((actual & 0x80) != 0).ToString() ||
                int.Parse(change.Groups[6].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) != (expected & 0x7F) ||
                int.Parse(change.Groups[7].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) != (actual & 0x7F)) return false;
        }
        return true;
    }
}
