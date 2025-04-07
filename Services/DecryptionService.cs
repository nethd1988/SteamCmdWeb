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
        private readonly string _encryptionKey;
        private readonly ILogger<DecryptionService> _logger;

        public DecryptionService(IConfiguration configuration, ILogger<DecryptionService> logger)
        {
            // Sửa dòng này - thay vì ném lỗi khi không có cấu hình, cung cấp một khóa mặc định an toàn
            _encryptionKey = configuration["EncryptionKey"] ?? "SteamCmdWebSecureKey123!@#$%";
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Log thông báo nếu đang sử dụng khóa mặc định
            if (configuration["EncryptionKey"] == null)
            {
                _logger.LogWarning("EncryptionKey không được cấu hình trong appsettings.json. Đang sử dụng khóa mặc định.");
            }
        }

        public string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";

            try
            {
                if (!cipherText.Contains("/") && !cipherText.Contains("=") &&
                    !cipherText.StartsWith("w5") && !cipherText.StartsWith("HEQ") &&
                    !cipherText.StartsWith("aeB") && !cipherText.StartsWith("gLQ") &&
                    !cipherText.StartsWith("sIV") && !cipherText.StartsWith("1x"))
                {
                    return cipherText;
                }

                byte[] cipherBytes;
                try
                {
                    cipherBytes = Convert.FromBase64String(cipherText);
                }
                catch
                {
                    _logger.LogWarning("Chuỗi '{CipherText}' không phải là Base64, trả về nguyên bản", cipherText);
                    return cipherText;
                }

                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(
                        _encryptionKey,
                        new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });

                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);

                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            try
                            {
                                cs.Write(cipherBytes, 0, cipherBytes.Length);
                                cs.Close();
                                return Encoding.Unicode.GetString(ms.ToArray());
                            }
                            catch
                            {
                                _logger.LogWarning("Không thể giải mã chuỗi, trả về chuỗi gốc");
                                return cipherText;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi giải mã chuỗi");
                return cipherText;
            }
        }

        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";

            try
            {
                if (plainText.Contains("/") && plainText.Contains("="))
                {
                    return plainText;
                }

                byte[] clearBytes = Encoding.Unicode.GetBytes(plainText);

                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(
                        _encryptionKey,
                        new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });

                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);

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
                _logger.LogError(ex, "Lỗi khi mã hóa chuỗi");
                return plainText;
            }
        }
    }
}