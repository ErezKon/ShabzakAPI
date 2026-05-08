using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Translators.Encryption
{
    /// <summary>
    /// SHA-512 one-way hashing utility. Used for password hashing (combined with per-user salt).
    /// </summary>
    public abstract class Sha512Encryptor
    {
        /// <summary>
        /// Computes a SHA-512 hash of the input string and returns it as a lowercase hex string.
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
    }
}
