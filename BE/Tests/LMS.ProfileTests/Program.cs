using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LMS.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LMS.ProfileTests;

internal static class ProfileEntry
{
    private static int _checks;

    private static async Task Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lms-profile-" + Guid.NewGuid().ToString("N"));
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
            Console.WriteLine($"PROFILE TESTS PASSED: {_checks} assertions; isolated SQLite/HTTP only.");
        }
        finally
        {
            foreach (var setting in originals) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            SqliteConnection.ClearAllPools();
            var fullDirectory = Path.GetFullPath(directory);
            if (Path.GetDirectoryName(fullDirectory) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) ||
                !Path.GetFileName(fullDirectory).StartsWith("lms-profile-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe test cleanup target");
            Directory.Delete(fullDirectory, true);
        }
    }

    private static async Task Run(string password)
    {
        string savedToken;
        int courseId;
        using (var host = new TestHost())
        {
            using var anonymous = host.CreateClient();
            using var admin = await Login(host, "admin", password);
            using var teacher = await Login(host, "lecturer", password);
            await Send(anonymous, "PUT", "/api/auth/me", new { fullName = "Anonymous", email = "test@example.com" }, 401);
            await Send(anonymous, "POST", "/api/auth/register", new { username = "invalid_email", fullName = "Invalid Email", password, email = "not-an-email" }, 400);
            var registration = await Send(anonymous, "POST", "/api/auth/register", new
            {
                username = "profile_student", fullName = "Profile Student", password, email = " initial@example.com ", role = "Admin", isLocked = true
            }, 201);
            savedToken = registration!["accessToken"]!.GetValue<string>();
            var studentId = registration["userId"]!.GetValue<int>();
            using var student = Authenticated(host, savedToken);
            var profile = await Send(student, "GET", "/api/auth/me", null, 200);
            Check(profile!["email"]!.GetValue<string>() == "initial@example.com" && profile["createdAt"] != null, "new registration stores email and creation date");
            Check(profile["role"]!.GetValue<string>() == "Student" && !profile["isLocked"]!.GetValue<bool>() && profile["courses"]!.AsArray().Count == 0, "registration cannot set protected fields; empty membership is real");
            Check(!profile.ToJsonString().Contains("password", StringComparison.OrdinalIgnoreCase), "profile excludes password data");

            var updated = await Send(student, "PUT", "/api/auth/me", new
            {
                fullName = " Updated Student ", email = " student@example.com ", role = "Admin", isLocked = true, username = "changed", userId = 1
            }, 200);
            Check(updated!["fullName"]!.GetValue<string>() == "Updated Student" && updated["email"]!.GetValue<string>() == "student@example.com", "self update saves permitted fields with trimming");
            Check(updated["username"]!.GetValue<string>() == "profile_student" && updated["role"]!.GetValue<string>() == "Student" && !updated["isLocked"]!.GetValue<bool>() && updated["userId"]!.GetValue<int>() == studentId, "profile update cannot modify username, role, lock or another account");
            await Send(student, "PUT", "/api/auth/me", new { fullName = "x", email = "valid@example.com" }, 400);
            await Send(student, "PUT", "/api/auth/me", new { fullName = "Valid Name", email = "bad email@example.com" }, 400);
            await Send(student, "PUT", "/api/auth/me", new { fullName = "Valid Name", email = new string('a', 255) + "@example.com" }, 400);
            var unchanged = await Send(student, "GET", "/api/auth/me", null, 200);
            Check(unchanged!["fullName"]!.GetValue<string>() == "Updated Student", "invalid update preserves stored name");
            var cleared = await Send(student, "PUT", "/api/auth/me", new { fullName = "Updated Student", email = " " }, 200);
            Check(cleared!["email"] == null, "optional email can be cleared");
            await Send(student, "PUT", "/api/auth/me", new { fullName = "Updated Student", email = "student@example.com" }, 200);
            await Send(student, "GET", "/api/admin/users?search=profile", null, 403);

            var lecturer = await Send(admin, "POST", "/api/admin/users/lecturers", new
            {
                username = "profile_teacher", fullName = "Other Teacher", password, email = "teacher@example.com"
            }, 201);
            Check(lecturer!["email"]!.GetValue<string>() == "teacher@example.com" && lecturer["createdAt"] != null, "admin lecturer creation includes email and creation date");
            using var otherTeacher = await Login(host, "profile_teacher", password);
            await Send(admin, "POST", "/api/admin/users/lecturers", new { username = "bad_teacher", fullName = "Bad Teacher", password, email = "bad" }, 400);
            var filtered = await Send(admin, "GET", "/api/admin/users?search=STUDENT%40EXAMPLE.COM&role=Student&isLocked=false&pageSize=1", null, 200);
            Check(filtered!["total"]!.GetValue<int>() == 1 && filtered["items"]!.AsArray().Single()!["id"]!.GetValue<int>() == studentId, "admin filters combine email search, role and status before pagination");
            Check((await Send(admin, "GET", "/api/admin/users?search=no_such_account", null, 200))!["total"]!.GetValue<int>() == 0, "admin empty search has zero actual matches");
            await Send(admin, "GET", "/api/admin/users?role=SuperAdmin", null, 400);
            await Send(admin, "GET", "/api/admin/users?isLocked=invalid", null, 400);
            await Send(admin, "GET", "/api/admin/users?search=" + new string('a', 151), null, 400);

            var course = await Send(teacher, "POST", "/api/courses", new { code = "PROFILE101", title = "Profile Course" }, 201);
            courseId = course!["id"]!.GetValue<int>();
            var code = course["joinCode"]!.GetValue<string>();
            await Send(student, "GET", $"/api/courses/{courseId}/students", null, 403);
            await Send(otherTeacher, "GET", $"/api/courses/{courseId}/students", null, 403);
            await Send(student, "POST", "/api/courses/enroll", new { joinCode = code }, 200);
            await Send(student, "POST", "/api/courses/enroll", new { joinCode = code }, 409);
            var membership = (await Send(student, "GET", "/api/auth/me", null, 200))!["courses"]!.AsArray().Single()!;
            Check(membership["courseId"]!.GetValue<int>() == courseId && membership["relationship"]!.GetValue<string>() == "Enrolled" && membership["enrolledAt"] != null, "student profile contains joined course and enrollment date");
            var teaching = (await Send(teacher, "GET", "/api/auth/me", null, 200))!["courses"]!.AsArray().Single(c => c!["courseId"]!.GetValue<int>() == courseId)!;
            Check(teaching["relationship"]!.GetValue<string>() == "Teaching" && teaching["enrolledAt"] == null && teaching["joinCode"] == null, "lecturer profile lists taught course without invented enrollment date");

            var roster = (await Send(teacher, "GET", $"/api/courses/{courseId}/students", null, 200))!.AsArray().Single()!;
            Check(roster["email"]!.GetValue<string>() == "student@example.com" && !roster["isLocked"]!.GetValue<bool>() && roster["enrolledAt"] != null, "roster includes actual email, account status and enrollment date");
            Check(roster["publishedQuizCount"]!.GetValue<int>() == 0 && roster["completedQuizCount"]!.GetValue<int>() == 0 && roster["averageScore"] == null && roster["learningStatus"]!.GetValue<string>() == "NotStarted", "empty roster progress has zero counts and no fabricated score");
            var topic = await Send(teacher, "POST", $"/api/courses/{courseId}/topics", new { name = "Profile Topic" }, 201);
            var question = await Send(teacher, "POST", "/api/questions", new
            {
                courseId, topicId = topic!["id"]!.GetValue<int>(), content = "Which answer is correct?", explanation = "First answer", options = new[]
                {
                    new { content = "First", isCorrect = true }, new { content = "Second", isCorrect = false },
                    new { content = "Third", isCorrect = false }, new { content = "Fourth", isCorrect = false }
                }
            }, 201);
            var questionId = question!["id"]!.GetValue<int>();
            var correctOptionId = question["options"]!.AsArray().Single(o => o!["isCorrect"]!.GetValue<bool>())!["id"]!.GetValue<int>();
            await Send(teacher, "POST", $"/api/questions/{questionId}/approve", null, 200);
            var firstQuiz = await Send(teacher, "POST", "/api/quizzes", new { courseId, title = "First published quiz" }, 201);
            var quizId = firstQuiz!["id"]!.GetValue<int>();
            await Send(teacher, "POST", $"/api/quizzes/{quizId}/publish", new { questionIds = new[] { questionId } }, 200);
            var secondQuiz = await Send(teacher, "POST", "/api/quizzes", new { courseId, title = "Second published quiz" }, 201);
            var secondQuizId = secondQuiz!["id"]!.GetValue<int>();
            await Send(teacher, "POST", $"/api/quizzes/{secondQuizId}/publish", new { questionIds = new[] { questionId } }, 200);
            await Send(teacher, "POST", "/api/quizzes", new { courseId, title = "Unpublished excluded" }, 201);
            await Send(student, "POST", $"/api/quizzes/{quizId}/submit", new { answers = new[] { new { questionId, selectedOptionId = correctOptionId } } }, 200);
            var progress = (await Send(teacher, "GET", $"/api/courses/{courseId}/students", null, 200))!.AsArray().Single()!;
            Check(progress["publishedQuizCount"]!.GetValue<int>() == 2 && progress["completedQuizCount"]!.GetValue<int>() == 1 && progress["quizCompletionPercent"]!.GetValue<double>() == 50 && progress["averageScore"]!.GetValue<decimal>() == 10 && progress["learningStatus"]!.GetValue<string>() == "InProgress", "roster calculates real quiz completion and average, excludes Draft quiz");
            await Send(student, "POST", $"/api/quizzes/{secondQuizId}/submit", new { answers = new[] { new { questionId, selectedOptionId = correctOptionId } } }, 200);
            var completed = (await Send(teacher, "GET", $"/api/courses/{courseId}/students", null, 200))!.AsArray().Single()!;
            Check(completed["quizCompletionPercent"]!.GetValue<double>() == 100 && completed["learningStatus"]!.GetValue<string>() == "Completed", "completed roster state reflects every published quiz submitted");

            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
                var legacy = await db.Users.SingleAsync(u => u.Username == "profile_teacher");
                legacy.CreatedAt = null;
                await db.SaveChangesAsync();
            }
            Check((await Send(otherTeacher, "GET", "/api/auth/me", null, 200))!["createdAt"] == null, "unknown legacy creation date remains null");
            var otherId = lecturer["id"]!.GetValue<int>();
            await Send(admin, "PATCH", $"/api/admin/users/{otherId}/lock", new { isLocked = true }, 200);
            await Send(otherTeacher, "PUT", "/api/auth/me", new { fullName = "Locked Update", email = "no@example.com" }, 401);
            var locked = await Send(admin, "GET", "/api/admin/users?role=Lecturer&isLocked=true&search=teacher%40example.com", null, 200);
            Check(locked!["total"]!.GetValue<int>() == 1 && locked["items"]!.AsArray().Single()!["createdAt"] == null, "admin locked account filtering preserves unknown dates");
        }
        using (var restarted = new TestHost())
        using (var student = Authenticated(restarted, savedToken))
        {
            var persisted = await Send(student, "GET", "/api/auth/me", null, 200);
            Check(persisted!["email"]!.GetValue<string>() == "student@example.com" && persisted["fullName"]!.GetValue<string>() == "Updated Student" && persisted["courses"]!.AsArray().Any(c => c!["courseId"]!.GetValue<int>() == courseId), "profile edits and memberships persist across host restart");
        }
    }

    private static async Task<HttpClient> Login(TestHost host, string username, string password)
    {
        using var anonymous = host.CreateClient();
        var response = await Send(anonymous, "POST", "/api/auth/login", new { username, password }, 200);
        return Authenticated(host, response!["accessToken"]!.GetValue<string>());
    }

    private static HttpClient Authenticated(TestHost host, string token)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<JsonNode?> Send(HttpClient client, string method, string path, object? body, int expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        Check((int)response.StatusCode == expected, $"{method} {path}: {(int)response.StatusCode} expected {expected}");
        var content = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + label);
        _checks++;
        Console.WriteLine("PASS " + label);
    }

    private sealed class TestHost : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "LMS.sln"))) root = root.Parent;
            if (root == null) throw new InvalidOperationException("Cannot locate BE/LMS.sln");
            builder.UseContentRoot(Path.Combine(root.FullName, "Services", "Core", "LMS.Core.API"));
            builder.UseEnvironment("Development");
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }
}
