using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LMS.Constants;
using LMS.CoreApplicationServices.MaterialModule.Abstracts;
namespace LMS.Core.API.Controllers;

[ApiController]
[Route("api/courses/{courseId:int}/materials")]
[Authorize]
public sealed class MaterialsController : ControllerBase
{
    private readonly IMaterialService _service;
    public MaterialsController(IMaterialService service) => _service = service;
    [HttpGet]
    public async Task<IActionResult> List(int courseId, CancellationToken ct)
        => Ok(await _service.ListAsync(courseId, CurrentUser.Id(User), CurrentUser.Role(User), ct));
    [HttpGet("/api/materials/upload-policy")]
    [HttpGet("/api/materials/capabilities")]
    public IActionResult UploadPolicy() => Ok(_service.UploadPolicy());
    [HttpGet("/api/materials/{id:int}/extracted-text")]
    public async Task<IActionResult> ExtractedText(int id, CancellationToken ct)
        => Ok(await _service.ExtractedTextAsync(id, CurrentUser.Id(User), CurrentUser.Role(User), ct));
    [HttpDelete("/api/materials/{id:int}")]
    [Authorize(Roles = Roles.Lecturer)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, CurrentUser.Id(User), ct);
        return NoContent();
    }
    [HttpPost("/api/materials/{id:int}/extract")]
    [Authorize(Roles = Roles.Lecturer)]
    public async Task<IActionResult> Reextract(int id, CancellationToken ct)
        => Ok(await _service.ReextractAsync(id, CurrentUser.Id(User), ct));
    [HttpGet("/api/materials/{id:int}/download")]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var file = await _service.DownloadAsync(id, CurrentUser.Id(User), CurrentUser.Role(User), ct);
        return File(file.Stream, file.ContentType, file.Name);
    }
    [HttpPost]
    [Authorize(Roles = Roles.Lecturer)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(int courseId, IFormFile file, [FromForm] int topicId, CancellationToken ct)
    {
        if (file == null) return BadRequest(new { error = "Chưa chọn tệp" });
        await using var stream = file.OpenReadStream();
        var material = await _service.UploadAsync(courseId, topicId, CurrentUser.Id(User), stream, file.FileName, file.Length, file.ContentType, ct);
        return CreatedAtAction(nameof(Download), new { id = material.Id }, material);
    }
}
