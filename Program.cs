using SteamCmdWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// Thêm các dịch vụ
builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddSingleton<AppProfileManager>();

// Thêm TCP Server như một background service
builder.Services.AddHostedService<TcpServerService>();

// Thêm CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", 
        builder => builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

var app = builder.Build();

// Cấu hình pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowAll");
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

// Tạo thư mục Data và Profiles nếu chưa tồn tại
var dataPath = Path.Combine(app.Environment.ContentRootPath, "Data");
var profilesPath = Path.Combine(app.Environment.ContentRootPath, "Profiles");

if (!Directory.Exists(dataPath))
{
    Directory.CreateDirectory(dataPath);
}

if (!Directory.Exists(profilesPath))
{
    Directory.CreateDirectory(profilesPath);
}

app.Run();