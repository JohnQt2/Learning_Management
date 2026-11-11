namespace LMS.CoreApplicationServices.CourseModule.Dtos;
public record CourseCreateRequest(string Code, string Title, string? Description);
public record CourseUpdateRequest(string Title, string? Description);
public record EnrollmentStudentDto(int StudentId, string Username, string FullName, DateTime EnrolledAt,
    string? Email = null, bool IsLocked = false, int PublishedQuizCount = 0, int CompletedQuizCount = 0,
    double QuizCompletionPercent = 0, decimal? AverageScore = null, string LearningStatus = "NotStarted");
public record CourseDto(int Id, string Code, string Title, string Description, string? JoinCode, int LecturerId, int EnrollmentCount = 0);
public record EnrollmentRequest(string JoinCode);
public record EnrollmentDto(int CourseId, int StudentId, DateTime EnrolledAt);
public record TopicRequest(string Name);
public record TopicDto(int Id, int CourseId, string Name);
