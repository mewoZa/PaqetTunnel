using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PaqetTunnel.Services;

/// <summary>
/// BUG-01 fix: Encrypts/decrypts credentials using AES with a machine+user-derived key.
/// Passwords are never stored in plaintext on disk.
/// </summary>
internal static class CredentialHelper
{
    private static byte[] DeriveKey()
    {
        var seed = Environment.MachineName + "|" + Environment.UserName + "|PaqetTunnel";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }

    internal static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        try
        {
            using var aes = Aes.Create();
            aes.Key = DeriveKey();
            aes.GenerateIV();
            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length);
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                var bytes = Encoding.UTF8.GetBytes(plainText);
                cs.Write(bytes, 0, bytes.Length);
            }
            return Convert.ToBase64String(ms.ToArray());
        }
        catch (Exception ex)
        {
            Logger.Error("CredentialHelper.Protect failed", ex);
            return "";
        }
    }

    internal static string Unprotect(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return "";
        try
        {
            var data = Convert.FromBase64String(encrypted);
            if (data.Length < 17) return ""; // IV (16) + at least 1 byte
            using var aes = Aes.Create();
            aes.Key = DeriveKey();
            var iv = new byte[16];
            Array.Copy(data, 0, iv, 0, 16);
            aes.IV = iv;
            using var ms = new MemoryStream(data, 16, data.Length - 16);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var reader = new StreamReader(cs, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch
        {
            return ""; // Decryption failed — possibly different machine/user
        }
    }
}
