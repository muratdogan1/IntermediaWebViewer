namespace Intermedia.Core.Models;

public class DicomStudy
{
    public string StudyInstanceUid { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string StudyDescription { get; set; } = string.Empty;
    public string Modality { get; set; } = string.Empty;
}
