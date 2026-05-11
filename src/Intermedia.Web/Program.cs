using FellowOakDicom;
using FellowOakDicom.Imaging;
using Intermedia.Dicom.Services;
using Intermedia.Web.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();

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

var localAeTitle = builder.Configuration["Dicom:LocalAeTitle"] ?? "LOCALSTORAGE";

// DicomServerSettings -> DI
var storeSettings = new DicomServerSettings
{
    Host = builder.Configuration["Dicom:PacsHost"] ?? "127.0.0.1",
    Port = int.TryParse(builder.Configuration["Dicom:PacsPort"], out var p) ? p : 104,
    AeTitle = builder.Configuration["Dicom:PacsAeTitle"] ?? "interMEDIAPacs",
    LocalAeTitle = localAeTitle,
    MoveDestinationAeTitle = builder.Configuration["Dicom:MoveDestinationAeTitle"] ?? localAeTitle,
    LocalPort = int.TryParse(builder.Configuration["Dicom:StorePort"], out var lp) ? lp : 11112,
    EnableScp = builder.Configuration.GetValue<bool?>("Dicom:EnableScp") ?? false,
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
// Storage SCP config ile acilir/kapanir.
// ================================

// Production varsayilani appsettings.json icinde kapali; gerekiyorsa EnableScp=true yap.

if (storeSettings.EnableScp)
{
    try
    {
        var scp = new StorageScpHosted(app.Services, storeSettings);
        scp.Start();
        Console.WriteLine($"Storage SCP calisiyor -> AE: {storeSettings.MoveDestinationAeTitle}, Port: {storeSettings.LocalPort}");
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
    Console.WriteLine("Storage SCP kapali (Dicom:EnableScp=false).");
}

app.Run();
