namespace Intermedia.Web.Models;

public sealed class ViewerIndexVm
{
    public List<StudyGroupVm> Studies { get; set; } = new();
    public string? Selected { get; set; }
    public string StorageFolder { get; set; } = "";
}

public sealed class StudyGroupVm
{
    public string StudyInstanceUid { get; set; } = "";
    public string? PatientName { get; set; }
    public string? StudyDate { get; set; }
    public List<SeriesGroupVm> Series { get; set; } = new();
}

public sealed class SeriesGroupVm
{
    public string SeriesInstanceUid { get; set; } = "";
    public string? SeriesDescription { get; set; }
    public string? Modality { get; set; }
    public List<FileItemVm> Files { get; set; } = new();
}

public sealed class FileItemVm
{
    public string FileName { get; set; } = "";
    public int? InstanceNumber { get; set; }
}