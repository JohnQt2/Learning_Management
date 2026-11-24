using LMS.CoreApplicationServices.MaterialModule.Dtos;

namespace LMS.CoreApplicationServices.MaterialModule.Abstracts;

/// <summary>UC04: tải lên, trích xuất, xem trước, tải về và xóa mềm học liệu.</summary>
public interface IMaterialService
{
    MaterialUploadPolicyDto UploadPolicy();
    Task<List<MaterialDto>> ListAsync(int courseId, int userId, string role, CancellationToken ct);
    Task<MaterialDto> UploadAsync(int courseId, int topicId, int ownerId, Stream content, string fileName, long length, CancellationToken ct);
    Task<MaterialDto> UploadAsync(int courseId, int topicId, int ownerId, Stream content, string fileName, long length, string? contentType, CancellationToken ct);
    Task<(Stream Stream, string Name, string ContentType)> DownloadAsync(int materialId, int userId, string role, CancellationToken ct);
    Task<MaterialTextDto> ExtractedTextAsync(int materialId, int userId, string role, CancellationToken ct);
    Task DeleteAsync(int materialId, int ownerId, CancellationToken ct);
    Task<MaterialDto> ReextractAsync(int materialId, int ownerId, CancellationToken ct);
}
