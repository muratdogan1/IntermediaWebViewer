using System.Globalization;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using Intermedia.Web.Models;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace Intermedia.Web.Controllers;

public class ViewerController : Controller
{
    private readonly IWebHostEnvironment _env;

    public ViewerController(IWebHostEnvironment env)
    {
        _env = env;
    }

    // /Viewer?studyUid=...&file=...
    [HttpGet]
    public IActionResult Index(string? studyUid = null, string? file = null)
    {
        var storage = Path.Combine(_env.ContentRootPath, "Storage");
        Directory.CreateDirectory(storage);

        var dicomFiles = Directory.EnumerateFiles(storage, "*.dcm", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // StudyUID -> StudyGroupVm
        var studyMap = new Dictionary<string, StudyGroupVm>(StringComparer.OrdinalIgnoreCase);

        foreach (var fullPath in dicomFiles)
        {
            var fileName = Path.GetFileName(fullPath);

            // Büyük tag'ları (PixelData) okumadan sadece header bilgilerini çek
            DicomDataset? ds = null;
            try
            {
                var df = DicomFile.Open(fullPath, FileReadOption.ReadLargeOnDemand);
                ds = df.Dataset;
            }
            catch
            {
                // Bazı dosyalar part10/meta uyumsuz olabilir.
                // Yine de listede görünsün diye Unknown'a koyacağız.
            }

            var sUid = ds?.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "") ?? "";
            var seUid = ds?.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "") ?? "";

            if (string.IsNullOrWhiteSpace(sUid)) sUid = "UNKNOWN-STUDY";
            if (string.IsNullOrWhiteSpace(seUid)) seUid = "UNKNOWN-SERIES";

            var patientName = ds?.GetSingleValueOrDefault(DicomTag.PatientName, "") ?? "";
            var studyDateRaw = ds?.GetSingleValueOrDefault(DicomTag.StudyDate, "") ?? "";
            var studyDate = NormalizeDicomDate(studyDateRaw);

            var seriesDesc = ds?.GetSingleValueOrDefault(DicomTag.SeriesDescription, "") ?? "";
            var modality = ds?.GetSingleValueOrDefault(DicomTag.Modality, "") ?? "";

            int? instanceNumber = null;
            try
            {
                instanceNumber = ds?.GetSingleValueOrDefault(DicomTag.InstanceNumber, 0);
                if (instanceNumber == 0) instanceNumber = null;
            }
            catch { /* ignore */ }

            if (!studyMap.TryGetValue(sUid, out var study))
            {
                study = new StudyGroupVm
                {
                    StudyInstanceUid = sUid,
                    PatientName = string.IsNullOrWhiteSpace(patientName) ? null : patientName,
                    StudyDate = string.IsNullOrWhiteSpace(studyDate) ? null : studyDate
                };
                studyMap[sUid] = study;
            }

            var series = study.Series.FirstOrDefault(x =>
                string.Equals(x.SeriesInstanceUid, seUid, StringComparison.OrdinalIgnoreCase));

            if (series == null)
            {
                series = new SeriesGroupVm
                {
                    SeriesInstanceUid = seUid,
                    SeriesDescription = string.IsNullOrWhiteSpace(seriesDesc) ? null : seriesDesc,
                    Modality = string.IsNullOrWhiteSpace(modality) ? null : modality
                };
                study.Series.Add(series);
            }

            series.Files.Add(new FileItemVm
            {
                FileName = fileName,
                InstanceNumber = instanceNumber
            });
        }

        // Dosyaları InstanceNumber'a göre sırala
        foreach (var st in studyMap.Values)
        {
            foreach (var se in st.Series)
            {
                se.Files = se.Files
                    .OrderBy(x => x.InstanceNumber ?? int.MaxValue)
                    .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            st.Series = st.Series
                .OrderBy(x => x.Modality ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.SeriesDescription ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var allStudies = studyMap.Values
            .OrderByDescending(x => x.StudyDate ?? "")
            .ThenBy(x => x.PatientName ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Eğer studyUid param geldiyse, sadece onu göster (bulunamazsa fallback: hepsini göster)
        List<StudyGroupVm> visibleStudies = allStudies;
        if (!string.IsNullOrWhiteSpace(studyUid))
        {
            var filtered = allStudies
                .Where(x => string.Equals(x.StudyInstanceUid, studyUid, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (filtered.Count > 0)
                visibleStudies = filtered;
        }

        // Seçili dosya belirle
        string? selected = null;

        if (!string.IsNullOrWhiteSpace(file))
        {
            // file doğrudan seçildiyse
            var exists = dicomFiles.Any(p => string.Equals(Path.GetFileName(p), file, StringComparison.OrdinalIgnoreCase));
            if (exists) selected = file;
        }

        if (selected == null)
        {
            // studyUid varsa önce o study içinden ilk dosyayı seç
            selected = visibleStudies
                .SelectMany(s => s.Series)
                .SelectMany(se => se.Files)
                .Select(f => f.FileName)
                .FirstOrDefault();

            // hala yoksa (hiç dosya yoksa) null kalır
        }

        var vm = new ViewerIndexVm
        {
            Studies = visibleStudies,
            Selected = selected,
            StorageFolder = storage
        };

        // View içinde Open linkleri için studyUid'yi de göstermek istersen lazım olabilir
        ViewBag.StudyUid = studyUid;

        return View(vm);
    }

    // /Viewer/Render?file=xxx.dcm&w=160
    [HttpGet]
    public IActionResult Render(string file, int? w = null)
    {
        if (string.IsNullOrWhiteSpace(file))
            return BadRequest("file boş olamaz.");

        var storage = Path.Combine(_env.ContentRootPath, "Storage");
        var fullPath = Path.Combine(storage, file);

        if (!System.IO.File.Exists(fullPath))
            return NotFound("Dosya bulunamadı: " + file);

        try
        {
            var dicomImage = new DicomImage(fullPath);

            using var rendered = dicomImage.RenderImage();
            using Image sharp = rendered.AsSharpImage();

            if (w.HasValue && w.Value > 0)
            {
                var targetW = Math.Clamp(w.Value, 32, 1024);
                sharp.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(targetW, 0)
                }));
            }

            using var ms = new MemoryStream();
            sharp.Save(ms, new PngEncoder());
            return File(ms.ToArray(), "image/png");
        }
        catch
        {
            // SR/PR gibi pixel içermeyen veya render edilemeyen dosyalar:
            // thumbnail kırılmasın diye "No Content" dönelim.
            return NoContent();
        }
    }

    private static string? NormalizeDicomDate(string? dicomDate)
    {
        if (string.IsNullOrWhiteSpace(dicomDate)) return null;

        // DICOM date: YYYYMMDD
        if (DateTime.TryParseExact(dicomDate, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
        {
            return dt.ToString("yyyy-MM-dd");
        }

        return dicomDate;
    }
}


