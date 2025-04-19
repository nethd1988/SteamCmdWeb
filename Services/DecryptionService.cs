using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Services
{
    public class DecryptionService
    {
        private readonly ILogger<DecryptionService> _logger;
        private readonly string _encryptionKey;
        private readonly string _encryptionIV;

        public DecryptionService(IConfiguration configuration, ILogger<DecryptionService> logger)
        {
            _logger = logger;
            _encryptionKey = configuration["Encryption:Key"] ?? "ThisIsASecretKey1234567890123456";
            _encryptionIV = configuration["Encryption:IV"] ?? "ThisIsAnIV123456";
        }

        public string DecryptString(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return string.Empty;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(encryptedText);
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
                        string decryptedText = Encoding.Unicode.GetString(ms.ToArray());
                        return decryptedText;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi giải mã chuỗi: {Error}", ex.Message);
                return string.Empty;
            }
        }

        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi mã hóa chuỗi: {Error}", ex.Message);
                return string.Empty;
            }
        }
    }
}