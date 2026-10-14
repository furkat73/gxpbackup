using System.Security.Cryptography;

namespace MySqlBackupUtility.Security;

/// <summary>DPAPI (CurrentUser) encryption for DB passwords. ТЗ п.4.4: запрет plain-text/base64-only.</summary>
public static class CryptoService
{
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        byte[] plain = System.Text.Encoding.UTF8.GetBytes(plainText);
        byte[] enc = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(enc);
    }

    public static string Decrypt(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64)) return "";
        try
        {
            byte[] enc = Convert.FromBase64String(encryptedBase64);
            byte[] plain = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return "";
        }
    }
}
