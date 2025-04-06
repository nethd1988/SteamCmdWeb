using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Services
{
    /// <summary>
    /// Dịch vụ mã hóa và giải mã thông tin nhạy cảm
    /// </summary>
    public class DecryptionService
    {
        private readonly ILogger<DecryptionService> _logger;
        private readonly string _encryptionKey = "yourEncryptionKey123!@#"; // Khóa mã hóa cần giữ bí mật

        public DecryptionService(ILogger<DecryptionService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Giải mã chuỗi đã được mã hóa
        /// </summary>
        /// <param name="cipherText">Chuỗi đã mã hóa (Base64)</param>
        /// <returns>Chuỗi đã giải mã</returns>
        public string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";
            
            try
            {
                // Kiểm tra xem chuỗi có phải là chuỗi mã hóa đúng định dạng không
                if (!cipherText.Contains("/") && !cipherText.Contains("=") && 
                    !cipherText.StartsWith("w5") && !cipherText.StartsWith("HEQ") && 
                    !cipherText.StartsWith("aeB") && !cipherText.StartsWith("gLQ") && 
                    !cipherText.StartsWith("sIV") && !cipherText.StartsWith("1x"))
                {
                    // Trả về nguyên bản nếu không phải chuỗi mã hóa
                    return cipherText;
                }
                
                // Thử chuyển đổi từ Base64
                byte[] cipherBytes;
                try {
                    cipherBytes = Convert.FromBase64String(cipherText);
                } catch {
                    // Nếu không phải chuỗi Base64 hợp lệ, trả về chuỗi gốc
                    return cipherText;
                }
                
                // Giải mã AES
                using (Aes encryptor = Aes.Create())
                {
                    // Tạo key và IV từ passphrase
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(
                        _encryptionKey,
                        new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                    
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    
                    // Giải mã
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            try {
                                cs.Write(cipherBytes, 0, cipherBytes.Length);
                                cs.Close();
                                return Encoding.Unicode.GetString(ms.ToArray());
                            } catch {
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

        /// <summary>
        /// Mã hóa chuỗi để bảo vệ thông tin nhạy cảm
        /// </summary>
        /// <param name="plainText">Chuỗi cần mã hóa</param>
        /// <returns>Chuỗi đã mã hóa (Base64)</returns>
        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            
            try
            {
                // Nếu có vẻ đã được mã hóa (để tránh mã hóa kép)
                if (plainText.Contains("/") && plainText.Contains("="))
                {
                    return plainText;
                }
                
                // Chuyển đổi text thành bytes
                byte[] clearBytes = Encoding.Unicode.GetBytes(plainText);
                
                // Mã hóa AES
                using (Aes encryptor = Aes.Create())
                {
                    // Tạo key và IV từ passphrase
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(
                        _encryptionKey,
                        new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                    
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    
                    // Mã hóa
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
                return plainText; // Trả về chuỗi gốc nếu có lỗi
            }
        }

        /// <summary>
        /// Lấy thông tin đăng nhập đã giải mã
        /// </summary>
        /// <param name="encryptedUsername">Tên đăng nhập đã mã hóa</param>
        /// <param name="encryptedPassword">Mật khẩu đã mã hóa</param>
        /// <returns>Tuple chứa tên đăng nhập và mật khẩu đã giải mã</returns>
        public (string Username, string Password) GetDecryptedCredentials(string encryptedUsername, string encryptedPassword)
        {
            return (
                DecryptString(encryptedUsername),
                DecryptString(encryptedPassword)
            );
        }
    }
}