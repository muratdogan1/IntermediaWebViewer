using FellowOakDicom;
using FellowOakDicom.Imaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;

namespace Intermedia.Web.Controllers;

[Authorize]
[ApiController]
[Route("dicom")]
public class DicomRenderController : ControllerBase
{
    private readonly IWebHostEnvironment _env;

    public DicomRenderController(IWebHostEnvironment env)
    {
        _env = env;
    }

    [HttpGet("png")]
    public async Task<IActionResult> Png([FromQuery] string name, [FromQuery] int frame = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("name bos olamaz");

        var storagePath = GetStoragePath();
        var rel = name.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

        if (!TryResolveStoragePath(storagePath, rel, out var fullPath))
            return BadRequest("Gecersiz dosya yolu.");

        if (!System.IO.File.Exists(fullPath))
            return NotFound("Dosya bulunamadi");

        var dicomFile = await DicomFile.OpenAsync(fullPath).ConfigureAwait(false);
        var image = new DicomImage(dicomFile.Dataset, frame);

        using var rendered = image.RenderImage();
        using Image sharp = rendered.AsSharpImage();

        await using var ms = new MemoryStream();
        await sharp.SaveAsPngAsync(ms).ConfigureAwait(false);
        ms.Position = 0;

        return File(ms.ToArray(), "image/png");
    }

    [HttpGet("list")]
    public IActionResult List()
    {
        var storagePath = GetStoragePath();
        Directory.CreateDirectory(storagePath);

        var files = Directory.GetFiles(storagePath, "*.dcm")
            .Select(Path.GetFileName)
            .OrderBy(x => x)
            .ToList();

        return Ok(files);
    }

    private string GetStoragePath()
        => Path.Combine(_env.ContentRootPath, "Storage");

    private static bool TryResolveStoragePath(string storage, string relativePath, out string fullPath)
    {
        var storageFull = Path.GetFullPath(storage);
        fullPath = Path.GetFullPath(Path.Combine(storageFull, relativePath));
        var rel = Path.GetRelativePath(storageFull, fullPath);

        return !Path.IsPathFullyQualified(rel) &&
            !rel.StartsWith("..", StringComparison.Ordinal) &&
            !string.Equals(rel, "..", StringComparison.Ordinal);
    }
}
