using NUnit.Framework;
using STranslate.Plugin.Tts.FishAudio.Configuration;
using static TestAssertions;

namespace NUnitCases.Settings;

[TestFixture]
public sealed class ApiKeyFormatTests
{
    // Synthetic fixture: eight five-character groups followed by three characters.
    private const string PrefixedKey = "sk-fish-Ab0_-Ab0_-Ab0_-Ab0_-Ab0_-Ab0_-Ab0_-Ab0_-Ab0";

    [Test]
    [Property("FishAudioCaseId", "settings.api-key/api-key-format-accepts-legacy-and-prefixed-keys")]
    public void ApiKeyFormatAcceptsLegacyAndPrefixedKeys()
    {
        foreach (var key in new[] { AppliedKey, DraftKey, PrefixedKey, "sk-fish-" + new string('A', 43) })
        {
            AssertEqual(true, SettingsValidation.IsValidApiKeyFormat(key),
                "Both legacy and prefixed API Key formats should be accepted");
        }
    }

    [Test]
    [Property("FishAudioCaseId", "settings.api-key/api-key-format-rejects-malformed-keys")]
    public void ApiKeyFormatRejectsMalformedKeys()
    {
        string[] invalidKeys =
        [
            "", " ", "\t", "\r\n", "ABC",
            AppliedKey[..31], AppliedKey + "0", AppliedKey.ToUpperInvariant(),
            "sk-fish-", "sk-fish-" + new string('A', 42), "sk-fish-" + new string('A', 44),
            "SK-fish-" + new string('A', 43), "sk-FISH-" + new string('A', 43),
            "sk-other-" + new string('A', 43),
        ];
        foreach (var key in invalidKeys)
            AssertEqual(false, SettingsValidation.IsValidApiKeyFormat(key), "Malformed API Keys should be rejected");

        AssertEqual(false, SettingsValidation.IsValidApiKeyFormat(null!), "Null API Keys should be rejected safely");

        foreach (var key in new[] { AppliedKey, PrefixedKey })
        {
            foreach (var whitespace in new[] { " ", "\t", "\r", "\n", "\r\n" })
            {
                foreach (var malformed in new[] { whitespace + key, key + whitespace, key.Insert(10, whitespace) })
                    AssertEqual(false, SettingsValidation.IsValidApiKeyFormat(malformed),
                        "API Key matching should reject leading, trailing, and embedded whitespace");
            }
        }

        foreach (var invalidCharacter in new[] { '+', '/', '=', '.', '中' })
            AssertEqual(false, SettingsValidation.IsValidApiKeyFormat(PrefixedKey[..^1] + invalidCharacter),
                "Prefixed API Keys should allow only the specified suffix characters");
    }

    [Test]
    [Property("FishAudioCaseId", "settings.api-key/voice-id-format-remains-legacy-only")]
    public void VoiceIdFormatRemainsLegacyOnly()
    {
        AssertEqual(true, SettingsValidation.IsValidVoiceIdFormat(AppliedKey), "32-hex Voice IDs should remain valid");
        AssertEqual(false, SettingsValidation.IsValidVoiceIdFormat(AppliedKey.ToUpperInvariant()),
            "Voice IDs should retain their lowercase requirement");
        AssertEqual(false, SettingsValidation.IsValidVoiceIdFormat(PrefixedKey),
            "Supporting prefixed API Keys should not broaden Voice ID validation");
    }

}
