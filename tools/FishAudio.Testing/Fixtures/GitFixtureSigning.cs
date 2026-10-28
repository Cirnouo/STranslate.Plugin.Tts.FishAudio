#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace FishAudio.Testing.Fixtures;

// This source is BCL-only so test projects can link it and PowerShell can load
// it with Add-Type. The caller owns processes, deadlines and private-root cleanup.
public sealed class GitFixtureSigning
{
    private const string PrivateKeyName = "fixture-ed25519";
    private const string AllowedSignersName = "allowed_signers";
    private readonly byte[] _privateKeyHash;
    private readonly byte[] _publicKeyBytes;
    private readonly byte[] _allowedSignersBytes;

    private GitFixtureSigning(string root, string keygen, string identity, string publicKey,
        byte[] privateKeyHash, byte[] publicKeyBytes, byte[] allowedSignersBytes)
    {
        RootDirectory = root;
        SshKeygenPath = keygen;
        UserIdentity = identity;
        PrivateKeyPath = Path.Combine(root, PrivateKeyName);
        PublicKeyPath = PrivateKeyPath + ".pub";
        AllowedSignersPath = Path.Combine(root, AllowedSignersName);
        SshPublicKey = publicKey;
        _privateKeyHash = privateKeyHash;
        _publicKeyBytes = publicKeyBytes;
        _allowedSignersBytes = allowedSignersBytes;
    }

    public string RootDirectory { get; }
    public string SshKeygenPath { get; }
    public string UserIdentity { get; }
    public string PrivateKeyPath { get; }
    public string PublicKeyPath { get; }
    public string AllowedSignersPath { get; }
    public string SshPublicKey { get; }

    public static string ResolveGitExecutable()
        => Io(() => ResolveExecutable("FISHAUDIO_TEST_GIT_EXECUTABLE", "git.exe"), "executable-resolution-io");

