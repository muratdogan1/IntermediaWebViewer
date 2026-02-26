using FellowOakDicom;
using FellowOakDicom.Network.Client;
using FellowOakDicom.Network;
using Intermedia.Core.Models;


namespace Intermedia.Dicom.Services;

public class DicomQueryService : IDicomQueryService
{
    public async Task<List<DicomStudy>> FindStudiesAsync(
        DicomServerSettings settings,
        string? patientName = null,
        string? patientId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var results = new List<DicomStudy>();

        // DicomClientFactory ile client oluştur
        var client = DicomClientFactory.Create(
            settings.Host,
            settings.Port,
            false,             // TLS kullanımı
            settings.LocalAeTitle,
            settings.AeTitle
        );

        // Study seviyesi C-FIND isteği
        var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);

        request.Dataset.AddOrUpdate(DicomTag.PatientName, patientName ?? "");
        request.Dataset.AddOrUpdate(DicomTag.PatientID, patientId ?? "");
        request.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, "");
        request.Dataset.AddOrUpdate(DicomTag.StudyDescription, "");
        request.Dataset.AddOrUpdate(DicomTag.Modality, "");
        request.Dataset.AddOrUpdate(DicomTag.StudyDate, "");

        request.OnResponseReceived += (rq, rp) =>
        {
            if (rp.Status == DicomStatus.Pending)
            {
                var ds = rp.Dataset;

                results.Add(new DicomStudy
                {
                    StudyInstanceUid = ds?.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "") ?? "",
                    PatientName = ds?.GetSingleValueOrDefault(DicomTag.PatientName, "") ?? "",
                    PatientId = ds?.GetSingleValueOrDefault(DicomTag.PatientID, "") ?? "",
                    StudyDescription = ds?.GetSingleValueOrDefault(DicomTag.StudyDescription, "") ?? "",
                    Modality = ds?.GetSingleValueOrDefault(DicomTag.Modality, "") ?? ""
                });
            }
        };

        await client.AddRequestAsync(request);
        await client.SendAsync();

        return results;
    }
}