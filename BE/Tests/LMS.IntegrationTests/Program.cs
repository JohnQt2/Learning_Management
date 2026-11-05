using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LMS.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace LMS.IntegrationTests;

internal static class IntegrationEntry
{
    private static int _checks;

    private static async Task Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lms-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var password = "Test!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)) + "a1";
        var jwtKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
        var settings = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development", ["Database__Provider"] = "Sqlite",
            ["DotEnv__Enabled"] = "false",
            ["Database__AutoMigrate"] = "true", ["ConnectionStrings__Sqlite"] = "Data Source=" + Path.Combine(directory, "test.sqlite"),
            ["Storage__RootPath"] = Path.Combine(directory, "Uploads"), ["Demo__Seed"] = "true", ["Demo__Password"] = password,
            ["Jwt__Key"] = jwtKey, ["Ai__Mode"] = "Disabled", ["Bootstrap__AdminUsername"] = "",
            ["Cors__AllowedOrigins__0"] = "http://localhost:5173", ["RateLimiting__AuthPermitLimit"] = "100"
        };
        var original = settings.ToDictionary(x => x.Key, x => Environment.GetEnvironmentVariable(x.Key));
        foreach (var setting in settings) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        try
        {
            await Run(directory, password, jwtKey);
            Console.WriteLine($"PASS HTTP integration: {_checks} assertions; isolated SQLite persisted across host restart. Live Gemini and browser UI NOT RUN.");
        }
        finally
        {
            foreach (var setting in original) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            SqliteConnection.ClearAllPools();
            // Only this test-created directory can be deleted.
            Directory.Delete(directory, true);
        }
    }

    private static async Task Run(string directory, string password, string jwtKey)
    {
        int courseId, topicId, materialId, quizId, attemptId, studentId;
        string studentToken;
        using (var factory = new TestHost())
        {
            using var publicClient = factory.CreateClient();
            await Send(publicClient, "GET", "/api/health", null, 200, "health");
            await Send(publicClient, "GET", "/api/health/ready", null, 200, "database readiness");
            await Send(publicClient, "GET", "/api/auth/me", null, 401, "missing JWT");
            using var invalid = factory.CreateClient();
            invalid.DefaultRequestHeaders.Authorization = new("Bearer", "invalid.jwt.token");
            await Send(invalid, "GET", "/api/auth/me", null, 401, "invalid JWT");
            await Send(publicClient, "POST", "/api/auth/login", new { username = "lecturer", password = "incorrect" }, 401, "wrong password");
            await Send(publicClient, "POST", "/api/auth/login", new { username = "lecturer' OR '1'='1", password = "incorrect" }, 401, "SQL injection input does not bypass authentication");
            var lecturer = await Login(factory, "lecturer", password);
            using var teacher = lecturer.Client;
            var administrator = await Login(factory, "admin", password);
            using var admin = administrator.Client;
            var registration = await Send(publicClient, "POST", "/api/auth/register",
                new { username = "http_student", fullName = "Sinh viên HTTP", password, role = "Admin" }, 201, "student registration");
            studentToken = registration!["accessToken"]!.GetValue<string>();
            studentId = registration["userId"]!.GetValue<int>();
            using var student = Authenticated(factory, studentToken);
            var profile = await Send(student, "GET", "/api/auth/me", null, 200, "student profile");
            Check(profile!["role"]!.GetValue<string>() == "Student" && profile["fullName"]!.GetValue<string>() == "Sinh viên HTTP", "no role escalation / profile full name");
            Check(!profile.ToJsonString().Contains("password", StringComparison.OrdinalIgnoreCase), "profile excludes password hash");
            await Send(publicClient, "POST", "/api/auth/register", new { username = "http_student", fullName = "Duplicate", password }, 409, "duplicate username");
            await Send(student, "POST", "/api/courses", new { code = "NOPE", title = "Denied" }, 403, "student lecturer API denied");
            await Send(teacher, "GET", "/api/admin/users", null, 403, "lecturer admin API denied");
            await Send(admin, "POST", "/api/admin/users/lecturers", new { username = "teacher_b", fullName = "Giảng viên B", password }, 201, "admin creates lecturer");
            var secondLecturer = await Login(factory, "teacher_b", password);
            using var teacherB = secondLecturer.Client;
            var course = await Send(teacher, "POST", "/api/courses", new { code = "HTTP101", title = "Khóa học HTTP", description = "Integration" }, 201, "create course");
            courseId = course!["id"]!.GetValue<int>();
            var joinCode = course["joinCode"]!.GetValue<string>();
            Check(joinCode.Length >= 8, "join code strength");
            await Send(teacher, "POST", "/api/courses", new { code = "HTTP101", title = "Duplicate" }, 409, "duplicate course code");
            await Send(teacherB, "PUT", $"/api/courses/{courseId}", new { title = "IDOR" }, 403, "other lecturer cannot edit course");
            await Send(student, "GET", $"/api/courses/{courseId}", null, 403, "unenrolled course denied");
            await Send(teacher, "PUT", $"/api/courses/{courseId}", new { title = "Course updated", description = "Lưu thật" }, 200, "edit course");
            var topic = await Send(teacher, "POST", $"/api/courses/{courseId}/topics", new { name = "Topic" }, 201, "create topic");
            topicId = topic!["id"]!.GetValue<int>();
            await Send(teacher, "PUT", $"/api/courses/{courseId}/topics/{topicId}", new { name = "Chủ đề HTTP" }, 200, "edit topic");
            await Send(teacherB, "PUT", $"/api/courses/{courseId}/topics/{topicId}", new { name = "IDOR" }, 403, "other lecturer topic denied");
            var txtBytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes("Nội dung học tập UTF-8. HTTP truyền request và response.")).ToArray();
            var material = await Upload(teacher, courseId, topicId, "learning.txt", txtBytes, 201, "TXT upload BOM Unicode");
            materialId = material!["id"]!.GetValue<int>();
            var pdfBytes = PdfFixture();
            var pdf = await Upload(teacher, courseId, topicId, "learning.pdf", pdfBytes, 201, "real PDF upload");
            await Send(student, "GET", $"/api/materials/{materialId}/download", null, 403, "unenrolled private download denied");
            await Send(publicClient, "GET", $"/api/materials/{materialId}/download", null, 401, "anonymous private download denied");
            await Upload(teacher, courseId, topicId, "unsafe.exe", [1, 2, 3], 400, "unsafe file extension denied");
            await Upload(teacher, courseId, topicId, "fake.pdf", Encoding.UTF8.GetBytes("not a PDF"), 400, "fake PDF denied");
            await Send(student, "POST", "/api/courses/enroll", new { joinCode = "WRONG" }, 404, "invalid join code");
            var enrolls = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => student.PostAsJsonAsync("/api/courses/enroll", new { joinCode })));
            Check(enrolls.Select(r => (int)r.StatusCode).Order().SequenceEqual(new[] { 200, 409 }), "concurrent enrollment exactly one success");
            foreach (var response in enrolls) response.Dispose();
            var ownCourse = await Send(student, "GET", $"/api/courses/{courseId}", null, 200, "enrolled course");
            Check(ownCourse!["joinCode"] is null, "student cannot read join code");
            var members = await Send(teacher, "GET", $"/api/courses/{courseId}/students", null, 200, "course student roster");
            Check(members!.AsArray().Count == 1, "one enrollment stored");
            Check((await student.GetByteArrayAsync($"/api/materials/{materialId}/download")).SequenceEqual(txtBytes), "TXT download byte identity");
            Check((await student.GetByteArrayAsync($"/api/materials/{pdf!["id"]!.GetValue<int>()}/download")).SequenceEqual(pdfBytes), "PDF download byte identity");
            var status = await Send(teacher, "GET", "/api/ai/questions/status", null, 200, "AI configuration status");
            Check(!status!["enabled"]!.GetValue<bool>(), "AI disabled explicitly no mock result");
            await Send(teacher, "POST", "/api/ai/questions/generate", new { materialId, count = 3, difficulty = "Easy" }, 503, "disabled AI fails visibly");
            var questionIds = new List<int>();
            var correct = new Dictionary<int, int>();
            var incorrect = new Dictionary<int, int>();
            for (var index = 0; index < 5; index++)
            {
                var question = await Send(teacher, "POST", "/api/questions", Question(courseId, topicId, $"Câu hỏi {index + 1}?"), 201, $"create question {index + 1}");
                var questionId = question!["id"]!.GetValue<int>();
                if (index == 0)
                    question = await Send(teacher, "PUT", $"/api/questions/{questionId}", new { content = "Câu hỏi đã sửa?", explanation = "Giải thích", options = Options() }, 200, "edit Draft");
                questionIds.Add(questionId);
                correct[questionId] = question!["options"]!.AsArray().Single(x => x!["isCorrect"]!.GetValue<bool>())!["id"]!.GetValue<int>();
                incorrect[questionId] = question["options"]!.AsArray().First(x => !x!["isCorrect"]!.GetValue<bool>())!["id"]!.GetValue<int>();
                await Send(teacher, "POST", $"/api/questions/{questionId}/approve", new { }, 200, $"approve question {index + 1}");
            }
            await Send(student, "GET", $"/api/courses/{courseId}/questions", null, 403, "student answer bank denied");
            await Send(teacher, "POST", "/api/questions", new { courseId, topicId, content = "bad", options = new object?[] { null, null, null, null } }, 400, "null question options validation");
            var quiz = await Send(teacher, "POST", "/api/quizzes", new { courseId, title = "HTTP Quiz", description = "Mô tả Quiz" }, 201, "create Draft quiz");
            quizId = quiz!["id"]!.GetValue<int>();
            await Send(teacher, "POST", $"/api/quizzes/{quizId}/publish", new { questionIds = Array.Empty<int>() }, 400, "empty quiz cannot publish");
            await Send(student, "GET", $"/api/quizzes/{quizId}", null, 404, "Draft quiz cannot take");
            await Send(teacher, "PUT", $"/api/quizzes/{quizId}", new { title = "HTTP Quiz", description = "Mô tả Quiz", questionIds }, 200, "save Draft quiz selection");
            var authoring = await Send(teacher, "GET", $"/api/quizzes/{quizId}/authoring", null, 200, "quiz authoring preview");
            Check(authoring!["questions"]!.AsArray().Count == 5, "authoring saved five questions");
            await Send(teacher, "POST", $"/api/quizzes/{quizId}/publish", new { questionIds }, 200, "publish quiz");
            var take = await Send(student, "GET", $"/api/quizzes/{quizId}", null, 200, "student takes quiz");
            Check(!take!.ToJsonString().Contains("isCorrect", StringComparison.OrdinalIgnoreCase) && !take.ToJsonString().Contains("explanation", StringComparison.OrdinalIgnoreCase), "quiz response contains no answer key or explanation");
            await Send(teacher, "PUT", $"/api/questions/{questionIds[0]}", new { content = "Changed history", options = Options() }, 409, "approved question immutable");
            await Send(student, "POST", $"/api/quizzes/{quizId}/submit", new { answers = new object?[] { null } }, 400, "null answer validation");
            var answers = questionIds.Select((id, index) => new { questionId = id, selectedOptionId = index < 2 ? correct[id] : incorrect[id] }).ToArray();
            var submissions = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => student.PostAsJsonAsync($"/api/quizzes/{quizId}/submit", new { answers, score = 10, isCorrect = true })));
            Check(submissions.Select(r => (int)r.StatusCode).Order().SequenceEqual(new[] { 200, 409 }), "concurrent quiz submission exactly one success");
            var submit = JsonNode.Parse(await submissions.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadAsStringAsync())!;
            attemptId = submit["attemptId"]!.GetValue<int>();
            Check(submit["score"]!.GetValue<decimal>() == 4 && submit["correctCount"]!.GetValue<int>() == 2, "backend score ignores tampered client score");
            foreach (var response in submissions) response.Dispose();
            await Send(student, "POST", $"/api/quizzes/{quizId}/submit", new { answers }, 409, "duplicate submission denied");
            var result = await Send(student, "GET", $"/api/attempts/{attemptId}/result", null, 200, "detailed student result");
            Check(result!["answers"]!.AsArray().Count == 5 && result["wrongCount"]!.GetValue<int>() == 3, "saved answer feedback and counts");
            var recommendations = await Send(student, "GET", "/api/students/me/recommendations", null, 200, "personal recommendations");
            Check(recommendations!.AsArray().Any(x => x!["topicId"]!.GetValue<int>() == topicId && x["accuracy"]!.GetValue<double>() == 40), "weak topic accuracy from real answers");
            await Send(teacher, "GET", $"/api/courses/{courseId}/attempts", null, 200, "lecturer results");
            await Send(teacherB, "GET", $"/api/courses/{courseId}/attempts", null, 403, "other lecturer results denied");
            await Send(teacherB, "GET", $"/api/attempts/{attemptId}/result", null, 403, "other lecturer result IDOR denied");
            var seededStudent = await Login(factory, "student", password);
            using var otherStudent = seededStudent.Client;
            await Send(otherStudent, "GET", $"/api/attempts/{attemptId}/result", null, 403, "other student result IDOR denied");
            await Send(otherStudent, "GET", $"/api/quizzes/{quizId}", null, 403, "unenrolled quiz denied");
            using var expired = Authenticated(factory, ExpiredJwt(studentId, jwtKey));
            await Send(expired, "GET", "/api/auth/me", null, 401, "expired signed JWT");
            await Send(admin, "PATCH", $"/api/admin/users/{studentId}/lock", new { isLocked = true }, 200, "lock account");
            await Send(student, "GET", "/api/auth/me", null, 401, "old JWT rejected while locked");
            await Send(publicClient, "POST", "/api/auth/login", new { username = "http_student", password }, 401, "locked login denied");
            await Send(admin, "PATCH", $"/api/admin/users/{studentId}/lock", new { isLocked = false }, 200, "unlock account");
            await Send(student, "GET", "/api/auth/me", null, 401, "old JWT remains revoked after unlock");
            var newStudent = await Login(factory, "http_student", password);
            using var activeStudent = newStudent.Client;
            await Send(activeStudent, "POST", "/api/auth/change-password", new { currentPassword = "wrong", newPassword = password + "New" }, 400, "wrong current password");
            await Send(activeStudent, "POST", "/api/auth/change-password", new { currentPassword = password, newPassword = password + "New" }, 204, "change password");
            await Send(activeStudent, "GET", "/api/auth/me", null, 401, "password change revokes old JWT");
            await Send(publicClient, "POST", "/api/auth/login", new { username = "http_student", password }, 401, "old password denied");
            var changedLogin = await Login(factory, "http_student", password + "New");
            using var changedStudent = changedLogin.Client;
            await Send(changedStudent, "POST", "/api/auth/logout", new { }, 204, "server logout");
            await Send(changedStudent, "GET", "/api/auth/me", null, 401, "logout revokes JWT");
            using var corsRequest = new HttpRequestMessage(HttpMethod.Options, "/api/courses");
            corsRequest.Headers.Add("Origin", "https://untrusted.example");
            corsRequest.Headers.Add("Access-Control-Request-Method", "GET");
            using var cors = await publicClient.SendAsync(corsRequest);
            Check(!cors.Headers.Contains("Access-Control-Allow-Origin"), "untrusted CORS origin denied");
            using var allowedRequest = new HttpRequestMessage(HttpMethod.Options, "/api/courses");
            allowedRequest.Headers.Add("Origin", "http://localhost:5173");
            allowedRequest.Headers.Add("Access-Control-Request-Method", "GET");
            using var allowed = await publicClient.SendAsync(allowedRequest);
            Check(allowed.Headers.GetValues("Access-Control-Allow-Origin").Single() == "http://localhost:5173", "allowed CORS origin accepted");
            using var malformed = await publicClient.PostAsync("/api/auth/login", new StringContent("{invalid", Encoding.UTF8, "application/json"));
            Check(malformed.StatusCode == HttpStatusCode.BadRequest, "malformed JSON returns 400");
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            Check(await db.QuizAttempts.CountAsync() == 1 && await db.StudentAnswers.CountAsync() == 5, "one attempt and five answers persisted");
            Check((await db.Database.GetAppliedMigrationsAsync()).Count() == db.Database.GetMigrations().Count(), "all SQLite migrations applied");
        }
        // New web host + new EF contexts, same isolated on-disk SQLite/uploads.
        using (var restarted = new TestHost())
        {
            var login = await Login(restarted, "http_student", password + "New");
            using var student = login.Client;
            var course = await Send(student, "GET", $"/api/courses/{courseId}", null, 200, "course persists after host restart");
            Check(course!["title"]!.GetValue<string>() == "Course updated", "edited course persists");
            var result = await Send(student, "GET", $"/api/attempts/{attemptId}/result", null, 200, "result persists after host restart");
            Check(result!["score"]!.GetValue<decimal>() == 4, "score survives restart");
            Check((await student.GetByteArrayAsync($"/api/materials/{materialId}/download")).Length > 0, "uploaded file survives restart");
        }
    }

    private static object[] Options() => [new { content = "A", isCorrect = true }, new { content = "B", isCorrect = false }, new { content = "C", isCorrect = false }, new { content = "D", isCorrect = false }];
    private static object Question(int courseId, int topicId, string content) => new { courseId, topicId, content, explanation = "Giải thích", options = Options() };
    private static HttpClient Authenticated(TestHost factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }
    private static async Task<(HttpClient Client, int UserId)> Login(TestHost factory, string username, string password)
    {
        using var client = factory.CreateClient();
        var response = await Send(client, "POST", "/api/auth/login", new { username, password }, 200, $"login {username}");
        return (Authenticated(factory, response!["accessToken"]!.GetValue<string>()), response["userId"]!.GetValue<int>());
    }
    private static async Task<JsonNode?> Send(HttpClient client, string method, string path, object? body, int status, string label)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        Check((int)response.StatusCode == status, $"{label}: HTTP {(int)response.StatusCode}, expected {status}");
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }
    private static async Task<JsonNode?> Upload(HttpClient client, int course, int topic, string name, byte[] bytes, int status, string label)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", name);
        form.Add(new StringContent(topic.ToString()), "topicId");
        using var response = await client.PostAsync($"/api/courses/{course}/materials", form);
        Check((int)response.StatusCode == status, $"{label}: HTTP {(int)response.StatusCode}, expected {status}");
        return JsonNode.Parse(await response.Content.ReadAsStringAsync());
    }
    private static string ExpiredJwt(int userId, string key) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        "LMS-AI", "LMS-AI-Frontend", [new(ClaimTypes.NameIdentifier, userId.ToString()), new(ClaimTypes.Role, "Student"), new("sv", "0")],
        expires: DateTime.UtcNow.AddHours(-1), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));
    private static void Check(bool pass, string label)
    {
        if (!pass) throw new InvalidOperationException("FAIL " + label);
        _checks++;
        Console.WriteLine("PASS " + label);
    }
    private static byte[] PdfFixture()
    {
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>", "<< /Length 38 >>\nstream\nBT /F1 12 Tf 20 100 Td (LMS AI) Tj ET\nendstream", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = pdf.Length;
        pdf.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private sealed class TestHost : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "LMS.sln"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("Cannot locate BE/LMS.sln for the integration host");
            builder.UseContentRoot(Path.Combine(root.FullName, "Services", "Core", "LMS.Core.API"));
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }
}
