using FellowOakDicom;
using Intermedia.Dicom.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Intermedia.Web.Controllers;

[Authorize]
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

    [HttpGet]
    public async Task<IActionResult> Index(string? patientName = null, string? patientId = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        // İstersen burada ViewBag dolduruyorsun (sende vardı)
        ViewBag.PatientName = patientName;
        ViewBag.PatientId = patientId;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        var studies = await _query.FindStudiesAsync(_settings, patientName, patientId, fromDate, toDate);
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

    // ✅ Open: Storage'ta bu study yoksa otomatik C-MOVE yap, sonra Viewer'a geç
    [HttpGet]
    public async Task<IActionResult> Open(string studyUid)
    {
        if (string.IsNullOrWhiteSpace(studyUid))
        {
            TempData["err"] = "studyUid boş olamaz.";
            return RedirectToAction(nameof(Index));
        }

        // 1) Bu study için Storage'ta görüntülenebilir (PixelData'lı) en az 1 dosya var mı?
        var hasLocal = HasAnyRenderableInstanceInStorage(studyUid);

        // 2) Yoksa otomatik C-MOVE çalıştır
        if (!hasLocal)
        {
            await _move.MoveStudyAsync(_settings, studyUid);

            // 3) SCP dosyaları yazarken küçük bir süre gerekebilir -> kısa poll
            var timeoutMs = 12000;
            var stepMs = 400;

            for (var waited = 0; waited < timeoutMs; waited += stepMs)
            {
                if (HasAnyRenderableInstanceInStorage(studyUid))
                    break;

                await Task.Delay(stepMs);
            }
        }

        // 4) Viewer'a geç
        return RedirectToAction("Index", "Viewer", new { studyUid });
    }

    private bool HasAnyRenderableInstanceInStorage(string studyUid)
    {
        try
        {
            var storage = _settings.StorageFolder;
            if (string.IsNullOrWhiteSpace(storage) || !Directory.Exists(storage))
                return false;

            foreach (var fullPath in Directory.EnumerateFiles(storage, "*.dcm", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    // PixelData okumadan header oku (hızlı)
                    var df = DicomFile.Open(fullPath, FileReadOption.ReadLargeOnDemand);
                    var ds = df.Dataset;

                    var sUid = ds?.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "") ?? "";
                    if (!string.Equals(sUid, studyUid, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Render edilebilir mi? (PixelData var mı)
                    if (ds != null && ds.Contains(DicomTag.PixelData))
                        return true;
                }
                catch
                {
                    // bozuk/uyumsuz dosyayı es geç
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}