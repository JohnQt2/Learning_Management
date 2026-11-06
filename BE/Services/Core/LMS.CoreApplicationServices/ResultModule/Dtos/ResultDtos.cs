namespace LMS.CoreApplicationServices.ResultModule.Dtos;
public record CourseAttemptDto(int AttemptId, int QuizId, string QuizTitle, int StudentId, string StudentName, decimal Score, DateTime SubmittedAt);
public record AttemptSummaryDto(int Id, int QuizId, string QuizTitle, decimal Score, DateTime SubmittedAt);
public record TopicAccuracyDto(int TopicId, string TopicName, double Accuracy, bool IsWeak);
public record RecommendationDto(int TopicId, string TopicName, int? MaterialId, string? MaterialTitle,
    double Accuracy = 0, int CourseId = 0, string? DownloadUrl = null, string Source = "RuleBased", string Reason = "");
public record AnswerFeedbackDto(int QuestionId, string QuestionContent, string SelectedOptionContent,
    string CorrectOptionContent, bool IsCorrect, string Explanation, string TopicName);
public record AttemptResultDto(int AttemptId, decimal Score, int CorrectCount, int TotalQuestions,
    List<TopicAccuracyDto> TopicAccuracies, List<RecommendationDto> Recommendations, List<AnswerFeedbackDto> Answers,
    int WrongCount, double Accuracy);
public record StudentCourseProgressDto(int CourseId, string Code, string Title, int PublishedQuizCount,
    int CompletedQuizCount, double QuizCompletionPercent, decimal? AverageScore);
public record StudentWeakTopicDto(int CourseId, string CourseTitle, int TopicId, string TopicName,
    double Accuracy, int CorrectAnswerCount, int TotalAnswerCount, string Reason);
public record StudentDashboardDto(int EnrolledCourseCount, int CompletedQuizCount, int PublishedQuizCount,
    decimal? AverageScore, double QuizCompletionPercent, List<StudentCourseProgressDto> CourseProgress,
    List<AttemptSummaryDto> RecentAttempts, List<StudentWeakTopicDto> WeakTopics, List<RecommendationDto> Recommendations);
public record QuizStatisticsDto(int QuizId, string Title, DateTime? PublishedAt, int SubmittedStudentCount, decimal? AverageScore);
public record StudentStatisticsDto(int StudentId, string FullName, int CompletedQuizCount, int PublishedQuizCount,
    double QuizCompletionPercent, decimal? AverageScore);
public record CourseWeakTopicDto(int TopicId, string TopicName, double Accuracy, int IncorrectAnswerCount,
    int TotalAnswerCount, int AffectedStudentCount, string Reason);
public record CourseStatisticsDto(int CourseId, int StudentCount, int PublishedQuizCount, int SubmittedAttemptCount,
    decimal? AverageScore, List<QuizStatisticsDto> QuizStatistics, List<StudentStatisticsDto> StudentStatistics,
    List<CourseWeakTopicDto> WeakTopics);
