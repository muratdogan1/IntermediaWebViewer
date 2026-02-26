using System;
using System.Threading.Tasks;
using FellowOakDicom;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Server;

class Program
{
    static async Task Main(string[] args)
    {
        // Local AE Title
        string localAe = "TESTCLIENT"; // PACS'te destination olarak ekle
        int port = 11113; // boş bir port seçin, ör: 11113

        var server = DicomServer.Create<MoveScpService>(port);

        Console.WriteLine($"DICOM SCP running on port {port} with AE Title {localAe}");
        Console.WriteLine("Press Enter to stop.");
        Console.ReadLine();

        server.Dispose();
    }
}

// Basit Move SCP Service
class MoveScpService : DicomService, IDicomServiceProvider, IDicomCMoveProvider
{
    public MoveScpService(INetworkStream stream, Encoding fallbackEncoding, ILogger logger, DicomServiceDependencies dependencies)
        : base(stream, fallbackEncoding, logger, dependencies) { }

    public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason) { }
    public void OnConnectionClosed(Exception exception) { }

    public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
    {
        foreach (var pc in association.PresentationContexts)
            pc.SetResult(DicomPresentationContextResult.Accept);
        return SendAssociationAcceptAsync(association);
    }

    public Task OnReceiveAssociationReleaseRequestAsync()
    {
        return Task.CompletedTask;
    }

    // C-MOVE geldiğinde çalışacak
    public Task<DicomCMoveResponse> OnCMoveRequestAsync(DicomCMoveRequest request)
    {
        Console.WriteLine($"Received C-MOVE request for Study UID: {request.StudyInstanceUID}");
        // Burada gerçek dosya gönderme yok, sadece test için Success dönüyoruz
        return Task.FromResult(new DicomCMoveResponse(request, DicomStatus.Success));
    }
}