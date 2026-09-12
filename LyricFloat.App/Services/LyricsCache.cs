using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LyricFloat.App.Models;

namespace LyricFloat.App.Services;

internal sealed class LyricsCache(string? directory = null)
{
    private readonly string _directory = directory ?? Path.Combine(AppLog.DataDirectory, "cache");
    private readonly SemaphoreSlim _maintenanceGate = new(1, 1);
    private int _generation;
    public int Generation => Volatile.Read(ref _generation);
    private sealed record Entry(int Version, string Key, LrclibLyricsResponse Data);

    public static string KeyFor(SpotifyTrack track)
    {
        var identity = !string.IsNullOrWhiteSpace(track.Id) ? "spotify:" + track.Id
            : JsonSerializer.Serialize(new { track.Name, track.Artist, track.Album, track.DurationMs });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    public async Task<LrclibLyricsResponse?> LoadAsync(SpotifyTrack track, CancellationToken cancellationToken)
    {
        var key = KeyFor(track);
        var path = Path.Combine(_directory, key + ".json");
        try
        {
            if (!File.Exists(path)) return null;
            var entry = JsonSerializer.Deserialize<Entry>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
            if (entry is { Version: 1, Data: not null } && entry.Key == key) return entry.Data;
            AppLog.Write("Lyrics cache entry invalid; fetching fresh lyrics.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { AppLog.Write("Lyrics cache could not be read; fetching fresh lyrics."); }
        return null;
    }

    public async Task SaveAsync(SpotifyTrack track, LrclibLyricsResponse data, CancellationToken cancellationToken, int? generation = null)
    {
        await _maintenanceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var key = KeyFor(track);
        var temporary = Path.Combine(_directory, key + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (generation is { } expected && expected != Generation) return;
            Directory.CreateDirectory(_directory);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Entry(1, key, data)), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, Path.Combine(_directory, key + ".json"), true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { AppLog.Write("Lyrics cache write failed; using downloaded lyrics in memory."); }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { AppLog.Write("Lyrics temporary cache cleanup failed."); }
            _maintenanceGate.Release();
        }
    }

    public async Task<bool> ClearAsync()
    {
        await _maintenanceGate.WaitAsync();
        try
        {
            Interlocked.Increment(ref _generation);
            if (!Directory.Exists(_directory)) return true;
            var success = true;
            foreach (var file in Directory.EnumerateFiles(_directory))
            {
                var name = Path.GetFileName(file);
                var key = name.Split('.')[0];
                if (key.Length != 64 || !key.All(Uri.IsHexDigit) || !(name.EndsWith(".json", StringComparison.Ordinal) || name.EndsWith(".tmp", StringComparison.Ordinal))) continue;
                try { File.Delete(file); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { success = false; }
            }
            if (!success) AppLog.Write("Some lyrics cache files could not be deleted.");
            return success;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { AppLog.Write("Lyrics cache could not be cleared."); return false; }
        finally { _maintenanceGate.Release(); }
    }
}
