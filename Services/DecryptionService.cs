using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace SteamCmdWeb.Services
{
    public class DecryptionService
    {
        private readonly string _encryptionKey;
        private readonly string _encryptionIV;

        public DecryptionService(IConfiguration configuration)
        {
            // Sử dụng cùng key và IV với phía client
            _encryptionKey = configuration["Encryption:Key"] ??
                            Environment.GetEnvironmentVariable("ENCRYPTION_KEY") ??
                            "ThisIsASecretKey1234567890123456";
            _encryptionIV = configuration["Encryption:IV"] ??
                           Environment.GetEnvironmentVariable("ENCRYPTION_IV") ??
                           "ThisIsAnIV123456";
        }

        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;

            byte[] clearBytes = Encoding.Unicode.GetBytes(plainText);
            using (Aes encryptor = Aes.Create())
            {
                byte[] keyBytes = Encoding.UTF8.GetBytes(_encryptionKey);
                byte[] ivBytes = Encoding.UTF8.GetBytes(_encryptionIV);

                // Đảm bảo đúng độ dài key và IV
                Array.Resize(ref keyBytes, 32); // 256 bit
                Array.Resize(ref ivBytes, 16);  // 128 bit

                encryptor.Key = keyBytes;
                encryptor.IV = ivBytes;

                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateEncryptor(), CryptoStreamMode.Write))
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
            if (string.IsNullOrEmpty(cipherText) || cipherText == "encrypted") return string.Empty;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                using (Aes encryptor = Aes.Create())
                {
                    byte[] keyBytes = Encoding.UTF8.GetBytes(_encryptionKey);
                    byte[] ivBytes = Encoding.UTF8.GetBytes(_encryptionIV);

                    // Đảm bảo đúng độ dài key và IV
                    Array.Resize(ref keyBytes, 32); // 256 bit
                    Array.Resize(ref ivBytes, 16);  // 128 bit

                    encryptor.Key = keyBytes;
                    encryptor.IV = ivBytes;

                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            cs.Write(cipherBytes, 0, cipherBytes.Length);
                            cs.Close();
                        }
                        return Encoding.Unicode.GetString(ms.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi giải mã: {ex.Message}");
                return string.Empty;
            }
        }
    }
}