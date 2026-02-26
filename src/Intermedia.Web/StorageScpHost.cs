using FellowOakDicom.Network;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public interface IStorageScpHost
{
    void Start();
}

public class StorageScpHost : IStorageScpHost
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _cfg;
    private IDicomServer? _server;

    public StorageScpHost(IServiceProvider sp, IConfiguration cfg)
    {
        _sp = sp;
        _cfg = cfg;
    }

    public void Start()
    {
        if (_server != null) return;

        var port = _cfg.GetValue<int>("Dicom:StorePort");
        var storagePath = _cfg.GetValue<string>("Dicom:StoragePath") ?? "Storage";

        var loggerFactory = _sp.GetRequiredService<ILoggerFactory>();
        var dicomServerFactory = _sp.GetRequiredService<IDicomServerFactory>();

        // StorageScp’e storagePath geçmek için factory override:
        _server = dicomServerFactory.Create<StorageScp>(port, userState: storagePath);

        var log = loggerFactory.CreateLogger<StorageScpHost>();
        log.LogInformation("Storage SCP started on port {Port}, path={Path}", port, storagePath);
    }
}