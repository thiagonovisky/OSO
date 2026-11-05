using System.Security.Cryptography;

namespace OSO.Models;

public static class PasswordHashService
{
    private const int SaltSize = 16;
    private const int KeySize = 16;
    private const int Iterations = 100000;

    public static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public static bool VerifyPassword(string plainPassword, string storedPassword)
    {
        if (string.IsNullOrWhiteSpace(plainPassword) || string.IsNullOrWhiteSpace(storedPassword))
        {
            return false;
        }

        if (!TryParseHash(storedPassword, out byte[] salt, out byte[] expectedKey))
        {
            return storedPassword == plainPassword;
        }

        byte[] actualKey = Rfc2898DeriveBytes.Pbkdf2(plainPassword, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }

    private static bool TryParseHash(string storedPassword, out byte[] salt, out byte[] key)
    {
        salt = Array.Empty<byte>();
        key = Array.Empty<byte>();

        string[] parts = storedPassword.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[0]);
            key = Convert.FromBase64String(parts[1]);
            return salt.Length == SaltSize && key.Length == KeySize;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}