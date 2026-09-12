using System.Security.Cryptography;
using System.Text;

namespace LyricFloat.App.Helpers;

internal static class PkceHelper
{
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(64));
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string CreateChallenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool ValidateState(string expected, string? actual) => actual is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
