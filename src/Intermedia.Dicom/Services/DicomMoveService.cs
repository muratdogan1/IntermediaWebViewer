using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;

namespace Intermedia.Dicom.Services;

public class DicomMoveService : IDicomMoveService
{
    public async Task<DicomMoveResult> MoveStudyAsync(DicomServerSettings settings, string studyInstanceUid)
    {
        var client = DicomClientFactory.Create(
            settings.Host,
            settings.Port,
            false,
            settings.LocalAeTitle,
            settings.AeTitle);

        // Destination AE: Storage SCP'nin AE Title'ı
        var req = new DicomCMoveRequest(settings.MoveDestinationAeTitle, studyInstanceUid);
        DicomMoveResult? finalResult = null;
        Exception? sendException = null;

        req.OnResponseReceived += (_, response) =>
        {
            if (response.Status != DicomStatus.Pending)
            {
                finalResult = new DicomMoveResult
                {
                    Status = response.Status,
                    Remaining = response.Remaining,
                    Completed = response.Completed,
                    Warnings = response.Warnings,
                    Failures = response.Failures
                };
            }
        };

        await client.AddRequestAsync(req);
        try
        {
            await client.SendAsync();
        }
        catch (Exception ex)
        {
            sendException = ex;
        }

        if (finalResult == null)
            throw new InvalidOperationException("C-MOVE yaniti alinamadi.", sendException);

        if (finalResult.Status.State is DicomState.Success or DicomState.Warning)
        {
            if (finalResult.Completed > 0)
                return finalResult;

            throw new InvalidOperationException("C-MOVE tamamlandi ama PACS hic instance gondermedi: " + finalResult.Summary, sendException);
        }

        throw new InvalidOperationException("C-MOVE statusu basarisiz: " + finalResult.Summary, sendException);
    }
}