    public static string ResolveSshKeygenExecutable(string gitFullPath)
        => Io(() =>
        {
            var git = ExecutablePath(gitFullPath, "git.exe");
            var configured = Environment.GetEnvironmentVariable("FISHAUDIO_TEST_SSH_KEYGEN_EXECUTABLE");
            if (configured is not null) return ExecutablePath(configured, "ssh-keygen.exe");

            var directory = new DirectoryInfo(Path.GetDirectoryName(git)!);
            var parentDirectory = directory.Parent;
            if (directory.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) && parentDirectory is not null &&
                parentDirectory.Name.Equals("usr", StringComparison.OrdinalIgnoreCase))
            {
                var sibling = Path.Combine(directory.FullName, "ssh-keygen.exe");
                if (File.Exists(sibling) || Directory.Exists(sibling)) return ExecutablePath(sibling, "ssh-keygen.exe");
            }
            DirectoryInfo? installation = null;
            if (directory.Name.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
                directory.Name.Equals("bin", StringComparison.OrdinalIgnoreCase))
            {
                installation = parentDirectory;
                if (installation is not null &&
                    (installation.Name.Equals("mingw64", StringComparison.OrdinalIgnoreCase) ||
                     installation.Name.Equals("mingw32", StringComparison.OrdinalIgnoreCase)))
                    installation = installation.Parent;
            }
            if (installation is not null)
            {
                var bundled = Path.Combine(installation.FullName, "usr", "bin", "ssh-keygen.exe");
                if (File.Exists(bundled) || Directory.Exists(bundled)) return ExecutablePath(bundled, "ssh-keygen.exe");
            }
            return ResolveExecutable("FISHAUDIO_TEST_SSH_KEYGEN_EXECUTABLE", "ssh-keygen.exe");
        }, "executable-resolution-io");

    // root must be a fresh, caller-owned short Temp directory outside repository
    // sources and captured evidence. invoke must fail on nonzero process exit.
    public static GitFixtureSigning Generate(string root, string sshKeygenFullPath,
        string userIdentityFixed, Action<string, string[]> invoke)
    {
        if (invoke is null) throw Failure("invoke-missing");
        Require(IsPrincipal(userIdentityFixed), "principal-invalid");
        var paths = Io(() =>
        {
            var directory = AbsolutePath(root);
            var temporaryRoot = AbsolutePath(Path.GetTempPath());
            Require(string.Equals(Path.GetDirectoryName(directory), temporaryRoot,
                StringComparison.OrdinalIgnoreCase), "root-not-owned-temp-child");
            Require(Path.Combine(directory, PrivateKeyName + ".pub").Length < 260, "root-too-long");
            CheckDirectoryChain(temporaryRoot);
            Require(!Directory.Exists(directory) && !File.Exists(directory), "root-already-exists");

            var keygen = AbsolutePath(sshKeygenFullPath);
            Require(string.Equals(Path.GetFileName(keygen), "ssh-keygen.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(keygen), "ssh-keygen", StringComparison.Ordinal), "keygen-name-invalid");
            CheckRegularFile(keygen);
            Directory.CreateDirectory(directory);
            CheckDirectoryChain(directory);
            Require(Directory.GetFileSystemEntries(directory).Length == 0, "root-not-empty");
            return new[] { directory, keygen };
        }, "preparation-io");

        // Deliberately outside our IO exception translation: the caller retains
        // the original process/ownership exception and its existing deadline.
        invoke(paths[1], new[] { "-q", "-t", "ed25519", "-N", "", "-C", userIdentityFixed,
            "-f", Path.Combine(paths[0], PrivateKeyName) });

        return Io(() =>
        {
            CheckRootEntries(paths[0], false);
            var privateKeyHash = HashPrivateKey(Path.Combine(paths[0], PrivateKeyName));
            var publicKeyBytes = ReadBounded(Path.Combine(paths[0], PrivateKeyName + ".pub"), 4096);
            var publicKey = ParsePublicKey(publicKeyBytes, userIdentityFixed);
            var allowedSignersBytes = new UTF8Encoding(false, true).GetBytes(userIdentityFixed + " " + publicKey + "\n");
            var allowedSignersPath = Path.Combine(paths[0], AllowedSignersName);
            using (var stream = new FileStream(allowedSignersPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(allowedSignersBytes, 0, allowedSignersBytes.Length);
                stream.Flush(true);
            }
            var signing = new GitFixtureSigning(paths[0], paths[1], userIdentityFixed, publicKey,
                privateKeyHash, publicKeyBytes, allowedSignersBytes);
            signing.CheckMaterials();
            return signing;
        }, "material-io");
    }

    // Only signing configuration belongs here. The caller supplies -C and must
    // request -S on its ordinary commit or commit-tree command.
    public string[] ConfigurationArguments()
    {
        Io(() => { CheckMaterials(); return true; }, "material-io");
        return new[]
        {
            "-c", "gpg.format=ssh",
            "-c", "user.signingkey=" + PrivateKeyPath,
            "-c", "gpg.ssh.program=" + SshKeygenPath,
            "-c", "gpg.ssh.allowedSignersFile=" + AllowedSignersPath,
            "-c", "commit.gpgsign=true"
        };
    }

    // Includes the same signing configuration and verify-commit for the exact
    // object ID. Repository selection remains exclusively with the caller.
    public string[] VerifyCommitArguments(string objectId)
    {
        if (objectId is null || (objectId.Length != 40 && objectId.Length != 64)) throw Failure("object-id-invalid");
        foreach (var character in objectId)
            Require(IsAsciiHex(character), "object-id-invalid");
        var arguments = new List<string>(ConfigurationArguments()) { "verify-commit", objectId };
        return arguments.ToArray();
    }

    private void CheckMaterials()
    {
        CheckRootEntries(RootDirectory, true);
        CheckRegularFile(SshKeygenPath);
        Require(EqualBytes(HashPrivateKey(PrivateKeyPath), _privateKeyHash), "private-key-changed");
        var publicKeyBytes = ReadBounded(PublicKeyPath, 4096);
        Require(EqualBytes(publicKeyBytes, _publicKeyBytes) &&
            ParsePublicKey(publicKeyBytes, UserIdentity) == SshPublicKey, "public-key-changed");
        Require(EqualBytes(ReadBounded(AllowedSignersPath, 8192), _allowedSignersBytes), "allowed-signers-changed");
    }

    private static void CheckRootEntries(string root, bool hasAllowedSigners)
    {
        CheckDirectoryChain(root);
        var expected = new HashSet<string>(StringComparer.Ordinal)
            { PrivateKeyName, PrivateKeyName + ".pub" };
        if (hasAllowedSigners) expected.Add(AllowedSignersName);
        foreach (var entry in Directory.GetFileSystemEntries(root))
        {
            Require(expected.Remove(Path.GetFileName(entry)), "root-entry-unexpected");
            CheckRegularFile(entry);
        }
        Require(expected.Count == 0, "material-missing");
    }

    private static byte[] HashPrivateKey(string path)
    {
        var bytes = ReadBounded(path, 65536);
        try
        {
            using (var hash = SHA256.Create()) return hash.ComputeHash(bytes);
        }
        finally { Array.Clear(bytes, 0, bytes.Length); }
    }

    private static byte[] ReadBounded(string path, int maximumLength)
    {
        CheckRegularFile(path);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Require(stream.Length > 0 && stream.Length <= maximumLength, "material-length-invalid");
            var bytes = new byte[(int)stream.Length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var count = stream.Read(bytes, offset, bytes.Length - offset);
                Require(count > 0, "material-truncated");
                offset += count;
            }
            Require(stream.ReadByte() == -1, "material-length-changed");
            CheckRegularFile(path);
            return bytes;
        }
    }

    private static string ParsePublicKey(byte[] bytes, string identity)
    {
        string line;
        try { line = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw Failure("public-key-encoding-invalid"); }
        if (line.EndsWith("\r\n", StringComparison.Ordinal)) line = line.Substring(0, line.Length - 2);
        else if (line.EndsWith("\n", StringComparison.Ordinal)) line = line.Substring(0, line.Length - 1);
        var parts = line.Split(' ');
        Require(parts.Length == 3 && parts[0] == "ssh-ed25519" && parts[2] == identity, "public-key-shape-invalid");
        byte[] key;
        try { key = Convert.FromBase64String(parts[1]); }
        catch (FormatException) { throw Failure("public-key-base64-invalid"); }
        Require(Convert.ToBase64String(key) == parts[1] && key.Length == 51 &&
            key[0] == 0 && key[1] == 0 && key[2] == 0 && key[3] == 11 &&
            Encoding.ASCII.GetString(key, 4, 11) == "ssh-ed25519" &&
            key[15] == 0 && key[16] == 0 && key[17] == 0 && key[18] == 32, "public-key-wire-invalid");
        return line;
    }

    private static bool IsPrincipal(string identity)
    {
        if (string.IsNullOrEmpty(identity) || identity.Length > 128 || !IsAsciiLetterOrDigit(identity[0])) return false;
        foreach (var character in identity)
            if (!IsAsciiLetterOrDigit(character) && character != '.' && character != '_' &&
                character != '@' && character != '+' && character != '-') return false;
        return true;
    }

    private static string AbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path != path.Trim())
            throw Failure("path-invalid");
        Require(!path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\.\", StringComparison.Ordinal) &&
            !path.StartsWith(@"\??\", StringComparison.Ordinal), "path-namespace-invalid");
        foreach (var character in path) Require(!char.IsControl(character), "path-invalid");
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        foreach (var component in fullPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            Require(!component.EndsWith(".", StringComparison.Ordinal) && !component.EndsWith(" ", StringComparison.Ordinal), "path-alias-invalid");
            var stem = component.Split('.')[0].ToUpperInvariant();
            Require(stem != "CON" && stem != "PRN" && stem != "AUX" && stem != "NUL" &&
                stem != "CONIN$" && stem != "CONOUT$" &&
                !(stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                    stem[3] >= '1' && stem[3] <= '9'), "path-device-invalid");
            for (var index = 0; index + 1 < component.Length; index++)
                Require(component[index] != '~' || component[index + 1] < '0' || component[index + 1] > '9', "path-alias-invalid");
        }
        return fullPath;
    }

    private static void CheckDirectoryChain(string path)
    {
        var directory = new DirectoryInfo(path);
        while (true)
        {
            directory.Refresh();
            Require(directory.Exists, "directory-missing");
            Require((directory.Attributes & FileAttributes.ReparsePoint) == 0, "directory-reparse");
            var parent = directory.Parent;
            if (parent is null) return;
            directory = parent;
        }
    }

    private static void CheckRegularFile(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent)) throw Failure("file-parent-invalid");
        CheckDirectoryChain(parent);
        var file = new FileInfo(path);
        file.Refresh();
        Require(file.Exists, "file-missing");
        Require((file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) == 0,
            "file-not-regular");
    }

    private static bool EqualBytes(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
        return true;
    }

    private static string ResolveExecutable(string environmentName, string fileName)
    {
        var configured = Environment.GetEnvironmentVariable(environmentName);
        if (configured is not null) return ExecutablePath(configured, fileName);
        foreach (var part in (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = part;
            if (directory.Length >= 2 && directory[0] == '"' && directory[directory.Length - 1] == '"')
                directory = directory.Substring(1, directory.Length - 2);
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate)) return ExecutablePath(candidate, fileName);
        }
        throw Failure("executable-missing");
    }

    private static string ExecutablePath(string path, string fileName)
    {
        var fullPath = AbsolutePath(path);
        Require(Path.GetFileName(fullPath).Equals(fileName, StringComparison.OrdinalIgnoreCase), "executable-name-invalid");
        CheckRegularFile(fullPath);
        Require(new FileInfo(fullPath).Length > 0, "executable-empty");
        return fullPath;
    }

    private static bool IsAsciiLetterOrDigit(char value) => value >= 'a' && value <= 'z' ||
        value >= 'A' && value <= 'Z' || value >= '0' && value <= '9';
    private static bool IsAsciiHex(char value) => value >= '0' && value <= '9' ||
        value >= 'a' && value <= 'f' || value >= 'A' && value <= 'F';

    private static T Io<T>(Func<T> operation, string token)
    {
        try { return operation(); }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
            error is SecurityException || error is ArgumentException || error is NotSupportedException)
        { throw Failure(token); }
    }

    private static void Require(bool condition, string token)
    {
        if (!condition) throw Failure(token);
    }

    private static InvalidOperationException Failure(string token) => new InvalidOperationException("fixture-signing-" + token);
}
