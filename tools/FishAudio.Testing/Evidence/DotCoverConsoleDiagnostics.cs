using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FishAudio.Testing.Evidence;

public sealed record ConsoleDiagnosticReview(int SchemaVersion, string RuleId,
    IReadOnlyList<DiagnosticArtifact> Evidence, string Scope)
{
    public bool BaselineClaim => false;
}

public sealed class DotCoverConsoleDiagnosticException : Exception
{
    internal DotCoverConsoleDiagnosticException(string code) : base(code) => Code = code;
    public string Code { get; }
}

/// <summary>
/// Reviews only the raw console protocol observed for the fixed Windows x64
/// runner. It supplies no process, test-success or complete-baseline claim.
/// The CP936 copyright dash is pinned as bytes; other encodings are unreviewed.
/// Caller-owned artifacts are read without rewriting or normalizing them.
/// </summary>
public static class DotCoverConsoleDiagnostics
{
    public const string Rule = "dotcover-2026.2.3.1-build-20260930.210750-windows-x64-console-cp936-v1";
    private const int MaximumBytes = 64 * 1024;
    private static readonly byte[] Banner =
    [
        .. Encoding.ASCII.GetBytes("JetBrains dotCover Console Runner 2026.2.3.1. Build 20260930.210750 (windows-x64)\r\nCopyright (c) 2009"),
        0xa8, 0x43,
        .. Encoding.ASCII.GetBytes("2026 JetBrains s.r.o. All rights reserved.\r\n"),
    ];
    private static readonly string[] CoverMessages =
    [
        "Coverage session started", "Coverage session finished",
        "Coverage results post-processing started", "Merging snapshots",
        "Snapshots merging finished", "Coverage results post-processing finished",
    ];
    private static readonly string[] ExportMessages =
    [
        "Report generation started.", "JSON report generated.", "XML report generated.",
    ];

    public static ConsoleDiagnosticReview ReadValidated(string coverStdout, string coverStderr,
        string exportStdout, string exportStderr, string dotCoverVersion)
    {
        var streams = new List<FileStream>();
        var succeeded = false;
        try
        {
            Require(dotCoverVersion == DotCoverAggregate.SupportedToolVersion, "console-toolchain-mismatch");
            var paths = new[] { coverStdout, coverStderr, exportStdout, exportStderr };
            Require(paths.All(Path.IsPathFullyQualified), "console-path-must-be-absolute");
            paths = paths.Select(Path.GetFullPath).ToArray();
            Require(paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4, "console-artifact-paths-must-differ");
            // Keep every file locked through the complete four-stream review.
            foreach (var path in paths) streams.Add(OpenRegular(path));
            var bytes = streams.Select(Read).ToArray();
            Require(bytes[1].Length == 0 && bytes[3].Length == 0, "console-unexpected-stderr");
            ValidateOutput(bytes[0], CoverMessages);
            ValidateOutput(bytes[2], ExportMessages);
            var evidence = paths.Select((path, index) => new DiagnosticArtifact(path,
                Convert.ToHexStringLower(SHA256.HashData(bytes[index])))).ToArray();
            succeeded = true;
            return new(1, Rule, Array.AsReadOnly(evidence),
                "console-only; exact fixed runner banner and ordered cover/export messages; no process, test-pass or baseline claim");
        }
        catch (DotCoverConsoleDiagnosticException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or NotSupportedException or OverflowException)
        {
            throw Rejected("console-invalid-input-or-file");
        }
        finally
        {
            var releaseFailed = false;
            foreach (var stream in streams)
            {
                try { stream.Dispose(); }
                catch { releaseFailed = true; }
            }
            if (succeeded && releaseFailed) throw Rejected("console-file-release-failed");
        }
    }

    private static FileStream OpenRegular(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        Require(file.Exists && (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0,
            "console-missing-or-nonregular-file");
        for (var directory = file.Directory; directory is not null; directory = directory.Parent)
        {
            directory.Refresh();
            Require(directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0,
                "console-reparse-directory");
        }
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Require(stream.Length <= MaximumBytes, "console-size-limit");
            return stream;
        }
        catch
        {
            try { stream.Dispose(); } catch { }
            throw;
        }
    }

    private static byte[] Read(FileStream stream)
    {
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        Require(stream.ReadByte() == -1, "console-file-size-changed");
        return bytes;
    }

    private static void ValidateOutput(byte[] bytes, string[] messages)
    {
        Require(bytes.AsSpan().StartsWith(Banner), "console-banner-or-encoding-mismatch");
        var body = bytes.AsSpan(Banner.Length);
        foreach (var value in body)
            Require(value is >= 0x20 and <= 0x7e or 0x0d or 0x0a, "console-nonascii-content");
        var lines = Encoding.ASCII.GetString(body).Split("\r\n", StringSplitOptions.None);
        Require(lines.Length == messages.Length + 1 && lines[^1].Length == 0, "console-incomplete-or-extra-content");
        for (var index = 0; index < messages.Length; index++)
        {
            var prefix = "[JetBrains dotCover] " + messages[index] + " [";
            var line = lines[index];
            Require(line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith(']'), "console-message-mismatch");
            var stamp = line[prefix.Length..^1];
            Require(DateTime.TryParseExact(stamp, "ddd MM dd yyyy hh:mm:ss tt", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed) &&
                parsed.ToString("ddd MM dd yyyy hh:mm:ss tt", CultureInfo.InvariantCulture) == stamp,
                "console-invalid-timestamp");
        }
    }

    private static DotCoverConsoleDiagnosticException Rejected(string code) => new(code);
    private static void Require([DoesNotReturnIf(false)] bool condition, string code)
    {
        if (!condition) throw Rejected(code);
    }
}
