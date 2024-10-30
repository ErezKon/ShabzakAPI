using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Translators.Encryption
{
    public abstract class Sha512Encryptor
    {
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
