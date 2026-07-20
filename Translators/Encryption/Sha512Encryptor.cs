using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Translators.Encryption
{
    /// <summary>
    /// Password hashing utility. Supports:
    /// - Legacy: single-round SHA-512 (for existing accounts).
    /// - New:    PBKDF2-HMAC-SHA512 with 100,000 iterations (key-stretched).
    ///
    /// Stored format for PBKDF2: "pbkdf2:{iterations}:{saltBase64}:{hashBase64}"
    /// Legacy format:            128-char lowercase hex string (no prefix).
    /// </summary>
    public abstract class Sha512Encryptor
    {
        private const string Pbkdf2Prefix = "pbkdf2:";
        private const int Pbkdf2Iterations = 100_000;
        private const int Pbkdf2SaltBytes = 16;
        private const int Pbkdf2HashBytes = 64; // 512 bits

        /// <summary>
        /// Legacy: Computes a single-round SHA-512 hash and returns it as a lowercase hex string.
        /// Used for verifying old passwords and for username derivation (which is not security-sensitive).
        /// </summary>
        /// <param name="value">The string to hash.</param>
        /// <returns>128-character lowercase hex hash string.</returns>
        public static string Encrypt(string value)
        {
            var sb = new StringBuilder();

            using (SHA512 hash = SHA512.Create())
            {
                var enc = Encoding.UTF8;
                var result = hash.ComputeHash(enc.GetBytes(value));

                foreach (var b in result)
                    sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Creates a PBKDF2-HMAC-SHA512 hash of the password with a random salt.
        /// Returns the result in the portable "pbkdf2:{iterations}:{saltBase64}:{hashBase64}" format.
        /// </summary>
        /// <param name="password">The password (or pre-hashed password) to hash.</param>
        /// <returns>Self-describing hash string with embedded salt and iteration count.</returns>
        public static string HashWithPbkdf2(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(Pbkdf2SaltBytes);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA512,
                Pbkdf2HashBytes);
            return $"{Pbkdf2Prefix}{Pbkdf2Iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        /// <summary>
        /// Verifies a password against a stored hash. Automatically detects the format:
        /// - "pbkdf2:..." → PBKDF2-HMAC-SHA512 verification.
        /// - Otherwise   → legacy SHA-512(password + salt) comparison.
        /// </summary>
        /// <param name="password">The password (or pre-hashed password) to verify.</param>
        /// <param name="storedHash">The stored hash from the database.</param>
        /// <param name="legacySalt">The per-user salt (only used for legacy SHA-512 verification).</param>
        /// <returns>True if the password matches.</returns>
        public static bool Verify(string password, string storedHash, string legacySalt)
        {
            if (storedHash.StartsWith(Pbkdf2Prefix))
            {
                // Format: "pbkdf2:{iterations}:{saltBase64}:{hashBase64}"
                var parts = storedHash[Pbkdf2Prefix.Length..].Split(':');
                if (parts.Length != 3) return false;

                var iterations = int.Parse(parts[0]);
                var salt = Convert.FromBase64String(parts[1]);
                var expectedHash = Convert.FromBase64String(parts[2]);

                var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    salt,
                    iterations,
                    HashAlgorithmName.SHA512,
                    expectedHash.Length);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }

            // Legacy: SHA-512(password + salt)
            var legacyHash = Encrypt($"{password}{legacySalt}");
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(legacyHash),
                Encoding.UTF8.GetBytes(storedHash));
        }

        /// <summary>
        /// Returns true if the stored hash is in the legacy (non-PBKDF2) format and should be upgraded.
        /// </summary>
        public static bool NeedsUpgrade(string storedHash)
        {
            return !storedHash.StartsWith(Pbkdf2Prefix);
        }
    }
}
