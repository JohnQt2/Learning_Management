using LMS.CoreApplicationServices.QuestionModule.Dtos;

namespace LMS.CoreApplicationServices.QuestionModule.Abstracts;

/// <summary>UC06: ngân hàng câu hỏi — soạn, sửa Draft, duyệt.</summary>
public interface IQuestionService
{
    Task<List<QuestionDto>> ListAsync(int courseId, int lecturerId, CancellationToken ct);
    Task<QuestionDto> CreateAsync(int lecturerId, QuestionCreateRequest request, CancellationToken ct);
    Task<QuestionDto> EditAsync(int id, int lecturerId, QuestionEditRequest request, CancellationToken ct);
    Task<QuestionDto> ApproveAsync(int id, int lecturerId, CancellationToken ct);
}
