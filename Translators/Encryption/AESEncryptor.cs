using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Translators.Encryption
{
    /// <summary>
    /// AES-256 symmetric encryption service using Rijndael algorithm.
    /// Caches encryption/decryption results in thread-safe dictionaries for performance.
    /// Used to encrypt/decrypt all PII data at rest.
    /// </summary>
    public class AESEncryptor
    {
        public static readonly string GenKey = "g1rmNZsVU5+maAU/nVhWg89t9qSNAU3jPqCnLDT1TDk=";
        public static readonly string GenIV = "wb6ZsDrefHgpaebdweEjcg==";

        private static readonly Dictionary<string, string> decryptionDic = [];
        private static readonly Dictionary<string, string> encryptionDic = [];
        /// <summary>
        /// Encrypts a plaintext string using AES-256. Returns a Base64-encoded ciphertext.
        /// Results are cached for repeated lookups.
        /// </summary>
        /// <param name="plainText">The plaintext to encrypt.</param>
        /// <returns>Base64-encoded encrypted string.</returns>
        public string Encrypt(string plainText)
        {
            lock (encryptionDic)
            {
                if (encryptionDic.ContainsKey(plainText))
                {
                    return encryptionDic[plainText];
                }
            }
            byte[] encrypted;

            using (var rijAlg = new RijndaelManaged())
            {
                rijAlg.Key = Convert.FromBase64String(GenKey);
                rijAlg.IV = Convert.FromBase64String(GenIV);


                // Create a decryptor to perform the stream transform.
                var encryptor = rijAlg.CreateEncryptor(rijAlg.Key, rijAlg.IV);
                // Create the streams used for encryption. 
                using var msEncrypt = new MemoryStream();
                using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
                using (var swEncrypt = new StreamWriter(csEncrypt))
                {

                    //Write all data to the stream.
                    swEncrypt.Write(plainText);
                }
                encrypted = msEncrypt.ToArray();
            }
            // Return the encrypted bytes from the memory stream. 
            var enc = Convert.ToBase64String(encrypted);
            lock (encryptionDic)
            {
                encryptionDic[plainText] = enc;
            }
            return enc;
        }

        /// <summary>
        /// Decrypts a Base64-encoded ciphertext string back to plaintext using AES-256.
        /// Results are cached for repeated lookups.
        /// </summary>
        /// <param name="cipherText">The Base64-encoded ciphertext to decrypt.</param>
        /// <returns>The decrypted plaintext string.</returns>
        /// <exception cref="ArgumentNullException">Thrown if cipherText is null or whitespace.</exception>
        public string Decrypt(string cipherText)
        {
            // Check arguments. 
            if (string.IsNullOrWhiteSpace(cipherText))
            {
                throw new ArgumentNullException("cipherText cannot be null");
            }
            lock (decryptionDic)
            {
                if (decryptionDic.ContainsKey(cipherText))
                {
                    return decryptionDic[cipherText];
                }
            }
            // Declare the string used to hold 
            // the decrypted text. 
            string plaintext = null;
            var cipherTextString = Convert.FromBase64String(cipherText);//HexString.StringTobyteArray(CipherText);
            // Create an RijndaelManaged object 
            // with the specified key and IV. 
            using (var rijAlg = new RijndaelManaged())
            {
                rijAlg.Key = Convert.FromBase64String(GenKey);
                rijAlg.IV = Convert.FromBase64String(GenIV);

                // Create a decrytor to perform the stream transform.
                var decryptor = rijAlg.CreateDecryptor(rijAlg.Key, rijAlg.IV);

                // Create the streams used for decryption. 
                using MemoryStream msDecrypt = new(cipherTextString);
                using CryptoStream csDecrypt = new(msDecrypt, decryptor, CryptoStreamMode.Read);
                using StreamReader srDecrypt = new(csDecrypt);

                // Read the decrypted bytes from the decrypting stream 
                // and place them in a string.
                plaintext = srDecrypt.ReadToEnd();

            }
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
