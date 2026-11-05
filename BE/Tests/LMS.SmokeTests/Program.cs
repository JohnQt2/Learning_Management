using System.Text;
using System.Text.Json;
using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Adapters;
using LMS.Core.Infrastructure.Persistence;
using LMS.CoreApplicationServices.AIModule.Implements;
using LMS.CoreApplicationServices.AuthenticationModule.Dtos;
using LMS.CoreApplicationServices.AuthenticationModule.Implements;
using LMS.CoreApplicationServices.Common;
using LMS.CoreApplicationServices.CourseModule.Dtos;
using LMS.CoreApplicationServices.CourseModule.Implements;
using LMS.CoreApplicationServices.MaterialModule.Implements;
using LMS.CoreApplicationServices.QuestionModule.Dtos;
using LMS.CoreApplicationServices.QuestionModule.Implements;
using LMS.CoreApplicationServices.QuizModule.Dtos;
using LMS.CoreApplicationServices.QuizModule.Implements;
using LMS.CoreApplicationServices.ResultModule.Implements;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
}

static async Task ExpectAppError(Func<Task> action, int status, string label)
{
    try { await action(); throw new Exception("FAIL: " + label + " was accepted"); }
    catch (AppException e) when (e.StatusCode == status) { Console.WriteLine("PASS: " + label); }
}

using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<LmsDbContext>().UseSqlite(connection).Options;
await using var db = new LmsDbContext(options);
await db.Database.EnsureCreatedAsync();
var testPassword = "Test!" + Guid.NewGuid().ToString("N");
await DemoSeeder.SeedAsync(db, testPassword);
var hasher = new PasswordHasher<User>();
var auth = new AuthService(db, hasher);
var lecturer = await auth.LoginAsync(new LoginRequest("lecturer", testPassword), default);
var student = await auth.LoginAsync(new LoginRequest("student", testPassword), default);
Check(lecturer.Role == "Lecturer" && student.Role == "Student", "Log in with hashed passwords");
var registered = await auth.RegisterStudentAsync(new RegisterRequest("newstudent", "Student Two", "Secret2026!"), default);
Check(registered.Role == "Student", "Student registration");
await ExpectAppError(() => auth.LoginAsync(new LoginRequest("lecturer", "wrongpassword"), default), 401, "Wrong password rejected");
await ExpectAppError(() => auth.RegisterStudentAsync(new RegisterRequest("newstudent", "Student Three", "Secret2026!"), default), 409, "Duplicate username rejected");

var courses = new CourseService(db);
var course = await courses.CreateAsync(lecturer.Id, new CourseCreateRequest("IT101", "Backend", "Basic course"), default);
Check(course.JoinCode?.Length == 8, "Course created with join code");
await ExpectAppError(() => courses.RequireOwnerAsync(course.Id, student.Id, default), 403, "Student cannot edit lecturer course");
await ExpectAppError(() => courses.EnrollAsync(registered.Id, course.Id, new EnrollmentRequest("BADCODE"), default), 404, "Wrong join code rejected");
await ExpectAppError(() => courses.ListTopicsAsync(course.Id, registered.Id, "Student", default), 403, "Unenrolled student cannot view topics");
await courses.EnrollAsync(student.Id, course.Id, new EnrollmentRequest(course.JoinCode!), default);
Check((await courses.ListAsync(student.Id, "Student", default)).Any(c => c.Id == course.Id), "Student enrolled in course");
try
{
    await courses.EnrollAsync(student.Id, course.Id, new EnrollmentRequest(course.JoinCode!), default);
    throw new Exception("FAIL: Duplicate enrollment accepted");
}
catch (AppException ex) when (ex.StatusCode == 409) { Console.WriteLine("PASS: Duplicate enrollment blocked"); }

