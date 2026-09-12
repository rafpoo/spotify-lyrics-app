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
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");
        if (!File.Exists(path)) throw new InvalidOperationException("Configure ClientId in LyricFloat.App/appsettings.Development.json, then rebuild.");
        SpotifyConfiguration config;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            config = json.RootElement.GetProperty("Spotify").Deserialize<SpotifyConfiguration>()
                ?? throw new JsonException();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException("Invalid Spotify development configuration. Check the example file.");
        }
        if (string.IsNullOrWhiteSpace(config.ClientId) || config.ClientId.Length != 32 || !config.ClientId.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Set your 32-character Spotify Client ID in appsettings.Development.json.");
        if (config.RedirectUri != CallbackUri)
            throw new InvalidOperationException($"RedirectUri must be exactly {CallbackUri}");
        return config;
    }
}
