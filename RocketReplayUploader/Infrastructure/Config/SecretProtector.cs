using System.Security.Cryptography;
using System.Text;

namespace RocketReplayUploader.Infrastructure.Config;

// Cifra secretos (la API key) con DPAPI: solo el usuario de Windows actual
// puede descifrarlos, y no hace falta contraseña adicional.
public static class SecretProtector
{
    private const string Prefix = "v1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RocketReplayUploader.v1");

    public static string Protect(string plain)
    {
        var data = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain),
            Entropy,
            DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(data);
    }

    public static string? Unprotect(string stored)
    {
        try
        {
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return null; // no cifrado: lo trata como valor heredado en claro
            }

            var data = ProtectedData.Unprotect(
                Convert.FromBase64String(stored[Prefix.Length..]),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsProtected(string stored) => stored.StartsWith(Prefix, StringComparison.Ordinal);
}
