using System.IO;
using System.Text.Json;

namespace LyricFloat.App.Services;

internal sealed class SpotifyConfiguration
{
    public const string CallbackUri = "http://127.0.0.1:43821/callback";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = CallbackUri;

    public static SpotifyConfiguration Load()
    {
        var path = Path.Combine(AppLog.DataDirectory, "spotify.json");
        if (!File.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
        if (!File.Exists(path)) throw new InvalidOperationException("Open Settings → Spotify to configure your Client ID, then connect.");
        SpotifyConfiguration config;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            config = json.RootElement.GetProperty("Spotify").Deserialize<SpotifyConfiguration>()
                ?? throw new JsonException();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException("Spotify configuration is invalid. Open Settings → Spotify to update it.");
        }
        if (string.IsNullOrWhiteSpace(config.ClientId) || config.ClientId.Length != 32 || !config.ClientId.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Enter your 32-character Spotify Client ID in Settings → Spotify.");
        if (config.RedirectUri != CallbackUri)
            throw new InvalidOperationException($"RedirectUri must be exactly {CallbackUri}");
        return config;
    }

    public static async Task SaveClientIdAsync(string clientId)
    {
        clientId = clientId.Trim();
        if (clientId.Length != 32 || !clientId.All(Uri.IsHexDigit)) throw new InvalidOperationException("Enter a valid 32-character Spotify Client ID.");
        Directory.CreateDirectory(AppLog.DataDirectory);
        var path = Path.Combine(AppLog.DataDirectory, "spotify.json");
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new { Spotify = new SpotifyConfiguration { ClientId = clientId } }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
