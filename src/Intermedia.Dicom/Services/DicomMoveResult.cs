using FellowOakDicom.Network;

namespace Intermedia.Dicom.Services;

public sealed class DicomMoveResult
{
    public DicomStatus Status { get; init; } = DicomStatus.Pending;
    public int Remaining { get; init; }
    public int Completed { get; init; }
    public int Warnings { get; init; }
    public int Failures { get; init; }

    public string Summary =>
        $"{Status}; completed={Completed}, warnings={Warnings}, failures={Failures}, remaining={Remaining}";
}
