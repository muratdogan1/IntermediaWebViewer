using FellowOakDicom;
using FellowOakDicom.Imaging;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;

namespace Intermedia.Web.Controllers;

[ApiController]
[Route("dicom")]
public class DicomRenderController : ControllerBase
{
    [HttpGet("png")]
    public async Task<IActionResult> Png([FromQuery] string name, [FromQuery] int frame = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("name boş olamaz");

        name = Path.GetFileName(name);

        var storagePath = Path.Combine(Directory.GetCurrentDirectory(), "Storage");
        var fullPath = Path.Combine(storagePath, name);

        if (!System.IO.File.Exists(fullPath))
            return NotFound("Dosya bulunamadı");

        // Image manager: ImageSharp
        // (paket: fo-dicom.Imaging.ImageSharp)
        new DicomSetupBuilder()
            .RegisterServices(s => s.AddFellowOakDicom().AddImageManager<ImageSharpImageManager>())
            .Build();

        var dicomFile = await DicomFile.OpenAsync(fullPath).ConfigureAwait(false);
        var image = new DicomImage(dicomFile.Dataset, frame);

        // Render → ImageSharp image
        var rendered = image.RenderImage();               // IImage
        var sharp = rendered.AsSharpImage();              // SixLabors.ImageSharp.Image

        await using var ms = new MemoryStream();
        await sharp.SaveAsPngAsync(ms).ConfigureAwait(false);
        ms.Position = 0;

        return File(ms.ToArray(), "image/png");
    }

    [HttpGet("list")]
    public IActionResult List()
    {
        var storagePath = Path.Combine(Directory.GetCurrentDirectory(), "Storage");
        if (!Directory.Exists(storagePath))
            Directory.CreateDirectory(storagePath);

        var files = Directory.GetFiles(storagePath, "*.dcm")
            .Select(Path.GetFileName)
            .OrderBy(x => x)
            .ToList();

        return Ok(files);
    }
}