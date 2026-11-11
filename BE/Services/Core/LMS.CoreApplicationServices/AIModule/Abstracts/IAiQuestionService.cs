using LMS.CoreApplicationServices.AIModule.Dtos;
using LMS.CoreApplicationServices.QuestionModule.Dtos;

namespace LMS.CoreApplicationServices.AIModule.Abstracts;

/// <summary>UC05: lập kế hoạch chia đoạn và sinh câu hỏi Draft bằng AI từ học liệu đã trích xuất.</summary>
public interface IAiQuestionService
{
    Task<AiGenerationPlanDto> PlanAsync(GenerateRequest request, int lecturerId, CancellationToken ct);
    Task<List<QuestionDto>> GenerateAsync(GenerateRequest request, int lecturerId, CancellationToken ct);
}
