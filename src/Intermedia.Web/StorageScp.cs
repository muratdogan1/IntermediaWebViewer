using System.Text;
using FellowOakDicom;
using FellowOakDicom.Network;
using Microsoft.Extensions.Logging;

public class StorageScp : DicomService, IDicomServiceProvider, IDicomCStoreProvider
{
    private readonly ILogger _logger;
    private readonly string _storagePath;

    public StorageScp(
        INetworkStream stream,
        Encoding fallbackEncoding,
        ILogger logger,
        DicomServiceDependencies dependencies,
        string storagePath)
        : base(stream, fallbackEncoding, logger, dependencies)
    {
        _logger = logger;
        _storagePath = storagePath;
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
        => _logger.LogWarning("Abort: {Source} {Reason}", source, reason);

    public void OnConnectionClosed(Exception exception)
        => _logger.LogInformation("Connection closed: {Message}", exception?.Message);

    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        Directory.CreateDirectory(_storagePath);

        var sop = request.SOPInstanceUID?.UID ?? Guid.NewGuid().ToString("N");
        var fileName = Path.Combine(_storagePath, sop + ".dcm");

        await request.File.SaveAsync(fileName);

        _logger.LogInformation("Saved: {File}", fileName);
        return new DicomCStoreResponse(request, DicomStatus.Success);
    }

    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e)
    {
        _logger.LogError(e, "C-STORE exception: {Temp}", tempFileName);
        return Task.CompletedTask;
    }
}