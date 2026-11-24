using LMS.CoreApplicationServices.QuizModule.Dtos;

namespace LMS.CoreApplicationServices.QuizModule.Abstracts;

/// <summary>UC07, UC08: soạn và phát hành Quiz; sinh viên làm bài, nộp và được chấm điểm.</summary>
public interface IQuizService
{
    Task<List<QuizDto>> ListAsync(int courseId, int userId, string role, CancellationToken ct);
    Task<QuizAuthoringDto> AuthoringAsync(int quizId, int lecturerId, CancellationToken ct);
    Task<QuizDto> CreateAsync(int lecturerId, QuizCreateRequest request, CancellationToken ct);
    Task<QuizDto> UpdateAsync(int quizId, int lecturerId, QuizUpdateRequest request, CancellationToken ct);
    Task<QuizDto> PublishAsync(int quizId, int lecturerId, QuizPublishRequest request, CancellationToken ct);
    Task<QuizTakeDto> TakeAsync(int quizId, int studentId, CancellationToken ct);
    Task<SubmitResponse> SubmitAsync(int quizId, int studentId, SubmitRequest request, CancellationToken ct);
}
