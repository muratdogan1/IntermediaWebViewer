using Intermedia.Core.Models;

namespace Intermedia.Dicom.Services;

public interface IDicomMoveService
{
    Task<DicomMoveResult> MoveStudyAsync(DicomServerSettings settings, string studyInstanceUid);
}
