using System.Security.Cryptography;
using Microsoft.Win32;

namespace GofileUploader;

/// <summary>
/// Persists small bits of user state (API token, folder id) without ever touching a file
/// on disk. Values are DPAPI-encrypted (tied to the current Windows user account) and
/// stored under a registry key private to this app.
/// </summary>
internal static class SecureSettings
{
    private const string KeyPath = @"Software\GofileUploader";

    public static void SaveToken(string? token) => SaveEncrypted("Token", token);
    public static string? LoadToken() => LoadEncrypted("Token");

    public static void SaveFolder(string? folder) => SavePlain("FolderId", folder);
    public static string? LoadFolder() => LoadPlain("FolderId");

    private static void SaveEncrypted(string name, string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (string.IsNullOrEmpty(value))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            return;
        }

        var plainBytes = System.Text.Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        key.SetValue(name, Convert.ToBase64String(protectedBytes), RegistryValueKind.String);
    }

    private static string? LoadEncrypted(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key?.GetValue(name) is not string base64) return null;

        try
        {
            var protectedBytes = Convert.FromBase64String(base64);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            // Corrupted, or encrypted by a different user profile - ignore rather than crash.
            return null;
        }
    }

    private static void SavePlain(string name, string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (string.IsNullOrEmpty(value))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
            return;
        }
        key.SetValue(name, value, RegistryValueKind.String);
    }

    private static string? LoadPlain(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) as string;
    }
}
