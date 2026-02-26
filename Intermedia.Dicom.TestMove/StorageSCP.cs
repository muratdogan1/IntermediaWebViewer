using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FellowOakDicom;
using FellowOakDicom.Network;
using Microsoft.Extensions.Logging;

public class StorageSCP : DicomService, IDicomServiceProvider, IDicomCStoreProvider
{
    private readonly ILogger _logger;

    // ✅ fo-dicom 5.2.5: 4 parametreli base ctor
    public StorageSCP(
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
        {
            if (pc.AbstractSyntax.StorageCategory != DicomStorageCategory.None)
                pc.SetResult(DicomPresentationContextResult.Accept);
        }

        return SendAssociationAcceptAsync(association);
    }

    public Task OnReceiveAssociationReleaseRequestAsync()
        => SendAssociationReleaseResponseAsync();

    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
        => _logger?.LogInformation("Abort: {Source} {Reason}", source, reason);

    public void OnConnectionClosed(Exception exception)
        => _logger?.LogInformation("Connection closed: {Message}", exception?.Message);

    public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
    {
        Directory.CreateDirectory("Storage");
        var fileName = Path.Combine("Storage", request.SOPInstanceUID.UID + ".dcm");

        await request.File.SaveAsync(fileName);

        Console.WriteLine($"Kaydedildi: {fileName}");
        return new DicomCStoreResponse(request, DicomStatus.Success);
    }

    public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e)
    {
        Console.WriteLine($"C-STORE Hata: {e.Message}");
        return Task.CompletedTask;
    }
}