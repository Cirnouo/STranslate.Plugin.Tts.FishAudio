using System.Text.Json.Serialization;

namespace STranslate.Plugin.Tts.FishAudio.Model;

public class ModelListResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("items")]
    public List<ModelEntity> Items { get; set; } = [];
}

public class ModelEntity
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("cover_image")]
    public string CoverImage { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("samples")]
    public List<SampleEntity> Samples { get; set; } = [];

    [JsonPropertyName("languages")]
    public List<string> Languages { get; set; } = [];

    [JsonPropertyName("task_count")]
    public int TaskCount { get; set; }

    [JsonPropertyName("like_count")]
    public int LikeCount { get; set; }

    [JsonPropertyName("author")]
    public AuthorEntity? Author { get; set; }
}

public class SampleEntity
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("audio")]
    public string Audio { get; set; } = "";
}

public class AuthorEntity
{
    [JsonPropertyName("_id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = "";
}
