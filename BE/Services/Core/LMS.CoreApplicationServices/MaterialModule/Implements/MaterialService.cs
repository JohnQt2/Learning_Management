using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Adapters;
using LMS.Core.Infrastructure.Persistence;
using LMS.CoreApplicationServices.Common;
using LMS.CoreApplicationServices.CourseModule.Abstracts;
using LMS.CoreApplicationServices.MaterialModule.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LMS.CoreApplicationServices.MaterialModule.Implements;
public sealed class MaterialService
{
    private readonly LmsDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ICourseService _courses;
    private readonly ILogger<MaterialService>? _log;
    public MaterialService(LmsDbContext db, IFileStorage storage, ICourseService courses, ILogger<MaterialService>? log = null)
    { _db = db; _storage = storage; _courses = courses; _log = log; }
    private static string Status(LearningMaterial m) => m.ExtractionStatus == "Legacy" && !string.IsNullOrWhiteSpace(m.ExtractedText) ? "Extracted" : m.ExtractionStatus;
    private static string Message(LearningMaterial m) => m.ExtractionStatus == "Legacy"
        ? !string.IsNullOrWhiteSpace(m.ExtractedText) ? "Văn bản được lưu từ phiên bản trước." : "Học liệu cũ chưa được trích xuất. Giảng viên có thể trích xuất lại từ tệp gốc."
        : m.ExtractionMessage;
    private static MaterialDto Map(LearningMaterial m) => new(m.Id, m.CourseId, m.TopicId, m.Title, m.OriginalFileName,
        !m.IsDeleted && !string.IsNullOrWhiteSpace(m.ExtractedText),
        m.FileFormat,
        m.ContentType, m.SizeBytes, m.UploadedById, m.UploadedAt, Status(m), Message(m), m.ExtractedText.Length);

    public MaterialUploadPolicyDto UploadPolicy() => new(_storage.Limits.MaxUploadBytes,
        MaterialFormats.Extensions.ToArray(), _storage.Limits.MaxPreviewCharacters, MaterialFormats.Extensions.ToArray());
    public async Task<List<MaterialDto>> ListAsync(int courseId, int userId, string role, CancellationToken ct)
    {
        await _courses.RequireMemberAsync(courseId, userId, role, ct);
        // Project counts/metadata in SQL; never materialize every document's full text for a list.
        return await _db.LearningMaterials.AsNoTracking().Where(m => m.CourseId == courseId && !m.IsDeleted)
            .OrderByDescending(m => m.Id).Select(m => new MaterialDto(m.Id, m.CourseId, m.TopicId, m.Title, m.OriginalFileName,
                m.ExtractedText.Trim() != "", m.FileFormat, m.ContentType, m.SizeBytes, m.UploadedById, m.UploadedAt,
                m.ExtractionStatus == "Legacy" && m.ExtractedText.Trim() != "" ? "Extracted" : m.ExtractionStatus,
                m.ExtractionStatus == "Legacy" ? m.ExtractedText.Trim() != "" ? "Văn bản được lưu từ phiên bản trước." :
                    "Học liệu cũ chưa được trích xuất. Giảng viên có thể trích xuất lại từ tệp gốc." : m.ExtractionMessage,
                m.ExtractedText.Length)).ToListAsync(ct);
    }
    public async Task<MaterialDto> UploadAsync(int courseId, int topicId, int ownerId, Stream content, string fileName, long length, CancellationToken ct)
        => await UploadAsync(courseId, topicId, ownerId, content, fileName, length, null, ct);

