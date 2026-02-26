using Intermedia.Core.Models;

namespace Intermedia.Dicom.Services;

public interface IDicomMoveService
{
    Task MoveStudyAsync(DicomServerSettings settings, string studyInstanceUid);
}