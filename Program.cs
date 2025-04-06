using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamCmdWeb;
using SteamCmdWeb.Services;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

// Namespace của MiddlewareExtensions đã được định nghĩa trong SteamCmdWeb
// Không cần thêm using bổ sung nếu MiddlewareExtensions nằm trong cùng namespace

var builder = WebApplication.CreateBuilder(args);

// **Thêm các dịch vụ**
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

// **Cấu hình Authentication với Cookie**
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    });

// **Đăng ký các dịch vụ tùy chỉnh**
builder.Services.AddSingleton<DecryptionService>();
builder.Services.AddSingleton<AppProfileManager>();
builder.Services.AddSingleton<ProfileMigrationService>();
builder.Services.AddSingleton<SilentSyncService>();
builder.Services.AddSingleton<SystemMonitoringService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SystemMonitoringService>());
builder.Services.AddHostedService<TcpServerService>();

// **Thêm CORS policy**
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder => builder
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
});

// **Cấu hình logging**
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddEventLog();
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);
builder.Logging.AddFilter("SteamCmdWeb", LogLevel.Information);

var app = builder.Build();

// **Cấu hình pipeline HTTP**
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
app.UseAuthentication();
app.UseAuthorization();

// **Sử dụng middleware tùy chỉnh**
app.UseSilentSyncMiddleware(); // Middleware xử lý yêu cầu POST đến /api/silentsync
app.UseRemoteRequestLogging(); // Middleware ghi log các yêu cầu từ xa

app.MapControllers();
app.MapRazorPages();

// **Tạo các thư mục cần thiết**
var paths = new[]
{
    Path.Combine(app.Environment.ContentRootPath, "Data"),
    Path.Combine(app.Environment.ContentRootPath, "Profiles"),
    Path.Combine(app.Environment.ContentRootPath, "Data", "SilentSync"),
    Path.Combine(app.Environment.ContentRootPath, "Data", "Backup"),
    Path.Combine(app.Environment.ContentRootPath, "Data", "ClientSync"),
    Path.Combine(app.Environment.ContentRootPath, "Data", "Logs"),
    Path.Combine(app.Environment.ContentRootPath, "Data", "Monitoring")
};

foreach (var path in paths)
{
    if (!Directory.Exists(path))
    {
        Directory.CreateDirectory(path);
        app.Logger.LogInformation("Đã tạo thư mục: {Path}", path);
    }
}

var profilesFilePath = Path.Combine(paths[0], "profiles.json");
if (!File.Exists(profilesFilePath))
{
    File.WriteAllText(profilesFilePath, "[]");
    app.Logger.LogInformation("Đã tạo file profiles.json trống tại: {FilePath}", profilesFilePath);
}

// **Xử lý lỗi tổng quát**
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Lỗi không xử lý trong pipeline HTTP: {Message}", ex.Message);
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            var error = new { Error = "Lỗi server", Message = "Đã xảy ra lỗi khi xử lý yêu cầu." };
            await context.Response.WriteAsJsonAsync(error);
        }
    }
});

app.Logger.LogInformation("SteamCmdWeb Server đã khởi động.");
app.Run();