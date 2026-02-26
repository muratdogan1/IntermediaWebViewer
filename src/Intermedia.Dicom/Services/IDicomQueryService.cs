using Intermedia.Core.Models;

namespace Intermedia.Dicom.Services;

public interface IDicomQueryService
{
    Task<List<DicomStudy>> FindStudiesAsync(
        DicomServerSettings settings,
        string? patientName = null,
        string? patientId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null);
}