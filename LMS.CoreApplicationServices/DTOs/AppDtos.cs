using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace LMS.CoreApplicationServices.DTOs
{
    public class AppException : System.Exception
    {
        public int StatusCode { get; }
        public AppException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    }

    // UC01 & UC10
    public record LoginRequestDto(string Username, string Password);
    public record LoginResponseDto(string Token, int UserId, string FullName, string Role);
    public record UserDto(int Id, string Username, string FullName, string Role, bool IsLocked); // Không bao giờ trả PasswordHash
    public record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize);

    // UC02 & UC03
    public record CreateCourseDto(string Code, string Title, string Description);
    public record CourseDto(int Id, string Code, string Title, string JoinCode);
    public record EnrollRequestDto(string JoinCode);
    public record EnrollmentDto(int CourseId, int StudentId, System.DateTime EnrolledAt);
    public record CreateTopicDto(string Name);
    public record TopicDto(int Id, string Name);

    // UC04
    public class UploadMaterialDto
    {
        public int TopicId { get; set; }
        public string Title { get; set; } = string.Empty;
        public IFormFile File { get; set; } = null!;
    }
    public record MaterialDto(int Id, string Title, int TopicId, int TextLength, bool CanGenerateAI);

    // UC05 & UC06
    public record GenerateAIQuestionsDto(int MaterialId, int Count, string Difficulty);
    public record OptionDto(int Id, string Content, bool IsCorrect);
    public record QuestionDto(int Id, string Content, string? Explanation, string Status, string Source, List<OptionDto> Options);
    public record UpdateQuestionDto(string Content, string? Explanation, List<OptionDto> Options);

    // UC07 & UC08
    public record CreateQuizDto(int CourseId, string Title);
    public record PublishQuizDto(List<int> QuestionIds);
    public record QuizDto(int Id, int CourseId, string Title, string Status);

    // DTO dành riêng cho Sinh viên làm bài: TUYỆT ĐỐI KHÔNG CÓ IsCorrect
    public record StudentOptionTakeDto(int Id, string Content);
    public record StudentQuestionTakeDto(int Id, string Content, List<StudentOptionTakeDto> Options);
    public record QuizTakeDto(int QuizId, string Title, List<StudentQuestionTakeDto> Questions);

    public record SubmitAnswerItemDto(int QuestionId, int SelectedOptionId);
    public record SubmitQuizDto(List<SubmitAnswerItemDto> Answers);
    public record SubmitResultDto(int AttemptId, decimal Score, int CorrectCount, int TotalQuestions);

    // UC09
    public record TopicAccuracyDto(int TopicId, string TopicName, double Accuracy, bool IsWeak);
    public record RecommendationDto(int TopicId, string TopicName, int MaterialId, string MaterialTitle);
    public record AttemptResultDto(
        int AttemptId,
        decimal Score,
        int CorrectCount,
        int TotalQuestions,
        List<TopicAccuracyDto> TopicAccuracies,
        List<RecommendationDto> Recommendations
    );
}
