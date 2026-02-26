using System.Text;
using FellowOakDicom;
using FellowOakDicom.Network;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Intermedia.Dicom.Services;

public sealed class StorageScpHosted
{
    private readonly IServiceProvider _sp;
    private readonly DicomServerSettings _settings;
    private IDicomServer? _server;

    public StorageScpHosted(IServiceProvider sp, DicomServerSettings settings)
    {
        _sp = sp;
        _settings = settings;
    }

    public void Start()
    {
        Directory.CreateDirectory(_settings.StorageFolder);

        var factory = _sp.GetRequiredService<IDicomServerFactory>();
        _server = factory.Create<StorageScpService>(_settings.LocalPort);

        StorageScpService.StorageFolder = _settings.StorageFolder;
    }

    public void Stop()
    {
        _server?.Stop();
        _server?.Dispose();
        _server = null;
    }
}

public class StorageScpService : DicomService, IDicomServiceProvider, IDicomCStoreProvider
{
    public static string StorageFolder { get; set; } = "Storage";
    private readonly ILogger _logger;

    public StorageScpService(
        INetworkStream stream,
        Encoding fallbackEncoding,
        ILogger logger,
        DicomServiceDependencies dependencies)
        : base(stream, fallbackEncoding, logger, dependencies)
    {
        _logger = logger;
    }

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        foreach (var pc in association.PresentationContexts)
            pc.SetResult(DicomPresentationContextResult.Accept);

        return SendAssociationAcceptAsync(association);
    }

    public Task OnReceiveAssociationReleaseRequestAsync()
        => SendAssociationReleaseResponseAsync();

    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
        => _logger.LogWarning("Abort: {source} {reason}", source, reason);

    public void OnConnectionClosed(Exception exception)
        => _logger.LogInformation("Connection closed: {ex}", exception?.Message);

    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        Directory.CreateDirectory(StorageFolder);

        var sop = request.SOPInstanceUID?.UID ?? Guid.NewGuid().ToString("N");
        var fileName = Path.Combine(StorageFolder, sop + ".dcm");

        await request.File.SaveAsync(fileName);
        _logger.LogInformation("Saved: {file}", fileName);

        return new DicomCStoreResponse(request, DicomStatus.Success);
    }

    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e)
    {
        _logger.LogError(e, "C-STORE exception tempFile={temp}", tempFileName);
        return Task.CompletedTask;
    }
}