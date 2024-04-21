using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BL
{
    public class AESEncryptor
    {
        public static readonly string GenKey = "g1rmNZsVU5+maAU/nVhWg89t9qSNAU3jPqCnLDT1TDk=";
        public static readonly string GenIV = "wb6ZsDrefHgpaebdweEjcg==";
        public string Encrypt(string plainText)
        {
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
            return Convert.ToBase64String(encrypted);
        }

        public string DecryptAES(string cipherText)
        {
            // Check arguments. 
            if (string.IsNullOrWhiteSpace(cipherText))
            { 
                throw new ArgumentNullException("cipherText cannot be null");
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
                using MemoryStream msDecrypt = new MemoryStream(cipherTextString);
                using CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
                using StreamReader srDecrypt = new StreamReader(csDecrypt);

                // Read the decrypted bytes from the decrypting stream 
                // and place them in a string.
                plaintext = srDecrypt.ReadToEnd();

            }
            return plaintext;
        }

        public static string ByteArrayToString(byte[] arr)
        {
            return Convert.ToBase64String(arr);
        }
        public static byte[] StringToByteArray(string s)
        {
            return Convert.FromBase64String(s);
        }
    }
}
