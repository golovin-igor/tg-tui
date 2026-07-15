using Tomlyn.Serialization;

namespace TgTui.Core.Config;

public sealed class AppConfig
{
    [TomlPropertyName("api_id")]
    public int? ApiId { get; set; }

    [TomlPropertyName("api_hash")]
    public string? ApiHash { get; set; }

    public bool HasCredentials =>
        ApiId is > 0 && !string.IsNullOrWhiteSpace(ApiHash);
}
