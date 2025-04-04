using SteamCmdWeb.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamCmdWeb;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Thêm các dịch vụ
builder.Services.AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddRazorPages();

// Đăng ký các Service
builder.Services.AddSyncServices();

// Đăng ký dịch vụ giám sát hệ thống
builder.Services.AddSingleton<SystemMonitoringService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SystemMonitoringService>());

// Tăng kích thước tối đa cho request body và response streaming
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 50 * 1024 * 1024; // 50MB
    serverOptions.Limits.MaxRequestHeadersTotalSize = 32 * 1024; // 32KB
});

// Thêm CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", 
        builder => builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// Cấu hình logging
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddEventLog();

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
app.UseCors("AllowAll");
app.UseAuthorization();

// Đăng ký middleware tùy chỉnh
app.UseSilentSyncMiddleware();
app.UseRemoteRequestLogging();

// Map endpoints
app.MapControllers();
app.MapRazorPages();

// Tạo thư mục Data và các thư mục con nếu chưa tồn tại
var dataPath = Path.Combine(app.Environment.ContentRootPath, "Data");
var profilesPath = Path.Combine(app.Environment.ContentRootPath, "Profiles");
var silentSyncPath = Path.Combine(dataPath, "SilentSync");
var backupPath = Path.Combine(dataPath, "Backup");
var clientSyncPath = Path.Combine(dataPath, "ClientSync");
var logsPath = Path.Combine(dataPath, "Logs");

foreach (var path in new[] { dataPath, profilesPath, silentSyncPath, backupPath, clientSyncPath, logsPath })
{
    if (!Directory.Exists(path))
    {
        Directory.CreateDirectory(path);
        Console.WriteLine($"Created directory: {path}");
    }
}

// Ghi log khởi động
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("SteamCmdWeb Server started. Ready to receive silent sync from clients.");

// Chạy ứng dụng
app.Run();