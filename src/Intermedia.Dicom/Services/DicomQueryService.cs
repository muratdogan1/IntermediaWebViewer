using System.Threading;
using FellowOakDicom;
using FellowOakDicom.Network.Client;
using FellowOakDicom.Network;
using Intermedia.Core.Models;


namespace Intermedia.Dicom.Services;

public class DicomQueryService : IDicomQueryService
{
    private const int LatestStudyCount = 3;
    private const int MaxRecentLookupDays = 30;

    public async Task<List<DicomStudy>> FindStudiesAsync(
        DicomServerSettings settings,
        string? patientName = null,
        string? patientId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var hasPatientFilter = !string.IsNullOrWhiteSpace(patientName) || !string.IsNullOrWhiteSpace(patientId);
        var hasDateFilter = fromDate.HasValue || toDate.HasValue;

        if (!hasPatientFilter && !hasDateFilter)
            return await FindLatestStudiesAsync(settings);

        var results = await FindStudiesCoreAsync(settings, patientName, patientId, fromDate, toDate);
        return TakeLatestStudies(results);
    }

    private static async Task<List<DicomStudy>> FindLatestStudiesAsync(DicomServerSettings settings)
    {
        var allResults = new Dictionary<string, DicomStudy>(StringComparer.OrdinalIgnoreCase);
        var today = DateTime.Today;

        for (var dayOffset = 0; dayOffset < MaxRecentLookupDays; dayOffset++)
        {
            var studyDate = today.AddDays(-dayOffset);
            var dayResults = await FindStudiesCoreAsync(settings, null, null, studyDate, studyDate);

            foreach (var study in dayResults)
            {
                if (!string.IsNullOrWhiteSpace(study.StudyInstanceUid))
                    allResults[study.StudyInstanceUid] = study;
            }

            if (allResults.Count >= LatestStudyCount)
                break;
        }

        return TakeLatestStudies(allResults.Values);
    }

    private static async Task<List<DicomStudy>> FindStudiesCoreAsync(
        DicomServerSettings settings,
        string? patientName,
        string? patientId,
        DateTime? fromDate,
        DateTime? toDate)
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
        request.Dataset.AddOrUpdate(DicomTag.StudyDate, BuildStudyDateQuery(fromDate, toDate));
        request.Dataset.AddOrUpdate(DicomTag.StudyTime, "");

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
                    StudyDate = ds?.GetSingleValueOrDefault(DicomTag.StudyDate, "") ?? "",
                    StudyTime = ds?.GetSingleValueOrDefault(DicomTag.StudyTime, "") ?? "",
                    Modality = ds?.GetSingleValueOrDefault(DicomTag.Modality, "") ?? ""
                });
            }
        };

        await client.AddRequestAsync(request);

        try
        {
            await client.SendAsync();
        }
        catch (Exception)
        {
            // Hata varsa yine boş liste dönebiliriz; UI'ya hata mesajı eklemek istersen.
        }

        return results;
    }

    private static List<DicomStudy> TakeLatestStudies(IEnumerable<DicomStudy> studies)
    {
        return studies
            .OrderByDescending(s => s.StudyDate ?? "")
            .ThenByDescending(s => s.StudyTime ?? "")
            .Take(LatestStudyCount)
            .ToList();
    }

    private static string BuildStudyDateQuery(DateTime? fromDate, DateTime? toDate)
    {
        var from = fromDate?.ToString("yyyyMMdd");
        var to = toDate?.ToString("yyyyMMdd");

        return (from, to) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{from}-{to}",
            ({ Length: > 0 }, _) => $"{from}-",
            (_, { Length: > 0 }) => $"-{to}",
            _ => ""
        };
    }
}
