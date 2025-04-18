using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Cryptography;
using System.Text;

namespace SteamCmdWeb.Services
{
    public class DecryptionService
    {
        private readonly ILogger<DecryptionService> _logger;
        private readonly string _encryptionKey;

        public DecryptionService(ILogger<DecryptionService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _encryptionKey = configuration["EncryptionKey"] ?? "SteamCmdWebSecureKey123!@#$%";
        }

        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plainText));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi mã hóa chuỗi");
                return plainText;
            }
        }

        public string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;

            try
            {
                return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cipherText));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi giải mã chuỗi");
                return cipherText;
            }
        }
    }
}