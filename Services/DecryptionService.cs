using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Services
{
    public class DecryptionService
    {
        private readonly string _encryptionKey;
        private readonly ILogger<DecryptionService> _logger;

        public DecryptionService(ILogger<DecryptionService> logger)
        {
            _logger = logger;
            _encryptionKey = "SteamCmdWebSecureKey123!@#$%"; // Khóa cố định để đồng bộ với client
        }

        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";

            try
            {
                byte[] clearBytes = Encoding.Unicode.GetBytes(plainText);
                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(_encryptionKey,
                        new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 }, 1000);
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);

                    using MemoryStream ms = new MemoryStream();
                    using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(clearBytes, 0, clearBytes.Length);
                        cs.Close();
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi mã hóa chuỗi");
                throw;
            }
        }

        public string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(_encryptionKey,
                        new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 }, 1000);
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);

                    using MemoryStream ms = new MemoryStream();
                    using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(cipherBytes, 0, cipherBytes.Length);
                        cs.Close();
                    }
                    return Encoding.Unicode.GetString(ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi giải mã chuỗi");
                return "[Lỗi giải mã]";
            }
        }
    }
}