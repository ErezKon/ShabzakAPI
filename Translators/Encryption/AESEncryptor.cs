using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace Translators.Encryption
{
    /// <summary>
    /// AES-256 symmetric encryption service.
    /// - Key and legacy IV are injected at startup via Configure() — no hardcoded secrets.
    /// - New encryptions use a random IV prepended to the ciphertext (prefixed "v2:").
    /// - Decryption is backward-compatible: "v2:" prefix → random IV; otherwise → legacy static IV.
    /// - Results are cached in thread-safe dictionaries for performance.
    /// </summary>
    public class AESEncryptor
    {
        // SECURITY: Key/IV are no longer hardcoded. Call Configure() from Program.cs at startup.
        private static byte[]? _key;
        private static byte[]? _legacyIV;

        private static readonly Dictionary<string, string> decryptionDic = [];
        private static readonly Dictionary<string, string> encryptionDic = [];

        /// <summary>
        /// Initializes AES key and legacy IV from external configuration.
        /// Must be called once at startup before any Encrypt/Decrypt operations.
        /// </summary>
        /// <param name="base64Key">Base64-encoded 256-bit AES key.</param>
        /// <param name="base64LegacyIV">Base64-encoded 128-bit IV used by existing encrypted data.</param>
        public static void Configure(string base64Key, string base64LegacyIV)
        {
            _key = Convert.FromBase64String(base64Key);
            _legacyIV = Convert.FromBase64String(base64LegacyIV);
        }

        private static byte[] GetKey() =>
            _key ?? throw new InvalidOperationException(
                "AESEncryptor not configured. Call AESEncryptor.Configure() in Program.cs.");

        /// <summary>
        /// Encrypts plaintext using AES-256 with a random IV.
        /// Output format: "v2:" + Base64(randomIV + ciphertext).
        /// Cached for repeated lookups.
        /// </summary>
        public string Encrypt(string plainText)
        {
            lock (encryptionDic)
            {
                if (encryptionDic.TryGetValue(plainText, out var cached))
                {
                    return cached;
                }
            }

            var key = GetKey();

            // SECURITY: Generate a random IV for each encryption — identical plaintexts
            // no longer produce identical ciphertexts (mitigates frequency analysis).
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV(); // random 128-bit IV

            var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var msEncrypt = new MemoryStream();
            using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
            using (var swEncrypt = new StreamWriter(csEncrypt))
            {
                swEncrypt.Write(plainText);
            }

            // Prepend IV to ciphertext so decryption can extract it.
            var ivPlusCipher = new byte[aes.IV.Length + msEncrypt.ToArray().Length];
            Buffer.BlockCopy(aes.IV, 0, ivPlusCipher, 0, aes.IV.Length);
            Buffer.BlockCopy(msEncrypt.ToArray(), 0, ivPlusCipher, aes.IV.Length, msEncrypt.ToArray().Length);

            var enc = "v2:" + Convert.ToBase64String(ivPlusCipher);
            lock (encryptionDic)
            {
                encryptionDic[plainText] = enc;
            }
            return enc;
        }

        /// <summary>
        /// Decrypts ciphertext. Supports two formats:
        /// - "v2:" prefix → random IV (first 16 bytes of decoded payload)
        /// - No prefix → legacy format using the static IV from configuration
        /// Cached for repeated lookups.
        /// </summary>
        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrWhiteSpace(cipherText))
            {
                throw new ArgumentNullException(nameof(cipherText), "cipherText cannot be null or empty.");
            }
            lock (decryptionDic)
            {
                if (decryptionDic.TryGetValue(cipherText, out var cached))
                {
                    return cached;
                }
            }

            var key = GetKey();
            byte[] iv;
            byte[] cipherBytes;

            if (cipherText.StartsWith("v2:"))
            {
                // New format: IV is prepended to the ciphertext.
                var combined = Convert.FromBase64String(cipherText[3..]);
                iv = new byte[16]; // AES block size = 128 bits
                cipherBytes = new byte[combined.Length - 16];
                Buffer.BlockCopy(combined, 0, iv, 0, 16);
                Buffer.BlockCopy(combined, 16, cipherBytes, 0, cipherBytes.Length);
            }
            else
            {
                // Legacy format: uses the static IV from configuration.
                iv = _legacyIV ?? throw new InvalidOperationException(
                    "AESEncryptor legacy IV not configured. Call AESEncryptor.Configure() in Program.cs.");
                cipherBytes = Convert.FromBase64String(cipherText);
            }

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

            using var msDecrypt = new MemoryStream(cipherBytes);
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);
            var plaintext = srDecrypt.ReadToEnd();

            lock (decryptionDic)
            {
                decryptionDic[cipherText] = plaintext;
            }
            return plaintext;
        }

        /// <summary>
        /// Converts a byte array to a Base64 string.
        /// </summary>
        public static string ByteArrayToString(byte[] arr)
        {
            return Convert.ToBase64String(arr);
        }
        /// <summary>
        /// Converts a Base64 string to a byte array.
        /// </summary>
        public static byte[] StringToByteArray(string s)
        {
            return Convert.FromBase64String(s);
        }
    }
}
