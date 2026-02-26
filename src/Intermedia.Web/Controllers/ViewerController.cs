using System.IO;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using Intermedia.Web.Models;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Microsoft.AspNetCore.Authorization;

namespace Intermedia.Web.Controllers;

[Authorize]
public class ViewerController : Controller
{
    private readonly IWebHostEnvironment _env;

    public ViewerController(IWebHostEnvironment env)
    {
        _env = env;
    }

    // /Viewer?studyUid=...&file=relative/path.dcm
    [HttpGet]
    public IActionResult Index(string? studyUid = null, string? file = null)
    {
        var storage = Path.Combine(_env.ContentRootPath, "Storage");
        Directory.CreateDirectory(storage);

        // Storage altındaki tüm .dcm dosyalarını (alt klasörler dahil) tara
        var allDicomFiles = Directory
            .EnumerateFiles(storage, "*.dcm", SearchOption.AllDirectories)
            .ToList();

        // Study/Series gruplama için meta oku (hatalı/PixelData olmayanlar da olabilir)
        var studies = new List<StudyGroupVm>();

        foreach (var fullPath in allDicomFiles)
        {
            try
            {
                // sadece header okumaya çalış (hafif). fo-dicom: FileReadOption.ReadLargeOnDemand her sürümde var.
                // Eğer sende farklıysa: DicomFile.Open(fullPath) da çalışır ama daha ağırdır.
                var dicomFile = DicomFile.Open(fullPath, FileReadOption.ReadLargeOnDemand);
                var ds = dicomFile.Dataset;

                var suid = ds.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");
                var serUid = ds.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "");
                if (string.IsNullOrWhiteSpace(suid) || string.IsNullOrWhiteSpace(serUid))
                    continue;

                // Eğer URL ile studyUid geldiyse sadece onu göster
                if (!string.IsNullOrWhiteSpace(studyUid) &&
                    !string.Equals(studyUid, suid, StringComparison.Ordinal))
                    continue;

                var patientName = ds.GetSingleValueOrDefault(DicomTag.PatientName, "");
                var studyDate = ds.GetSingleValueOrDefault(DicomTag.StudyDate, "");
                var seriesDesc = ds.GetSingleValueOrDefault(DicomTag.SeriesDescription, "");
                var modality = ds.GetSingleValueOrDefault(DicomTag.Modality, "");
                int? instanceNumber = null;
                if (ds.TryGetSingleValue(DicomTag.InstanceNumber, out int inst))
                    instanceNumber = inst;

                // storage'a göre relative path tut (Viewer/Render bunu kullanacak)
                var rel = Path.GetRelativePath(storage, fullPath);
                // URL'de daha temiz olsun diye / kullan
                rel = rel.Replace('\\', '/');

                var st = studies.FirstOrDefault(x => x.StudyInstanceUid == suid);
                if (st == null)
                {
                    st = new StudyGroupVm
                    {
                        StudyInstanceUid = suid,
                        PatientName = patientName,
                        StudyDate = studyDate
                    };
                    studies.Add(st);
                }

                var se = st.Series.FirstOrDefault(x => x.SeriesInstanceUid == serUid);
                if (se == null)
                {
                    se = new SeriesGroupVm
                    {
                        SeriesInstanceUid = serUid,
                        SeriesDescription = seriesDesc,
                        Modality = modality
                    };
                    st.Series.Add(se);
                }

                se.Files.Add(new FileItemVm
                {
                    FileName = rel,
                    InstanceNumber = instanceNumber
                });
            }
            catch
            {
                // okunamayan dosyayı atla
            }
        }

        // Sıralama
        foreach (var st in studies)
        {
            foreach (var se in st.Series)
            {
                se.Files = se.Files
                    .OrderBy(x => x.InstanceNumber ?? int.MaxValue)
                    .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            st.Series = st.Series
                .OrderBy(x => x.Modality)
                .ThenBy(x => x.SeriesDescription)
                .ToList();
        }

        studies = studies
            .OrderByDescending(x => x.StudyDate)
            .ThenBy(x => x.PatientName)
            .ToList();

        // Selected belirle:
        // 1) URL'den file geldiyse onu seç
        // 2) yoksa ilk bulunan dosyayı seç
        string? selected = null;

        if (!string.IsNullOrWhiteSpace(file))
        {
            // normalize
            var wanted = file.Replace('\\', '/');
            if (studies.SelectMany(s => s.Series).SelectMany(s => s.Files)
                .Any(f => string.Equals(f.FileName, wanted, StringComparison.OrdinalIgnoreCase)))
            {
                selected = wanted;
            }
        }

        if (selected == null)
        {
            selected = studies
                .SelectMany(s => s.Series)
                .SelectMany(s => s.Files)
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

    // /Viewer/Render?file=relative/path.dcm&w=160
    [HttpGet]
    public IActionResult Render(string file, int? w = null)
    {
        if (string.IsNullOrWhiteSpace(file))
            return BadRequest("file boş olamaz.");

        var storage = Path.Combine(_env.ContentRootPath, "Storage");
        Directory.CreateDirectory(storage);

        // URL'den gelen "a/b/c.dcm" -> OS path
        var rel = file.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

        // Güvenlik: Storage dışına çıkmayı engelle
        var fullPath = Path.GetFullPath(Path.Combine(storage, rel));
        var storageFull = Path.GetFullPath(storage);

        if (!fullPath.StartsWith(storageFull, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Geçersiz dosya yolu.");

        if (!System.IO.File.Exists(fullPath))
            return NotFound("Dosya bulunamadı: " + file);

        // PixelData yoksa DicomImage patlar; bunu yakalayıp mesaj döndürelim
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
        catch (DicomImagingException)
        {
            return BadRequest("Seçilen DICOM görüntü değil (PixelData yok).");
        }
    }
}