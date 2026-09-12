using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using LyricFloat.App.Models;

namespace LyricFloat.App.Services;

internal sealed class SecureTokenStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(AppLog.DataDirectory, "spotify.tokens.dpapi");

    public async Task SaveAsync(SpotifyToken token, CancellationToken cancellationToken)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(token);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<SpotifyToken?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return null;
        var encrypted = await File.ReadAllBytesAsync(_path, cancellationToken);
        var plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<SpotifyToken>(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public Task DeleteAsync()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
        return Task.CompletedTask;
    }
}