var topic = await courses.CreateTopicAsync(course.Id, lecturer.Id, new TopicRequest("N-Tier"), default);
var fileDir = Path.Combine(Path.GetTempPath(), "lms-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fileDir);
try
{
    var fileStore = new LocalFileStorage(fileDir);
    var materialSvc = new MaterialService(db, fileStore, courses);
    await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Controller gọi service. Service kiểm tra nghiệp vụ. Database lưu dữ liệu."));
    var mat = await materialSvc.UploadAsync(course.Id, topic.Id, lecturer.Id, stream, "lesson.txt", stream.Length, default);
    var retrieved = await materialSvc.DownloadAsync(mat.Id, student.Id, "Student", default);
    await using (retrieved.Stream)
    {
        using var reader = new StreamReader(retrieved.Stream);
        Check((await reader.ReadToEndAsync()).Contains("Controller"), "Upload/download TXT material with student permission");
    }

    // Reject forged PDF and over-limit content, even if the caller lies about size.
    await using (var fakePdf = new MemoryStream(Encoding.UTF8.GetBytes("not a pdf")))
    {
        await ExpectAppError(() => materialSvc.UploadAsync(course.Id, topic.Id, lecturer.Id,
            fakePdf, "forged.pdf", fakePdf.Length, default), 400, "Fake PDF content rejected");
    }
    await using (var huge = new MemoryStream(new byte[5_000_001]))
    {
        await ExpectAppError(() => materialSvc.UploadAsync(course.Id, topic.Id, lecturer.Id,
            huge, "big.txt", 1, default), 400, "Actual upload size enforced, not declared length");
    }
    var questions = new QuestionService(db, courses);
    var manual = await questions.CreateAsync(lecturer.Id,
        new QuestionCreateRequest(course.Id, topic.Id, "Tầng nào nhận HTTP request?", "API nhận HTTP", new List<OptionInput>
        {
            new("API", true), new("Database", false), new("Domain", false), new("SQL", false)
        }), default);
    Check(manual.Status == "Draft" && manual.Source == "Manual", "Manual question starts Draft");
    await questions.ApproveAsync(manual.Id, lecturer.Id, default);
    await ExpectAppError(() => questions.EditAsync(manual.Id, lecturer.Id,
        new QuestionEditRequest("Attempt to change published content", "", new List<OptionInput> {
            new("A", true), new("B", false), new("C", false), new("D", false)}), default), 409,
        "Approved question cannot be edited");
    var quizSvc = new QuizService(db, courses);
    await ExpectAppError(() => quizSvc.TakeAsync(99999, student.Id, default), 404, "Unpublished/unknown quiz hidden");
    var quiz = await quizSvc.CreateAsync(lecturer.Id, new QuizCreateRequest(course.Id, "Quiz N-Tier"), default);
    await quizSvc.PublishAsync(quiz.Id, lecturer.Id, new QuizPublishRequest(new List<int> { manual.Id }), default);
    await ExpectAppError(() => quizSvc.TakeAsync(quiz.Id, registered.Id, default), 403, "Unenrolled student cannot take quiz");
    var exam = await quizSvc.TakeAsync(quiz.Id, student.Id, default);
    Check(exam.Questions.Count == 1 && !JsonSerializer.Serialize(exam).Contains("IsCorrect"), "Quiz read does not leak correct answers");
    var correctOption = manual.Options.Single(x => x.IsCorrect).Id;
    var attempt = await quizSvc.SubmitAsync(quiz.Id, student.Id,
        new SubmitRequest(new List<SubmitItem> { new(manual.Id, correctOption) }), default);
    Check(attempt.Score == 10, "Backend calculates correct score");
    var result = await new ResultService(db).GetAsync(attempt.AttemptId, student.Id, "Student", default);
    Check(result.TotalQuestions == 1 && result.CorrectCount == 1, "Student retrieves stored result");
    Check((await new ResultService(db).MyAttemptsAsync(student.Id, default)).Count == 1, "History survives querying DB");
    var secondQuiz = await quizSvc.CreateAsync(lecturer.Id, new QuizCreateRequest(course.Id, "Quiz needs review"), default);
    await quizSvc.PublishAsync(secondQuiz.Id, lecturer.Id,
        new QuizPublishRequest(new List<int> { manual.Id }), default);
    var wrongOption = manual.Options.First(x => !x.IsCorrect).Id;
    var wrongAttempt = await quizSvc.SubmitAsync(secondQuiz.Id, student.Id,
        new SubmitRequest(new List<SubmitItem> { new(manual.Id, wrongOption) }), default);
    var wrongResult = await new ResultService(db).GetAsync(wrongAttempt.AttemptId, student.Id, "Student", default);
    Check(wrongResult.TopicAccuracies.Single().IsWeak && wrongResult.Recommendations.Any(x => x.MaterialId == mat.Id),
        "Wrong answer identifies weak topic and recommends matching real material");
    try
    {
        await quizSvc.SubmitAsync(quiz.Id, student.Id,
            new SubmitRequest(new List<SubmitItem> { new(manual.Id, correctOption) }), default);
        throw new Exception("FAIL: Duplicate quiz submit accepted");
    }
    catch (AppException ex) when (ex.StatusCode == 409) { Console.WriteLine("PASS: Duplicate quiz submit blocked"); }

    var mock = new MockAiClient();
    var generated = QuestionJsonValidator.Parse(await mock.GenerateJsonAsync(
        "N-Tier tách trách nhiệm. API nhận request. Database lưu dữ liệu.", 3, "Medium", default), 3);
    Check(generated.Count == 3 && generated.All(x => x.Options.Count(o => o.IsCorrect) == 1), "Optional simulated AI JSON format");
    try
    {
        await new DisabledAiClient().GenerateJsonAsync("content", 3, "Medium", default);
        throw new Exception("FAIL: Disabled AI responded with invented content");
    }
    catch (AiClientException ex) when (ex.StatusCode == 503) { Console.WriteLine("PASS: Disabled AI cannot invent generated questions"); }
    var fileCount = Directory.EnumerateFiles(fileDir).Count();
    Check(fileCount == 1, "Failed uploads cleaned up; only valid TXT retained");
}
finally
{
    try { Directory.Delete(fileDir, true); } catch { /* temp files only */ }
}
await BusinessSmoke.RunAsync();
Console.WriteLine("ALL BASIC FUNCTION SMOKE TESTS PASSED.");
