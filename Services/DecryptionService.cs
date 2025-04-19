using System;
using System.Security.Cryptography;
using System.Text;

namespace SteamCmdWeb.Services
{
    public class DecryptionService
    {
        private readonly string _encryptionKey = "SteamCmdWebSecureKey123!@#$%";

        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            byte[] clearBytes = Encoding.Unicode.GetBytes(plainText);
            using (Aes encryptor = Aes.Create())
            {
                var pdb = new Rfc2898DeriveBytes(_encryptionKey,
                    new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 },
                    1000);
                encryptor.Key = pdb.GetBytes(32);
                encryptor.IV = pdb.GetBytes(16);
                using (var ms = new System.IO.MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(clearBytes, 0, clearBytes.Length);
                        cs.Close();
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        public string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return string.Empty;

            try
            {
                cipherText = cipherText.Replace(" ", "+");
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                using (Aes encryptor = Aes.Create())
                {
                    var pdb = new Rfc2898DeriveBytes(_encryptionKey,
                        new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 },
                        1000);
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    using (var ms = new System.IO.MemoryStream())
                    {
                        using (var cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            cs.Write(cipherBytes, 0, cipherBytes.Length);
                            cs.Close();
                        }
                        return Encoding.Unicode.GetString(ms.ToArray());
                    }
                }
            }
            catch
            {
                // Tr? v? chu?i g?c n?u kh�ng gi?i m� ???c (c� th? l� ch?a ???c m� h�a)
                return cipherText;
            }
        }
    }
}