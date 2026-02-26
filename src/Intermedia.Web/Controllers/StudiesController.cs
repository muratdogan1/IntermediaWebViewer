using Intermedia.Dicom.Services;
using Microsoft.AspNetCore.Mvc;

namespace Intermedia.Web.Controllers;

public class StudiesController : Controller
{
    private readonly IDicomQueryService _query;
    private readonly IDicomMoveService _move;
    private readonly DicomServerSettings _settings;

    public StudiesController(
        IDicomQueryService query,
        IDicomMoveService move,
        DicomServerSettings settings)
    {
        _query = query;
        _move = move;
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var studies = await _query.FindStudiesAsync(_settings);
        return View(studies);
    }

    [HttpPost]
    public async Task<IActionResult> Move(string studyUid)
    {
        if (string.IsNullOrWhiteSpace(studyUid))
        {
            TempData["err"] = "studyUid boş olamaz.";
            return RedirectToAction(nameof(Index));
        }

        await _move.MoveStudyAsync(_settings, studyUid);

        TempData["ok"] = "C-MOVE tamamlandı. Viewer'dan açabilirsin.";
        return RedirectToAction(nameof(Index));
    }
}