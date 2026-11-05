using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LMS.ProgressTests;

internal static class ProgressEntry
{
    private static int _checks;
    private static async Task Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lms-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var password = "Test!" + Guid.NewGuid().ToString("N") + "a1";
        var settings = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development", ["DotEnv__Enabled"] = "false",
            ["Database__Provider"] = "Sqlite", ["Database__AutoMigrate"] = "true",
            ["ConnectionStrings__Sqlite"] = "Data Source=" + Path.Combine(directory, "test.sqlite"),
            ["Storage__RootPath"] = Path.Combine(directory, "Uploads"), ["Demo__Seed"] = "true",
            ["Demo__Password"] = password, ["Bootstrap__AdminUsername"] = "", ["Ai__Mode"] = "Disabled",
            ["Jwt__Key"] = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
            ["Cors__AllowedOrigins__0"] = "http://localhost:5173", ["RateLimiting__AuthPermitLimit"] = "100"
        };
        var originals = settings.ToDictionary(x => x.Key, x => Environment.GetEnvironmentVariable(x.Key));
        foreach (var setting in settings) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        try
        {
            await Run(password);
            Console.WriteLine($"PROGRESS TESTS PASSED: {_checks} assertions; isolated SQLite/HTTP with real quiz scoring; no provider calls.");
        }
        finally
        {
            foreach (var setting in originals) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            SqliteConnection.ClearAllPools();
            var full = Path.GetFullPath(directory);
            if (Path.GetDirectoryName(full) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) ||
                !Path.GetFileName(full).StartsWith("lms-progress-", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test cleanup target");
            Directory.Delete(full, true);
        }
    }

    private static async Task Run(string password)
    {
        int courseId;
        using (var host = new TestHost())
        {
            using var anonymous = host.CreateClient();
            using var admin = await Login(host, "admin", password);
            using var teacher = await Login(host, "lecturer", password);
            await Send(admin, "POST", "/api/admin/users/lecturers", new { username = "progress_teacher", fullName = "Other Teacher", password }, 201);
            using var otherTeacher = await Login(host, "progress_teacher", password);
            var a = await Register(host, "progress_a", password);
            var b = await Register(host, "progress_b", password);
            var c = await Register(host, "progress_c", password);
            using var studentA = a.Client; using var studentB = b.Client; using var studentC = c.Client;
            await Send(anonymous, "GET", "/api/students/me/dashboard", null, 401);
            await Send(teacher, "GET", "/api/students/me/dashboard", null, 403);
            await Send(admin, "GET", "/api/students/me/dashboard", null, 403);
            var empty = await Dashboard(studentA);
            Check(Int(empty, "enrolledCourseCount") == 0 && Int(empty, "completedQuizCount") == 0 && Int(empty, "publishedQuizCount") == 0 &&
                empty["averageScore"] == null && Number(empty, "quizCompletionPercent") == 0 && empty["courseProgress"]!.AsArray().Count == 0 &&
                empty["recentAttempts"]!.AsArray().Count == 0 && empty["weakTopics"]!.AsArray().Count == 0 && empty["recommendations"]!.AsArray().Count == 0,
                "student with no enrollment has actual zero counts, null average and empty lists");
            var course = await Send(teacher, "POST", "/api/courses", new { code = "PROGRESS", title = "Progress Course" }, 201);
            courseId = Int(course!, "id");
            await Send(anonymous, "GET", $"/api/courses/{courseId}/statistics", null, 401);
            await Send(studentA, "GET", $"/api/courses/{courseId}/statistics", null, 403);
            await Send(admin, "GET", $"/api/courses/{courseId}/statistics", null, 403);
            await Send(otherTeacher, "GET", $"/api/courses/{courseId}/statistics", null, 403);
            await Send(teacher, "GET", "/api/courses/2147483647/statistics", null, 404);
            var zeroStats = await Stats(teacher, courseId);
            Check(Int(zeroStats, "studentCount") == 0 && Int(zeroStats, "publishedQuizCount") == 0 && Int(zeroStats, "submittedAttemptCount") == 0 &&
                zeroStats["averageScore"] == null && zeroStats["quizStatistics"]!.AsArray().Count == 0 && zeroStats["studentStatistics"]!.AsArray().Count == 0,
                "empty course statistics never invent learners, submissions or average");
            var topic = await Send(teacher, "POST", $"/api/courses/{courseId}/topics", new { name = "Access control" }, 201);
            var topicId = Int(topic!, "id");
            var question = await Send(teacher, "POST", "/api/questions", new
            {
                courseId, topicId, content = "Who may modify a course?", explanation = "Only its lecturer owner.",
                options = new[] { new { content = "Owner", isCorrect = true }, new { content = "Any student", isCorrect = false },
                    new { content = "Anonymous", isCorrect = false }, new { content = "All users", isCorrect = false } }
            }, 201);
            var questionId = Int(question!, "id");
            var correctOption = Int(question!["options"]!.AsArray().Single(o => o!["isCorrect"]!.GetValue<bool>())!, "id");
            var wrongOption = Int(question["options"]!.AsArray().First(o => !o!["isCorrect"]!.GetValue<bool>())!, "id");
            await Send(teacher, "POST", $"/api/questions/{questionId}/approve", null, 200);
            var firstQuiz = await Quiz(teacher, courseId, "Quiz One", questionId);
            var secondQuiz = await Quiz(teacher, courseId, "Quiz Two", questionId);
            var draft = await Send(teacher, "POST", "/api/quizzes", new { courseId, title = "Unpublished" }, 201);
            var materialId = await Upload(teacher, courseId, topicId);
            foreach (var student in new[] { studentA, studentB, studentC })
                await Send(student, "POST", "/api/courses/enroll", new { joinCode = course!["joinCode"]!.GetValue<string>() }, 200);
            var before = await Dashboard(studentA);
            Check(Int(before, "enrolledCourseCount") == 1 && Int(before, "publishedQuizCount") == 2 && Int(before, "completedQuizCount") == 0 &&
                before["averageScore"] == null && Number(before, "quizCompletionPercent") == 0, "published quizzes count toward progress but draft and absent attempts do not count");
            var perfect = await Submit(studentA, firstQuiz, questionId, correctOption);
            Check(perfect["score"]!.GetValue<decimal>() == 10, "dashboard fixture obtains ten through backend scoring");
            var wrongA = await Submit(studentA, secondQuiz, questionId, wrongOption);
            var wrongB = await Submit(studentB, firstQuiz, questionId, wrongOption);
            Check(wrongA["score"]!.GetValue<decimal>() == 0 && wrongB["score"]!.GetValue<decimal>() == 0, "wrong submissions obtain zero through backend scoring");
            await Send(studentA, "POST", $"/api/quizzes/{firstQuiz}/submit", new { answers = new[] { new { questionId, selectedOptionId = correctOption } } }, 409);
            var dashboard = await Dashboard(studentA);
            Check(Int(dashboard, "completedQuizCount") == 2 && Int(dashboard, "publishedQuizCount") == 2 && Number(dashboard, "quizCompletionPercent") == 100 &&
                dashboard["averageScore"]!.GetValue<decimal>() == 5 && dashboard["recentAttempts"]!.AsArray().Count == 2,
                "own dashboard aggregates two actual submitted quizzes to full completion and score five");
            var progress = dashboard["courseProgress"]!.AsArray().Single()!;
            Check(Int(progress, "courseId") == courseId && progress["code"]!.GetValue<string>() == "PROGRESS" && Int(progress, "completedQuizCount") == 2 &&
                Number(progress, "quizCompletionPercent") == 100 && progress["averageScore"]!.GetValue<decimal>() == 5, "course progress uses course identity and scored attempts");
            var weak = dashboard["weakTopics"]!.AsArray().Single()!;
            Check(Int(weak, "courseId") == courseId && Int(weak, "topicId") == topicId && Number(weak, "accuracy") == 50 && Int(weak, "correctAnswerCount") == 1 &&
                Int(weak, "totalAnswerCount") == 2 && weak["reason"]!.GetValue<string>().Contains("1/2"), "student weak topic records own correct and total answers with reason");
            var rec = dashboard["recommendations"]!.AsArray().Single()!;
            Check(Int(rec, "materialId") == materialId && Number(rec, "accuracy") == 50 && rec["source"]!.GetValue<string>() == "RuleBased" &&
                rec["reason"]!.GetValue<string>().Contains("60%") && rec["downloadUrl"]!.GetValue<string>() == $"/api/materials/{materialId}/download",
                "review recommendations name the rule and link only related available material");
            var result = await Send(studentA, "GET", $"/api/attempts/{Int(wrongA, "attemptId")}/result", null, 200);
            Check(result!["recommendations"]!.AsArray().Single()!["source"]!.GetValue<string>() == "RuleBased" &&
                result["recommendations"]!.AsArray().Single()!["reason"]!.GetValue<string>().Contains("0/1"), "individual result also explains its rule based recommendation");
            await Send(studentB, "GET", $"/api/attempts/{Int(wrongA, "attemptId")}/result", null, 403);
            var ownB = await Dashboard(studentB);
            Check(Int(ownB, "completedQuizCount") == 1 && ownB["averageScore"]!.GetValue<decimal>() == 0 && Number(ownB, "quizCompletionPercent") == 50 &&
                ownB["recentAttempts"]!.AsArray().Count == 1 && Number(ownB["weakTopics"]!.AsArray().Single()!, "accuracy") == 0,
                "second student sees only own zero score and half completion");
            var ownC = await Dashboard(studentC);
            Check(ownC["averageScore"] == null && ownC["recentAttempts"]!.AsArray().Count == 0 && ownC["weakTopics"]!.AsArray().Count == 0 &&
                ownC["recommendations"]!.AsArray().Count == 0, "enrolled student with no submissions has no average or invented recommendations");
            var stats = await Stats(teacher, courseId);
            Check(Int(stats, "studentCount") == 3 && Int(stats, "publishedQuizCount") == 2 && Int(stats, "submittedAttemptCount") == 3 &&
                stats["averageScore"]!.GetValue<decimal>() == 3.33m, "owner statistics aggregate three submitted attempts and round their real mean");
            var quizOne = stats["quizStatistics"]!.AsArray().Single(q => Int(q!, "quizId") == firstQuiz)!;
            var quizTwo = stats["quizStatistics"]!.AsArray().Single(q => Int(q!, "quizId") == secondQuiz)!;
            Check(Int(quizOne, "submittedStudentCount") == 2 && quizOne["averageScore"]!.GetValue<decimal>() == 5 && Int(quizTwo, "submittedStudentCount") == 1 &&
                quizTwo["averageScore"]!.GetValue<decimal>() == 0, "per quiz statistics count distinct submitters and retain actual zero averages");
            var studentStats = stats["studentStatistics"]!.AsArray();
            Check(studentStats.Single(s => Int(s!, "studentId") == a.Id)!["averageScore"]!.GetValue<decimal>() == 5 &&
                studentStats.Single(s => Int(s!, "studentId") == b.Id)!["averageScore"]!.GetValue<decimal>() == 0 &&
                studentStats.Single(s => Int(s!, "studentId") == c.Id)!["averageScore"] == null, "roster statistics distinguish five, zero and missing scores");
            var courseWeak = stats["weakTopics"]!.AsArray().Single()!;
            Check(Number(courseWeak, "accuracy") == 33.3 && Int(courseWeak, "incorrectAnswerCount") == 2 && Int(courseWeak, "totalAnswerCount") == 3 &&
                Int(courseWeak, "affectedStudentCount") == 2, "course weak topics aggregate actual mistakes and distinct affected learners");
            Check(!stats.ToJsonString().Contains("isCorrect", StringComparison.OrdinalIgnoreCase) && !stats.ToJsonString().Contains("selectedOption", StringComparison.OrdinalIgnoreCase) &&
                !stats.ToJsonString().Contains("correctOption", StringComparison.OrdinalIgnoreCase), "statistics contract excludes individual answers and correct options");

            // An unfinished legacy row must not affect scores, recommendations or disclose answer keys.
            int unfinishedId;
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
                var unfinished = new QuizAttempt { StudentId = a.Id, QuizId = Int(draft!, "id"), Status = "InProgress", Score = 10,
                    StudentAnswers = [new StudentAnswer { QuestionId = questionId, SelectedOptionId = correctOption, IsCorrect = true }] };
                db.QuizAttempts.Add(unfinished); await db.SaveChangesAsync(); unfinishedId = unfinished.Id;
            }
            Check((await Dashboard(studentA))["averageScore"]!.GetValue<decimal>() == 5 && Int(await Stats(teacher, courseId), "submittedAttemptCount") == 3,
                "unfinished attempt is excluded from dashboard and course aggregate");
            Check((await Send(studentA, "GET", "/api/students/me/attempts", null, 200))!.AsArray().Count == 2 &&
                (await Send(teacher, "GET", $"/api/courses/{courseId}/attempts", null, 200))!.AsArray().Count == 3,
                "student and lecturer history exclude unfinished attempts");
            await Send(studentA, "GET", $"/api/attempts/{unfinishedId}/result", null, 404);
            await Send(teacher, "DELETE", $"/api/materials/{materialId}", null, 204);
            var fallback = (await Dashboard(studentA))["recommendations"]!.AsArray().Single()!;
            Check(fallback["materialId"] == null && fallback["downloadUrl"] == null && fallback["source"]!.GetValue<string>() == "RuleBased" &&
                fallback["reason"]!.GetValue<string>().Contains("1/2"), "deleted material is removed from recommendations while weak topic reasoning remains");
            Check((await Send(studentA, "GET", "/api/students/me/recommendations", null, 200))!.AsArray().Single()!["materialId"] == null,
                "standalone recommendations also exclude deleted material");
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
                db.Enrollments.Remove(await db.Enrollments.SingleAsync(e => e.CourseId == courseId && e.StudentId == b.Id));
                await db.SaveChangesAsync();
            }
            var removed = await Dashboard(studentB);
            Check(Int(removed, "enrolledCourseCount") == 0 && removed["averageScore"] == null && removed["recentAttempts"]!.AsArray().Count == 0 &&
                removed["recommendations"]!.AsArray().Count == 0, "withdrawn membership removes all course data from current dashboard");
            var afterRemoval = await Stats(teacher, courseId);
            Check(Int(afterRemoval, "studentCount") == 2 && Int(afterRemoval, "submittedAttemptCount") == 2 && afterRemoval["averageScore"]!.GetValue<decimal>() == 5 &&
                Int(afterRemoval["weakTopics"]!.AsArray().Single()!, "affectedStudentCount") == 1, "course current statistics exclude withdrawn learner and answers");
            Check((await Send(studentB, "GET", "/api/students/me/attempts", null, 200))!.AsArray().Count == 1 &&
                (await Send(teacher, "GET", $"/api/courses/{courseId}/attempts", null, 200))!.AsArray().Count == 3,
                "historical submitted results survive membership removal");
        }
        using (var restarted = new TestHost())
        {
            using var teacher = await Login(restarted, "lecturer", password);
            using var student = await Login(restarted, "progress_a", password);
            Check((await Dashboard(student))["averageScore"]!.GetValue<decimal>() == 5 && Int(await Stats(teacher, courseId), "submittedAttemptCount") == 2,
                "real scores and current membership statistics survive host restart");
        }
    }

    private static int Int(JsonNode value, string field) => value[field]!.GetValue<int>();
    private static double Number(JsonNode value, string field) => value[field]!.GetValue<double>();
    private static async Task<JsonNode> Dashboard(HttpClient client) => (await Send(client, "GET", "/api/students/me/dashboard", null, 200))!;
    private static async Task<JsonNode> Stats(HttpClient client, int courseId) => (await Send(client, "GET", $"/api/courses/{courseId}/statistics", null, 200))!;
    private static async Task<JsonNode> Submit(HttpClient client, int quizId, int questionId, int selectedOptionId)
        => (await Send(client, "POST", $"/api/quizzes/{quizId}/submit", new { answers = new[] { new { questionId, selectedOptionId } } }, 200))!;
    private static async Task<int> Quiz(HttpClient teacher, int courseId, string title, int questionId)
    {
        var quiz = await Send(teacher, "POST", "/api/quizzes", new { courseId, title }, 201); var id = Int(quiz!, "id");
        await Send(teacher, "POST", $"/api/quizzes/{id}/publish", new { questionIds = new[] { questionId } }, 200); return id;
    }
    private static async Task<int> Upload(HttpClient teacher, int courseId, int topicId)
    {
        using var body = new MultipartFormDataContent(); body.Add(new StringContent(topicId.ToString()), "topicId");
        body.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("Only the owning lecturer may modify course content.")), "file", "review.md");
        using var response = await teacher.PostAsync($"/api/courses/{courseId}/materials", body);
        var raw = await response.Content.ReadAsStringAsync(); Check((int)response.StatusCode == 201, "review material uploads through storage and extractor");
        return Int(JsonNode.Parse(raw)!, "id");
    }
    private static async Task<(HttpClient Client, int Id)> Register(TestHost host, string username, string password)
    {
        using var anonymous = host.CreateClient();
        var auth = await Send(anonymous, "POST", "/api/auth/register", new { username, fullName = username, password }, 201);
        return (Authenticated(host, auth!["accessToken"]!.GetValue<string>()), Int(auth, "userId"));
    }
    private static async Task<HttpClient> Login(TestHost host, string username, string password)
    {
        using var anonymous = host.CreateClient(); var auth = await Send(anonymous, "POST", "/api/auth/login", new { username, password }, 200);
        return Authenticated(host, auth!["accessToken"]!.GetValue<string>());
    }
    private static HttpClient Authenticated(TestHost host, string token)
    { var client = host.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", token); return client; }
    private static async Task<JsonNode?> Send(HttpClient client, string method, string path, object? body, int expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path); if (body != null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request); var raw = await response.Content.ReadAsStringAsync();
        Check((int)response.StatusCode == expected, $"{method} {path}: {(int)response.StatusCode} expected {expected}; {((int)response.StatusCode == expected ? "" : raw)}");
        return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
    }
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException("FAIL " + label); _checks++; Console.WriteLine("PASS " + label); }
    private sealed class TestHost : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "LMS.sln"))) root = root.Parent;
            if (root == null) throw new InvalidOperationException("Cannot locate BE/LMS.sln");
            builder.UseContentRoot(Path.Combine(root.FullName, "Services", "Core", "LMS.Core.API"));
            builder.UseEnvironment("Development"); builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }
}
