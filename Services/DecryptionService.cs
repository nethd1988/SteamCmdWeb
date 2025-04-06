using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Services
{
    public class DecryptionService
    {
        private readonly ILogger<DecryptionService> _logger;
        private readonly string _encryptionKey = "yourEncryptionKey123!@#"; // This should match the key in AppProfileManager

        public DecryptionService(ILogger<DecryptionService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Decrypts an encrypted string
        /// </summary>
        public string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";
            
            try
            {
                // Check if the string is a valid Base64 string
                try {
                    Convert.FromBase64String(cipherText);
                } catch {
                    // If not a valid Base64 string, return the original
                    return cipherText;
                }
                
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(_encryptionKey, 
                        new byte[] { 0x49, 0x76, 0x61, 0x6e, 0x20, 0x4d, 0x65, 0x64, 0x76, 0x65, 0x64, 0x65, 0x76 });
                    encryptor.Key = pdb.GetBytes(32);
                    encryptor.IV = pdb.GetBytes(16);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor.CreateDecryptor(), CryptoStreamMode.Write))
                        {
                            try {
                                cs.Write(cipherBytes, 0, cipherBytes.Length);
                                cs.Close();
                                return Encoding.Unicode.GetString(ms.ToArray());
                            } catch {
                                // If decryption fails, return the original string
                                return cipherText;
                            }
                        }
                    }
                }
            }
            catch
            {
                _logger?.LogWarning("Failed to decrypt string, returning original");
                return cipherText;
            }
        }

        /// <summary>
        /// Get clear text credentials for a profile
        /// </summary>
        public (string Username, string Password) GetDecryptedCredentials(string encryptedUsername, string encryptedPassword)
        {
            return (
                DecryptString(encryptedUsername),
                DecryptString(encryptedPassword)
            );
        }
        
        /// <summary>
        /// Encrypt a string to protect sensitive information
        /// </summary>
        public string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            
            try
            {
                byte[] clearBytes = Encoding.Unicode.GetBytes(plainText);
                using (Aes encryptor = Aes.Create())
                {
                    Rfc2898DeriveBytes pdb = new Rfc2898DeriveBytes(_encryptionKey, 
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
                _logger?.LogError(ex, "Error encrypting string");
                return plainText; // Return original text if encryption fails
            }
        }
    }
}