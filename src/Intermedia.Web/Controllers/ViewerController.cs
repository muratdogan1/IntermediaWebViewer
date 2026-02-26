using System.IO;
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

    // /Viewer?studyUid=...&file=xxx.dcm
    public IActionResult Index(string? studyUid = null, string? file = null)
    {
        var storage = Path.Combine(_env.ContentRootPath, "Storage");
        Directory.CreateDirectory(storage);

        // StudyUID -> StudyGroupVm
        var studyMap = new Dictionary<string, StudyGroupVm>(StringComparer.OrdinalIgnoreCase);

        foreach (var fullPath in Directory.EnumerateFiles(storage, "*.dcm", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(fileName))
                continue;

            try
            {
                // Header + dataset oku (basit yol)
                var dicomFile = DicomFile.Open(fullPath);
                var ds = dicomFile.Dataset;

                // PixelData yoksa görüntü değil -> atla (SR/PR/KO vb.)
                if (!ds.Contains(DicomTag.PixelData))
                    continue;

                var sUid = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");
                if (string.IsNullOrWhiteSpace(sUid))
                    continue;

                // İstersen sadece seçilen study'yi göster
                if (!string.IsNullOrWhiteSpace(studyUid) &&
                    !string.Equals(sUid, studyUid, StringComparison.OrdinalIgnoreCase))
                    continue;

                var seUid = ds.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "");
                if (string.IsNullOrWhiteSpace(seUid))
                    continue;

                var patientName = ds.GetSingleValueOrDefault(DicomTag.PatientName, "");
                var studyDate = ds.GetSingleValueOrDefault(DicomTag.StudyDate, "");
                var seriesDesc = ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, "");
                var modality = ds.GetSingleValueOrDefault(DicomTag.Modality, "");
                int? instanceNo = null;
                try
                {
                    var v = ds.GetSingleValueOrDefault(DicomTag.InstanceNumber, 0);
                    if (v > 0) instanceNo = v;
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
                    InstanceNumber = instanceNo
                });
            }
            catch
            {
                // bozuk/okunamayan dosyayı atla
            }
        }

        // Sıralama
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
                .ThenBy(x => x.SeriesInstanceUid, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var studies = studyMap.Values
            .OrderBy(x => x.PatientName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.StudyDate ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.StudyInstanceUid, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Selected dosyayı belirle
        string? selected = null;
        if (!string.IsNullOrWhiteSpace(file))
        {
            // file parametresi listede var mı kontrol
            var exists = studies.SelectMany(s => s.Series).SelectMany(s => s.Files)
                .Any(f => string.Equals(f.FileName, file, StringComparison.OrdinalIgnoreCase));
            if (exists) selected = file;
        }

        if (string.IsNullOrWhiteSpace(selected))
        {
            selected = studies.SelectMany(s => s.Series).SelectMany(s => s.Files)
                .Select(f => f.FileName)
                .FirstOrDefault();
        }

        var vm = new ViewerIndexVm
        {
            Studies = studies,
            Selected = selected,
            StorageFolder = storage
        };

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
            // PixelData yoksa render etmeyelim (kırılma olmasın)
            var ds = DicomFile.Open(fullPath).Dataset;
            if (!ds.Contains(DicomTag.PixelData))
                return StatusCode(415, "Bu dosyada PixelData yok (görüntü değil).");

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
        catch (DicomImagingException ex)
        {
            return StatusCode(415, "Render edilemedi: " + ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, "Hata: " + ex.Message);
        }
    }
}