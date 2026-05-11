using FellowOakDicom;
using Intermedia.Core.Models;
using Intermedia.Dicom.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace Intermedia.Web.Controllers;

[Authorize]
public class StudiesController : Controller
{
    private readonly IDicomQueryService _query;
    private readonly IDicomMoveService _move;
    private readonly DicomServerSettings _settings;
    private readonly IMemoryCache _cache;

    public StudiesController(IDicomQueryService query, IDicomMoveService move, DicomServerSettings settings, IMemoryCache cache)
    {
        _query = query;
        _move = move;
        _settings = settings;
        _cache = cache;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? patientName = null, string? patientId = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        // İstersen burada ViewBag dolduruyorsun (sende vardı)
        ViewBag.PatientName = patientName;
        ViewBag.PatientId = patientId;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        var cacheKey = BuildStudiesCacheKey(patientName, patientId, fromDate, toDate);
        var studies = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await _query.FindStudiesAsync(_settings, patientName, patientId, fromDate, toDate);
        }) ?? new List<DicomStudy>();

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

        try
        {
            var moveResult = await _move.MoveStudyAsync(_settings, studyUid);
            TempData["ok"] = "C-MOVE tamamlandi: " + moveResult.Summary;
        }
        catch (Exception ex)
        {
            TempData["err"] = "C-MOVE basarisiz: " + ex.Message;
            return RedirectToAction(nameof(Index));
        }

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
            if (!_settings.EnableScp)
            {
                TempData["err"] = "Bu study lokal Storage'ta yok. Otomatik C-MOVE icin Storage SCP kapali; appsettings.json icinde Dicom:EnableScp=true yap veya once C-MOVE/Store ile dosyalari indir.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                await _move.MoveStudyAsync(_settings, studyUid);
            }
            catch (Exception ex)
            {
                TempData["err"] = "C-MOVE basarisiz: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }

            // 3) SCP dosyaları yazarken küçük bir süre gerekebilir -> kısa poll
            var timeoutMs = 12000;
            var stepMs = 400;

            for (var waited = 0; waited < timeoutMs; waited += stepMs)
            {
                if (HasAnyRenderableInstanceInStorage(studyUid))
                    break;

                await Task.Delay(stepMs);
            }

            if (!HasAnyRenderableInstanceInStorage(studyUid))
            {
                var localInstances = CountStudyInstancesInStorage(studyUid);
                TempData["err"] = localInstances > 0
                    ? $"C-MOVE sonrasi {localInstances} DICOM dosyasi bulundu ama PixelData yok; bu study goruntulenebilir instance icermiyor olabilir."
                    : $"C-MOVE tamamlandi fakat Storage icinde bu study icin dosya bulunamadi. PACS tarafinda Move Destination AE '{_settings.MoveDestinationAeTitle}' -> bu uygulamanin IP:{_settings.LocalPort} portuna tanimli olmali ve firewall bu portu acmali.";
                return RedirectToAction(nameof(Index));
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

            var searchRoot = GetStudySearchRoot(storage, studyUid);
            foreach (var fullPath in Directory.EnumerateFiles(searchRoot, "*.dcm", SearchOption.AllDirectories))
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

    private int CountStudyInstancesInStorage(string studyUid)
    {
        try
        {
            var storage = _settings.StorageFolder;
            if (string.IsNullOrWhiteSpace(storage) || !Directory.Exists(storage))
                return 0;

            var searchRoot = GetStudySearchRoot(storage, studyUid);
            var count = 0;
            foreach (var fullPath in Directory.EnumerateFiles(searchRoot, "*.dcm", SearchOption.AllDirectories))
            {
                try
                {
                    var df = DicomFile.Open(fullPath, FileReadOption.ReadLargeOnDemand);
                    var sUid = df.Dataset?.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "") ?? "";
                    if (string.Equals(sUid, studyUid, StringComparison.OrdinalIgnoreCase))
                        count++;
                }
                catch
                {
                    // Ignore unreadable local files while producing a user-facing diagnostic.
                }
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static string BuildStudiesCacheKey(string? patientName, string? patientId, DateTime? fromDate, DateTime? toDate)
    {
            return string.Join("|",
            "studies",
            patientName?.Trim() ?? "",
            patientId?.Trim() ?? "",
            fromDate?.ToString("yyyyMMdd") ?? "",
            toDate?.ToString("yyyyMMdd") ?? "");
    }

    private static string GetStudySearchRoot(string storage, string studyUid)
    {
        var studyFolder = Path.Combine(storage, SanitizePathPart(studyUid));
        return Directory.Exists(studyFolder) ? studyFolder : storage;
    }

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
