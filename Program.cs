using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamCmdWeb.Services;
using SteamCmdWeb.Middleware;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Thêm cấu hình User Secrets
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

// Thêm các dịch vụ
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddRazorPages();

// Thêm HttpClient
builder.Services.AddHttpClient();

// Đăng ký các dịch vụ tùy chỉnh
builder.Services.AddSingleton<DecryptionService>();
builder.Services.AddSingleton<ProfileService>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddSingleton<ProfileMigrationService>();
builder.Services.AddSingleton<AppProfileManager>();
builder.Services.AddSingleton<SystemMonitoringService>();
builder.Services.AddSingleton<SteamAppService>();

// Thêm dịch vụ tracking client
builder.Services.AddSingleton<ClientTrackingService>();
builder.Services.AddHostedService<ClientInactivityService>();

// Đăng ký TcpServerService
builder.Services.AddHostedService<TcpServerService>();

// Thêm Memory Cache cho cải thiện hiệu suất
builder.Services.AddMemoryCache();

// Cấu hình logging
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);
builder.Logging.AddFilter("SteamCmdWeb", LogLevel.Information);

var app = builder.Build();

// Cấu hình pipeline HTTP
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Thêm middleware client tracking
app.UseClientTracking();

// Ánh xạ các endpoint
app.MapControllers();
app.MapRazorPages();

// Tạo các thư mục cần thiết
var dataFolder = Path.Combine(app.Environment.ContentRootPath, "Data");
if (!Directory.Exists(dataFolder))
{
    Directory.CreateDirectory(dataFolder);
    app.Logger.LogInformation("Đã tạo thư mục Data");
}

// Đảm bảo các thư mục cần thiết đều tồn tại
var profilesFilePath = Path.Combine(dataFolder, "profiles.json");
if (!File.Exists(profilesFilePath))
{
    File.WriteAllText(profilesFilePath, "[]");
    app.Logger.LogInformation("Đã tạo file profiles.json trống");
}

// Health Check endpoint
app.MapGet("/health", () => new { Status = "Healthy", Timestamp = DateTime.UtcNow });

app.Logger.LogInformation("SteamCmdWeb Server đã khởi động.");
app.Run();