using LMS.CoreApplicationServices.QuestionModule.Dtos;

namespace LMS.CoreApplicationServices.QuizModule.Dtos;
public record QuizCreateRequest(int CourseId, string Title, string? Description = null, List<int>? QuestionIds = null);
public record QuizUpdateRequest(string Title, string? Description = null, List<int>? QuestionIds = null);
public record QuizPublishRequest(List<int> QuestionIds);
public record QuizDto(int Id, int CourseId, string Title, string Status, DateTime? PublishedAt, string Description = "", int QuestionCount = 0);
public record QuizAuthoringDto(int Id, int CourseId, string Title, string Description, string Status, DateTime? PublishedAt,
    List<int> QuestionIds, List<QuestionDto> Questions);
public record TakeOptionDto(int Id, string Content);
public record TakeQuestionDto(int Id, string Content, List<TakeOptionDto> Options);
public record QuizTakeDto(int Id, string Title, List<TakeQuestionDto> Questions, string Description = "");
public record SubmitItem(int QuestionId, int SelectedOptionId);
public record SubmitRequest(List<SubmitItem> Answers);
public record SubmitResponse(int AttemptId, decimal Score, int CorrectCount, int TotalQuestions);
