using Intermedia.Dicom.Services;
using Microsoft.AspNetCore.Mvc;

namespace Intermedia.Web.Controllers;

public class StudiesController : Controller
{
    private readonly IDicomQueryService _query;
    private readonly IDicomMoveService _move;
    private readonly DicomServerSettings _settings;

    public StudiesController(IDicomQueryService query, IDicomMoveService move, DicomServerSettings settings)
    {
        _query = query;
        _move = move;
        _settings = settings;
    }

    // GET /Studies?patientId=...&patientName=...&fromDate=...&toDate=...
    [HttpGet]
    public async Task<IActionResult> Index(string? patientId = null, string? patientName = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        ViewBag.PatientId = patientId;
        ViewBag.PatientName = patientName;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        var studies = await _query.FindStudiesAsync(
            _settings,
            patientName: patientName,
            patientId: patientId,
            fromDate: fromDate,
            toDate: toDate
        );

        return View(studies);
    }

    // POST /Studies/Move
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(string studyUid)
    {
        if (string.IsNullOrWhiteSpace(studyUid))
        {
            TempData["err"] = "studyUid boş olamaz.";
            return RedirectToAction(nameof(Index));
        }

        await _move.MoveStudyAsync(_settings, studyUid);

        TempData["ok"] = "C-MOVE tamamlandı. Open ile Viewer'a geçebilirsin.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Studies/Open?studyUid=...
    [HttpGet]
    public IActionResult Open(string studyUid)
    {
        if (string.IsNullOrWhiteSpace(studyUid))
            return RedirectToAction(nameof(Index));

        // Viewer tarafında studyUid ile Storage içinden o study'yi göstereceğiz
        return RedirectToAction("Index", "Viewer", new { studyUid });
    }
}