    public async Task<MaterialDto> UploadAsync(int courseId, int topicId, int ownerId, Stream content, string fileName, long length, string? contentType, CancellationToken ct)
    {
        await _courses.RequireOwnerAsync(courseId, ownerId, ct);
        if (length <= 0 || length > _storage.Limits.MaxUploadBytes) throw new AppException(400, $"File phải có dung lượng từ 1 byte đến {_storage.Limits.MaxUploadBytes} byte");
        var originalName = (fileName ?? "").Replace('\\', '/').Split('/').Last().Trim();
        if (originalName.Length is < 1 or > 180 || originalName.Any(char.IsControl) || originalName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new AppException(400, "Tên tệp không hợp lệ hoặc dài hơn 180 ký tự");
        var ext = Path.GetExtension(originalName).ToLowerInvariant();
        try { MaterialFormats.ValidateMime(ext, contentType); }
        catch (InvalidDataException ex) { throw new AppException(400, ex.Message); }
        if (!await _db.Topics.AnyAsync(t => t.Id == topicId && t.CourseId == courseId, ct)) throw new AppException(400, "Topic không thuộc Course");
        StoredMaterial saved;
        try { saved = await _storage.SaveWithMetadataAsync(content, originalName, contentType, ct); }
        catch (InvalidDataException ex) { throw new AppException(400, ex.Message); }
        catch (IOException) { throw new AppException(503, "Không thể lưu học liệu; hãy kiểm tra ổ đĩa và thử lại"); }
        catch (UnauthorizedAccessException) { throw new AppException(503, "Không thể lưu học liệu; hãy kiểm tra quyền thư mục"); }
        var storagePath = saved.StoragePath;
        var material = new LearningMaterial
        {
            CourseId = courseId, TopicId = topicId, Title = originalName, OriginalFileName = originalName,
            StoragePath = storagePath, ExtractedText = saved.Text, FileFormat = saved.FileFormat, ContentType = saved.ContentType,
            SizeBytes = saved.SizeBytes, UploadedById = ownerId, UploadedAt = DateTime.UtcNow,
            ExtractionStatus = saved.ExtractionStatus, ExtractionMessage = saved.ExtractionMessage
        };
        try
        {
            _db.LearningMaterials.Add(material); await _db.SaveChangesAsync(ct);
            return Map(material);
        }
        catch
        {
            _db.Entry(material).State = EntityState.Detached;
            // A connection failure can make commit outcome ambiguous. Keep the file when
            // metadata exists or cannot be checked, so a committed row never loses its file.
            try
            {
                if (!await _db.LearningMaterials.AsNoTracking().AnyAsync(m => m.StoragePath == storagePath, CancellationToken.None))
                    _storage.Delete(storagePath);
            }
            catch (Exception cleanup) when (cleanup is not OperationCanceledException)
            { _log?.LogWarning("A new material file was preserved because database commit or cleanup could not be verified"); }
            throw;
        }
    }
    public async Task<(Stream Stream, string Name, string ContentType)> DownloadAsync(int materialId, int userId, string role, CancellationToken ct)
    {
        var m = await _db.LearningMaterials.AsNoTracking().FirstOrDefaultAsync(x => x.Id == materialId && !x.IsDeleted, ct)
            ?? throw new AppException(404, "Không tìm thấy học liệu");
        await _courses.RequireMemberAsync(m.CourseId, userId, role, ct);
        try
        {
            var stream = _storage.OpenRead(m.StoragePath);
            var type = MaterialFormats.ContentType(Path.GetExtension(m.OriginalFileName).ToLowerInvariant());
            if (type.StartsWith("text/", StringComparison.Ordinal)) type += "; charset=utf-8";
            return (stream, m.OriginalFileName, type);
        }
        catch (FileNotFoundException) { throw new AppException(404, "Tệp không còn trên máy chủ"); }
        catch (DirectoryNotFoundException) { throw new AppException(404, "Tệp không còn trên máy chủ"); }
        catch (IOException) { throw new AppException(503, "Không thể mở học liệu; hãy thử lại sau"); }
        catch (UnauthorizedAccessException) { throw new AppException(503, "Không thể mở học liệu; hãy kiểm tra quyền thư mục"); }
    }

    public async Task<MaterialTextDto> ExtractedTextAsync(int materialId, int userId, string role, CancellationToken ct)
    {
        var material = await _db.LearningMaterials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == materialId && !m.IsDeleted, ct)
            ?? throw new AppException(404, "Không tìm thấy học liệu");
        await _courses.RequireMemberAsync(material.CourseId, userId, role, ct);
        var text = material.ExtractedText;
        var limit = _storage.Limits.MaxPreviewCharacters;
        // Do not split a UTF-16 surrogate pair when bounding a response.
        var count = Math.Min(text.Length, limit);
        if (count < text.Length && count > 0 && char.IsHighSurrogate(text[count - 1])) count--;
        return new(material.Id, text[..count], Status(material), Message(material), text.Length > count, text.Length);
    }

    public async Task DeleteAsync(int materialId, int ownerId, CancellationToken ct)
    {
        var material = await _db.LearningMaterials.FirstOrDefaultAsync(m => m.Id == materialId, ct)
            ?? throw new AppException(404, "Không tìm thấy học liệu");
        await _courses.RequireOwnerAsync(material.CourseId, ownerId, ct);
        if (material.IsDeleted) return;
        // Preserve both original file and FK references used by Questions, Quiz, and history.
        material.IsDeleted = true;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AppException(409, "Học liệu vừa được thay đổi; hãy tải lại danh sách"); }
    }

    public async Task<MaterialDto> ReextractAsync(int materialId, int ownerId, CancellationToken ct)
    {
        var material = await _db.LearningMaterials.FirstOrDefaultAsync(m => m.Id == materialId && !m.IsDeleted, ct)
            ?? throw new AppException(404, "Không tìm thấy học liệu");
        await _courses.RequireOwnerAsync(material.CourseId, ownerId, ct);
        StoredMaterial extracted;
        try { extracted = await _storage.ExtractExistingAsync(material.StoragePath, material.OriginalFileName, ct); }
        catch (InvalidDataException ex) { throw new AppException(400, ex.Message); }
        catch (FileNotFoundException) { throw new AppException(404, "Tệp không còn trên máy chủ"); }
        catch (DirectoryNotFoundException) { throw new AppException(404, "Tệp không còn trên máy chủ"); }
        catch (IOException) { throw new AppException(503, "Không thể đọc học liệu; hãy thử lại sau"); }
        catch (UnauthorizedAccessException) { throw new AppException(503, "Không thể đọc học liệu; hãy kiểm tra quyền thư mục"); }
        material.ExtractedText = extracted.Text;
        material.FileFormat = extracted.FileFormat;
        material.ContentType = extracted.ContentType;
        material.SizeBytes = extracted.SizeBytes;
        material.ExtractionStatus = extracted.ExtractionStatus;
        material.ExtractionMessage = extracted.ExtractionMessage;
        // Legacy upload time/uploader stay unknown. Extraction is not a new upload.
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AppException(409, "Học liệu vừa được thay đổi; hãy tải lại danh sách"); }
        return Map(material);
    }
}
