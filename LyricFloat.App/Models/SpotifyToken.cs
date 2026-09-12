namespace LyricFloat.App.Models;

// Never bind or log this model; it is serialized only inside the protected token store.
internal sealed class SpotifyToken
{
    public string ClientId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public string Scope { get; set; } = "";
    public string TokenType { get; set; } = "Bearer";
}
