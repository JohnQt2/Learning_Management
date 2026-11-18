using LMS.CoreApplicationServices.ResultModule.Dtos;

namespace LMS.CoreApplicationServices.ResultModule.Abstracts;

/// <summary>UC09: kết quả bài làm, lịch sử, dashboard sinh viên, thống kê khóa học và gợi ý ôn tập.</summary>
public interface IResultService
{
    Task<AttemptResultDto> GetAsync(int attemptId, int userId, string role, CancellationToken ct);
    Task<List<RecommendationDto>> MyRecommendationsAsync(int studentId, CancellationToken ct);
    Task<List<AttemptSummaryDto>> MyAttemptsAsync(int studentId, CancellationToken ct);
    Task<List<CourseAttemptDto>> CourseAttemptsAsync(int courseId, int lecturerId, CancellationToken ct);
    Task<StudentDashboardDto> DashboardAsync(int studentId, CancellationToken ct);
    Task<CourseStatisticsDto> CourseStatisticsAsync(int courseId, int lecturerId, CancellationToken ct);
}
