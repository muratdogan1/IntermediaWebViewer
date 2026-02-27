using FellowOakDicom;
using FellowOakDicom.Imaging;
using Intermedia.Dicom.Services;
using Intermedia.Web.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Auth config
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/Login";
        options.Cookie.Name = "Intermedia.Auth";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
    });

// Her yeri login zorunlu yap (Auth/Login hariç) -> Controller'da [AllowAnonymous]
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// DICOM servisleri
builder.Services.AddScoped<IDicomQueryService, DicomQueryService>();
builder.Services.AddScoped<IDicomMoveService, DicomMoveService>();

// fo-dicom imaging (ImageSharp)
builder.Services.AddFellowOakDicom()
    .AddImageManager<FellowOakDicom.Imaging.ImageSharpImageManager>();

// DicomServerSettings -> DI
var storeSettings = new DicomServerSettings
{
    Host = builder.Configuration["Dicom:PacsHost"] ?? "127.0.0.1",
    Port = int.TryParse(builder.Configuration["Dicom:PacsPort"], out var p) ? p : 104,
    AeTitle = builder.Configuration["Dicom:PacsAeTitle"] ?? "interMEDIAPacs",
    LocalAeTitle = builder.Configuration["Dicom:LocalAeTitle"] ?? "LOCALSTORAGE",
    LocalPort = int.TryParse(builder.Configuration["Dicom:StorePort"], out var lp) ? lp : 11112,
    StorageFolder = Path.Combine(
        builder.Environment.ContentRootPath,
        builder.Configuration["Dicom:StoragePath"] ?? "Storage"
    )
};
builder.Services.AddSingleton(storeSettings);

var app = builder.Build();

// IIS altında /WebViewer gibi virtual directory kullanıyorsan bunu aç:
// app.UsePathBase("/WebViewer");

DicomSetupBuilder.UseServiceProvider(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Studies}/{action=Index}/{id?}"
);

// ================================
// ✅ Storage SCP sadece izinliyse başlat
// ================================
var enableScpFromConfig = builder.Configuration.GetValue<bool?>("Dicom:EnableScp") ?? false;

// IIS'te güvenli varsayılan: Production'da SCP kapalı
var enableScp = app.Environment.IsDevelopment() && enableScpFromConfig;

if (enableScp)
{
    try
    {
        var scp = new StorageScpHosted(app.Services, storeSettings);
        scp.Start();
        Console.WriteLine($"Storage SCP çalışıyor → AE: {storeSettings.LocalAeTitle}, Port: {storeSettings.LocalPort}");
        Console.WriteLine($"Storage Folder: {storeSettings.StorageFolder}");
        app.Lifetime.ApplicationStopping.Register(() => scp.Stop());
    }
    catch (Exception ex)
    {
        Console.WriteLine("Storage SCP başlatılamadı: " + ex.Message);
    }
}
else
{
    Console.WriteLine("Storage SCP kapalı (IIS/Production için önerilen).");
}

app.Run();