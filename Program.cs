using SteamCmdWeb.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SteamCmdWeb;
using System.IO;
using System.Text.Json.Serialization;
using System.Text.Json;
using System;

var builder = WebApplication.CreateBuilder(args);

// Thêm các dịch vụ
builder.Services.AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // Đảm bảo không xảy ra vấn đề với tham chiếu vòng tròn
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddRazorPages();

// Đăng ký các Service theo thứ tự phụ thuộc
builder.Services.AddSingleton<DecryptionService>();
builder.Services.AddSingleton<AppProfileManager>();
builder.Services.AddSingleton<ProfileMigrationService>();
builder.Services.AddSingleton<SilentSyncService>();

// Đăng ký dịch vụ giám sát hệ thống
builder.Services.AddSingleton<SystemMonitoringService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SystemMonitoringService>());

// Đăng ký dịch vụ TCP server
builder.Services.AddHostedService<TcpServerService>();

// Tăng kích thước tối đa cho request body và response streaming
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 50 * 1024 * 1024; // 50MB
    serverOptions.Limits.MaxRequestHeadersTotalSize = 32 * 1024; // 32KB
    serverOptions.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10); // Tăng timeout cho kết nối kéo dài
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(2); // Thời gian chờ headers
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

// Cấu hình logging với bộ lọc
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddEventLog();

// Lọc bớt logs để tránh spam
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
app.UseCors("AllowAll");
app.UseAuthorization();

// Đăng ký middleware tùy chỉnh
app.UseSilentSyncMiddleware();
app.UseRemoteRequestLogging();

// Map endpoints
app.MapControllers();
app.MapRazorPages();

// Tạo thư mục Data và các thư mục con nếu chưa tồn tại
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

// Kiểm tra và tạo file profiles.json nếu chưa tồn tại
var profilesFilePath = Path.Combine(paths[0], "profiles.json");
if (!File.Exists(profilesFilePath))
{
    File.WriteAllText(profilesFilePath, "[]");
    app.Logger.LogInformation("Đã tạo file profiles.json trống tại: {FilePath}", profilesFilePath);
}

// Thêm xử lý lỗi tổng quát
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

            var error = new
            {
                Error = "Lỗi server",
                Message = "Đã xảy ra lỗi khi xử lý yêu cầu. Vui lòng thử lại sau."
            };

            await context.Response.WriteAsJsonAsync(error);
        }
    }
});

// Ghi log khởi động
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("SteamCmdWeb Server đã khởi động. Sẵn sàng nhận silent sync từ clients.");
logger.LogInformation("Ứng dụng đang chạy tại {Url}", app.Configuration["Kestrel:Endpoints:Http:Url"] ?? "http://localhost:5000");

// Chạy ứng dụng
app.Run();