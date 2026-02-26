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
    });

// Her yeri login zorunlu yap (Auth/Login hariç)
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
    StorageFolder = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Dicom:StoragePath"] ?? "Storage")
};
builder.Services.AddSingleton(storeSettings);

var app = builder.Build();

// fo-dicom için gerekli
DicomSetupBuilder.UseServiceProvider(app.Services);

// Storage SCP başlat
var scp = new StorageScpHosted(app.Services, storeSettings);
scp.Start();
Console.WriteLine($"Storage SCP çalışıyor → AE: {storeSettings.LocalAeTitle}, Port: {storeSettings.LocalPort}");
Console.WriteLine($"Storage Folder: {storeSettings.StorageFolder}");
app.Lifetime.ApplicationStopping.Register(() => scp.Stop());

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
    pattern: "{controller=Studies}/{action=Index}/{id?}");

app.Run();