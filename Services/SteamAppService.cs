using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SteamCmdWeb.Services
{
    public class SteamAppService
    {
        private readonly ILogger<SteamAppService> _logger;
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, string> _appNameCache = new Dictionary<string, string>();

        public SteamAppService(ILogger<SteamAppService> logger, HttpClient httpClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<string> GetAppNameAsync(string appId)
        {
            try
            {
                // Kiểm tra cache trước
                if (_appNameCache.TryGetValue(appId, out string cachedName))
                {
                    return cachedName;
                }

                // Gọi API Steam để lấy thông tin game
                string url = $"https://store.steampowered.com/api/appdetails?appids={appId}";
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    
                    using (JsonDocument document = JsonDocument.Parse(content))
                    {
                        if (document.RootElement.TryGetProperty(appId, out JsonElement appElement) &&
                            appElement.TryGetProperty("success", out JsonElement successElement) &&
                            successElement.GetBoolean() &&
                            appElement.TryGetProperty("data", out JsonElement dataElement) &&
                            dataElement.TryGetProperty("name", out JsonElement nameElement))
                        {
                            string appName = nameElement.GetString();
                            // Lưu vào cache
                            _appNameCache[appId] = appName;
                            return appName;
                        }
                    }
                }
                
                // Nếu không tìm thấy, trả về chính appId
                return appId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy tên game cho AppID {AppId}", appId);
                return appId;
            }
        }
    }
} 