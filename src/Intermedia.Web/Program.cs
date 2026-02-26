using FellowOakDicom;
using Intermedia.Dicom.Services;
using FellowOakDicom.Imaging;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
    });

builder.Services.AddAuthorization();
// DICOM servisleri
builder.Services.AddScoped<IDicomQueryService, DicomQueryService>();
builder.Services.AddScoped<IDicomMoveService, DicomMoveService>();

// fo-dicom imaging (ImageSharp)
builder.Services.AddFellowOakDicom()
    .AddImageManager<ImageSharpImageManager>();

// Settings -> DI (Controller bunu alacak)
var storeSettings = new DicomServerSettings
{
    Host = "127.0.0.1",
    Port = 104,
    AeTitle = "interMEDIAPacs",
    LocalAeTitle = "LOCALSTORAGE",
    LocalPort = 11112,
    StorageFolder = Path.Combine(builder.Environment.ContentRootPath, "Storage")
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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Studies}/{action=Index}/{id?}");

app.Run();