using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AIA.Services
{
    /// <summary>
    /// Protects secrets at rest with Windows DPAPI (current user scope).
    /// Protected values are prefixed with "enc:v1:" so legacy plaintext values can be
    /// detected and migrated transparently.
    /// </summary>
    public static class SecretProtector
    {
        private const string Prefix = "enc:v1:";

        public static bool IsProtected(string? value) =>
            value != null && value.StartsWith(Prefix, StringComparison.Ordinal);

        public static string Protect(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText ?? string.Empty;

            if (IsProtected(plainText))
                return plainText;

            try
            {
                var encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plainText),
                    null,
                    DataProtectionScope.CurrentUser);

                return Prefix + Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to encrypt secret: {ex.Message}");
                throw new CryptographicException("Failed to protect a secret at rest.", ex);
            }
        }

        public static string Unprotect(string? storedValue)
        {
            if (string.IsNullOrEmpty(storedValue))
                return storedValue ?? string.Empty;

            if (!IsProtected(storedValue))
                return storedValue;

            try
            {
                var payload = Convert.FromBase64String(storedValue.Substring(Prefix.Length));
                var decrypted = ProtectedData.Unprotect(payload, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception ex)
            {
                // Different Windows user or machine: the secret cannot be recovered.
                System.Diagnostics.Debug.WriteLine($"Failed to decrypt secret: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Encrypts a whole JSON file (used for files that contain multiple secrets).
        /// </summary>
        public static async Task WriteEncryptedFileAsync(string path, string json)
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                System.IO.Directory.CreateDirectory(directory);

            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json),
                null,
                DataProtectionScope.CurrentUser);

            var tempPath = path + ".tmp";
            await System.IO.File.WriteAllBytesAsync(tempPath, encrypted).ConfigureAwait(false);
            System.IO.File.Move(tempPath, path, true);
        }

        /// <summary>
        /// Reads a DPAPI-protected JSON file. Returns null when the file is missing or cannot
        /// be decrypted.
        /// </summary>
        public static async Task<string?> ReadEncryptedFileAsync(string path)
        {
            if (!System.IO.File.Exists(path))
                return null;

            try
            {
                var encrypted = await System.IO.File.ReadAllBytesAsync(path).ConfigureAwait(false);
                var json = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to read protected file '{path}': {ex.Message}");
                return null;
            }
        }
    }
}
