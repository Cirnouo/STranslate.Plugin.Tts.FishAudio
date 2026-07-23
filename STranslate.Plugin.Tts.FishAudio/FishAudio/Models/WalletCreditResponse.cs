using System.Text.Json.Serialization;

namespace STranslate.Plugin.Tts.FishAudio.Model;

public class WalletCreditResponse
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("credit")]
    public string Credit { get; set; } = "0";
}
