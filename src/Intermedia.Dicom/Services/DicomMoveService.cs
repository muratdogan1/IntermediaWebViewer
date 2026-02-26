using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using Intermedia.Core.Models;

namespace Intermedia.Dicom.Services;

public class DicomMoveService : IDicomMoveService
{
    public async Task MoveStudyAsync(DicomServerSettings settings, string studyInstanceUid)
    {
        var client = DicomClientFactory.Create(
            settings.Host,
            settings.Port,
            false,
            settings.LocalAeTitle,
            settings.AeTitle);

        // Destination AE: Storage SCP'nin AE Title'ı
        var req = new DicomCMoveRequest(settings.MoveDestinationAeTitle, studyInstanceUid);

        await client.AddRequestAsync(req);
        await client.SendAsync();
    }
}