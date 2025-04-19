using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Services
{
    public class DecryptionService
    {
        private readonly ILogger<DecryptionService> _logger;
        private readonly string _encryptionKey;

        public DecryptionService(ILogger<DecryptionService> logger)
        {
            _logger = logger;
            _encryptionKey = "SteamCmdWebSecureKey123!@#$%"; // Khóa cố định để đảm bảo tương thích
        }

        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return string.Empty;
            }

            try
            {
                byte[] clearBytes = Encoding.Unicode.GetBytes(plainText);
                using var encryptor = Aes.Create();

                // Sử dụng Rfc2898DeriveBytes với số lần lặp
                var pdb = new Rfc2898DeriveBytes(_encryptionKey,
                    new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 },
                    1000);

                encryptor.Key = pdb.GetBytes(32);
                encryptor.IV = pdb.GetBytes(16);

                using var ms = new MemoryStream();
                using (var cs = new CryptoStream(ms, encryptor.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(clearBytes, 0, clearBytes.Length);
                }

                return Convert.ToBase64String(ms.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi mã hóa chuỗi");
                throw new Exception("Lỗi khi mã hóa chuỗi", ex);
            }
        }

        public string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
            {
                return string.Empty;
            }

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                using var encryptor = Aes.Create();

                // Sử dụng Rfc2898DeriveBytes với số lần lặp
                var pdb = new Rfc2898DeriveBytes(_encryptionKey,
                    new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 },
                    1000);

                encryptor.Key = pdb.GetBytes(32);
                encryptor.IV = pdb.GetBytes(16);

                using var ms = new MemoryStream();
                using (var cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(cipherBytes, 0, cipherBytes.Length);
                }

                return Encoding.Unicode.GetString(ms.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi giải mã chuỗi");
                return string.Empty; // Trả về chuỗi rỗng nếu giải mã thất bại
            }
        }
    }
}