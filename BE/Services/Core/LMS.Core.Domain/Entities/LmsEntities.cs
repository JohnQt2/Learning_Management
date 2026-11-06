namespace LMS.Core.Domain.Entities;

public sealed class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Student";
    public bool IsLocked { get; set; }
    public int SecurityVersion { get; set; }
}
public sealed class Course
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string JoinCode { get; set; } = "";
    public int LecturerId { get; set; }
    public User Lecturer { get; set; } = null!;
}
public sealed class Enrollment
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int StudentId { get; set; }
    public User Student { get; set; } = null!;
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
}
public sealed class Topic
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public string Name { get; set; } = "";
}
public sealed class LearningMaterial
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int TopicId { get; set; }
    public Topic Topic { get; set; } = null!;
    public string Title { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string StoragePath { get; set; } = "";
    public string ExtractedText { get; set; } = "";
    public string FileFormat { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long? SizeBytes { get; set; }
    public int? UploadedById { get; set; }
    public User? UploadedBy { get; set; }
    public DateTime? UploadedAt { get; set; }
    public string ExtractionStatus { get; set; } = "Legacy";
    public string ExtractionMessage { get; set; } = "";
    public bool IsDeleted { get; set; }
}
public sealed class Question
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int TopicId { get; set; }
    public Topic Topic { get; set; } = null!;
    public int? MaterialId { get; set; }
    public LearningMaterial? Material { get; set; }
    public string Content { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string Source { get; set; } = "Manual";
    public string? Difficulty { get; set; }
    public int? SourceChunkIndex { get; set; }
    public int? SourceStartCharacter { get; set; }
    public int? SourceEndCharacter { get; set; }
    public string Status { get; set; } = "Draft";
    public List<AnswerOption> Options { get; set; } = new();
}
public sealed class AnswerOption
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public string Content { get; set; } = "";
    public bool IsCorrect { get; set; }
}
public sealed class Quiz
{
    public int Id { get; set; }
    public int Revision { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public DateTime? PublishedAt { get; set; }
    public List<QuizQuestion> QuizQuestions { get; set; } = new();
}
public sealed class QuizQuestion
{
    public int QuizId { get; set; }
    public Quiz Quiz { get; set; } = null!;
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
}
public sealed class QuizAttempt
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz Quiz { get; set; } = null!;
    public int StudentId { get; set; }
    public User Student { get; set; } = null!;
    public string Status { get; set; } = "Submitted";
    public decimal Score { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public List<StudentAnswer> StudentAnswers { get; set; } = new();
}
public sealed class StudentAnswer
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public QuizAttempt Attempt { get; set; } = null!;
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public int SelectedOptionId { get; set; }
    public AnswerOption SelectedOption { get; set; } = null!;
    public bool IsCorrect { get; set; }
}
public sealed class MaterialRecommendation
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public QuizAttempt Attempt { get; set; } = null!;
    public int MaterialId { get; set; }
    public LearningMaterial Material { get; set; } = null!;
    public int TopicId { get; set; }
}
public sealed class AIGenerationLog
{
    public int Id { get; set; }
    public int MaterialId { get; set; }
    public LearningMaterial Material { get; set; } = null!;
    public string Status { get; set; } = "Failed";
    public string ErrorMessage { get; set; } = "";
    public int? RequestedQuestionCount { get; set; }
    public int? GeneratedQuestionCount { get; set; }
    public int? ChunkCount { get; set; }
    public int? ProviderRequestCount { get; set; }
    public long? DurationMilliseconds { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
