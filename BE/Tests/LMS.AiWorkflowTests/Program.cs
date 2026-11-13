using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Adapters;
using LMS.Core.Infrastructure.Persistence;
using LMS.CoreApplicationServices.AIModule.Dtos;
using LMS.CoreApplicationServices.AIModule.Implements;
using LMS.CoreApplicationServices.Common;
using LMS.CoreApplicationServices.CourseModule.Implements;
using LMS.CoreApplicationServices.QuizModule.Dtos;
using LMS.CoreApplicationServices.QuizModule.Implements;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace LMS.AiWorkflowTests;

internal static class WorkflowEntry
{
    private static int _checks;
    private const string PackageNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string ContentNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly CountingAi Client = new();

    private static async Task Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lms-ai-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var password = "Test!" + Guid.NewGuid().ToString("N") + "a1";
        var settings = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development", ["DotEnv__Enabled"] = "false",
            ["Database__Provider"] = "Sqlite", ["Database__AutoMigrate"] = "true",
            ["ConnectionStrings__Sqlite"] = "Data Source=" + Path.Combine(directory, "test.sqlite"),
            ["Storage__RootPath"] = Path.Combine(directory, "Uploads"),
            ["Demo__Seed"] = "true", ["Demo__Password"] = password, ["Bootstrap__AdminUsername"] = "",
            ["Ai__Mode"] = "Mock", ["Ai__MaxSourceCharacters"] = "1000", ["Ai__MaxGenerationChunks"] = "3",
            ["Ai__MaxProviderRequests"] = "4", ["Ai__MaxRetries"] = "1",
            ["Jwt__Key"] = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
            ["Cors__AllowedOrigins__0"] = "http://localhost:5173", ["RateLimiting__AuthPermitLimit"] = "100",
            ["RateLimiting__AiPermitLimit"] = "100"
        };
        var originals = settings.ToDictionary(x => x.Key, x => Environment.GetEnvironmentVariable(x.Key));
        foreach (var setting in settings) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        try
        {
            PlannerChecks();
            await HttpChecks(password);
            Console.WriteLine($"AI WORKFLOW TESTS PASSED: {_checks} assertions; fake provider, real parsers/SQLite/HTTP; live Gemini NOT RUN.");
        }
        finally
        {
            foreach (var setting in originals) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
            SqliteConnection.ClearAllPools();
            var full = Path.GetFullPath(directory);
            if (Path.GetDirectoryName(full) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) ||
                !Path.GetFileName(full).StartsWith("lms-ai-workflow-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe test cleanup target");
            Directory.Delete(full, true);
        }
    }

    private static void PlannerChecks()
    {
        var limits = new AiGenerationLimits(1000, 3, 4, 1, 240);
        var text = new string('A', 999) + "😀" + new string('B', 1000) + new string('C', 37);
        var plan = AiGenerationPlanner.Build(7, text, 5, limits);
        Check(plan.ChunkCount == 3 && plan.Chunks.Sum(c => c.QuestionCount) == 5 && plan.Chunks.All(c => c.QuestionCount > 0), "long source gets bounded chunks and exact question allocation");
        Check(plan.Chunks.Select(c => c.Index).SequenceEqual(new[] { 1, 2, 3 }), "source chunk indexes are one based");
        Check(string.Concat(plan.Chunks.Select(c => text[c.StartCharacter..c.EndCharacter])) == text &&
            plan.Chunks[0].StartCharacter == 0 && plan.Chunks[^1].EndCharacter == text.Length &&
            plan.Chunks.Zip(plan.Chunks.Skip(1)).All(p => p.First.EndCharacter == p.Second.StartCharacter), "ranges cover every original UTF16 character exactly once in order");
        Check(plan.Chunks.All(c => c.EndCharacter - c.StartCharacter <= 1000 &&
            !char.IsLowSurrogate(text[c.StartCharacter]) && !char.IsHighSurrogate(text[c.EndCharacter - 1])), "chunks never cut a UTF16 surrogate pair");
        var exactCapacity = string.Concat(Enumerable.Repeat(new string('a', 899) + " " + new string('b', 100), 3));
        var capacityPlan = AiGenerationPlanner.Build(7, exactCapacity, 3, limits);
        Check(capacityPlan.Chunks.Count == 3 && capacityPlan.Chunks[^1].EndCharacter == exactCapacity.Length, "word boundaries do not reject source fitting total capacity");
        Throws(() => AiGenerationPlanner.Build(7, new string('x', 3001), 3, limits), 400, "oversize source rejected rather than truncated");
        Throws(() => AiGenerationPlanner.Build(7, new string('x', 2001), 3, limits with { MaxRequests = 2 }), 400, "plan rejects excess chunk request budget");
        Throws(() => AiGenerationPlanner.Build(7, " ", 3, limits), 400, "empty extracted text rejected");
        Throws(() => AiGenerationPlanner.Build(7, new string('A', 500) + new string(' ', 1500) + new string('B', 100), 3, limits), 400, "whitespace-only planned range rejected before provider calls");
        Throws(() => AiGenerationPlanner.Build(7, "valid source", 2, limits), 400, "public question minimum remains three");
        var bounded = AiGenerationLimits.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Ai:MaxSourceCharacters"] = "1", ["Ai:MaxGenerationChunks"] = "999", ["Ai:MaxProviderRequests"] = "999", ["Ai:MaxRetries"] = "999", ["Ai:GenerationTimeoutSeconds"] = "999" }).Build());
        Check(bounded.MaxSourceCharacters == 1000 && bounded.MaxChunks == 5 && bounded.MaxRequests == 12 && bounded.MaxRetries == 2 && bounded.TotalTimeoutSeconds == 300, "generation configuration has explicit upper and lower bounds");
    }

    private static async Task HttpChecks(string password)
    {
        int persistedQuestionId;
        using (var host = new TestHost())
        {
            using var anonymous = host.CreateClient();
            using var teacher = await Login(host, "lecturer", password);
            using var student = await Login(host, "student", password);
            var course = await Send(teacher, "POST", "/api/courses", new { code = "AIWORKFLOW", title = "AI Workflow Course" }, 201);
            var courseId = course!["id"]!.GetValue<int>();
            await Send(student, "POST", "/api/courses/enroll", new { joinCode = course["joinCode"]!.GetValue<string>() }, 200);
            var topic = await Send(teacher, "POST", $"/api/courses/{courseId}/topics", new { name = "Learning source" }, 201);
            var topicId = topic!["id"]!.GetValue<int>();
            await ManualDifficultyChecks(teacher, student, courseId, topicId);
            var lastRows = new JsonArray();
            var firstMaterial = 0;
            foreach (var format in new[] { "txt", "md", "pdf", "docx", "pptx" })
            {
                var bytes = format switch
                {
                    "pdf" => Pdf(true), "docx" => Docx(), "pptx" => Pptx(),
                    _ => Encoding.UTF8.GetBytes("Learning objectives: validated source material for the question generation workflow.")
                };
                var material = await Upload(teacher, courseId, topicId, "lesson." + format, bytes);
                var materialId = material["id"]!.GetValue<int>();
                if (firstMaterial == 0) firstMaterial = materialId;
                Check(material["extractionStatus"]!.GetValue<string>() == "Extracted" && material["extractedCharacterCount"]!.GetValue<int>() > 0, format + " genuine parser produces text for AI");
                Client.Reset();
                var plan = await Send(teacher, "POST", "/api/ai/questions/plan", new { materialId, count = 3, difficulty = "Medium" }, 200);
                Check(Client.Calls.Count == 0 && plan!["chunkCount"]!.GetValue<int>() == 1, format + " plan has no provider call");
                lastRows = (await Send(teacher, "POST", "/api/ai/questions/generate", new { materialId, count = 3, difficulty = "Medium" }, 200))!.AsArray();
                Check(lastRows.Count == 3 && Client.Calls.Count == 1 && Client.Calls[0].Source.Length == plan!["totalSourceCharacters"]!.GetValue<int>(), format + " extraction enters generation without truncation");
                Check(lastRows.All(q => q!["status"]!.GetValue<string>() == "Draft" && q["source"]!.GetValue<string>() == "AI" &&
                    q["difficulty"]!.GetValue<string>() == "Medium" && q["materialId"]!.GetValue<int>() == materialId && q["topicId"]!.GetValue<int>() == topicId &&
                    q["sourceChunkIndex"]!.GetValue<int>() == 1 && q["sourceStartCharacter"]!.GetValue<int>() == 0 &&
                    q["sourceEndCharacter"]!.GetValue<int>() == Client.Calls[0].Source.Length && q["options"]!.AsArray().Count(o => o!["isCorrect"]!.GetValue<bool>()) == 1), format + " generated questions keep source range, difficulty and exactly one correct answer as Draft");
                using var scope = host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
                var log = await db.AIGenerationLogs.AsNoTracking().Where(l => l.MaterialId == materialId).SingleAsync();
                Check(log.Status == "Success" && log.RequestedQuestionCount == 3 && log.GeneratedQuestionCount == 3 && log.ChunkCount == 1 && log.ProviderRequestCount == 1 && log.DurationMilliseconds >= 0, format + " success log records only safe operational metadata");
            }

            await Send(anonymous, "POST", "/api/ai/questions/plan", new { materialId = firstMaterial, count = 3, difficulty = "Easy" }, 401);
            await Send(student, "POST", "/api/ai/questions/plan", new { materialId = firstMaterial, count = 3, difficulty = "Easy" }, 403);
            await Send(student, "POST", "/api/ai/questions/generate", new { materialId = firstMaterial, count = 3, difficulty = "Easy" }, 403);
            await Send(teacher, "POST", "/api/ai/questions/plan", new { materialId = firstMaterial, count = 11, difficulty = "Easy" }, 400);
            await Send(teacher, "POST", "/api/ai/questions/plan", new { materialId = firstMaterial, count = 3, difficulty = "Unknown" }, 400);

            var questionIds = lastRows.Select(q => q!["id"]!.GetValue<int>()).ToArray();
            persistedQuestionId = questionIds[0];
            var quiz = await Send(teacher, "POST", "/api/quizzes", new { courseId, title = "Generated source quiz" }, 201);
            var quizId = quiz!["id"]!.GetValue<int>();
            await Send(teacher, "POST", $"/api/quizzes/{quizId}/publish", new { questionIds }, 400);
            foreach (var id in questionIds) await Send(teacher, "POST", $"/api/questions/{id}/approve", null, 200);
            await Send(teacher, "POST", $"/api/quizzes/{quizId}/publish", new { questionIds }, 200);
            var taken = await Send(student, "GET", $"/api/quizzes/{quizId}", null, 200);
            Check(!taken!.ToJsonString().Contains("isCorrect", StringComparison.OrdinalIgnoreCase) && !taken.ToJsonString().Contains("explanation", StringComparison.OrdinalIgnoreCase), "taking generated quiz never exposes answer correctness or explanation");
            var answers = lastRows.Select(q => new
            {
                questionId = q!["id"]!.GetValue<int>(),
                selectedOptionId = q["options"]!.AsArray().Single(o => o!["isCorrect"]!.GetValue<bool>())!["id"]!.GetValue<int>()
            }).ToArray();
            var scored = await Send(student, "POST", $"/api/quizzes/{quizId}/submit", new { answers }, 200);
            Check(scored!["score"]!.GetValue<decimal>() == 10 && scored["correctCount"]!.GetValue<int>() == 3, "generated and approved questions score through backend quiz workflow");
            await Send(student, "POST", $"/api/quizzes/{quizId}/submit", new { answers }, 409);

            var scan = await Upload(teacher, courseId, topicId, "scan.pdf", Pdf(false));
            Client.Reset();
            await Send(teacher, "POST", "/api/ai/questions/plan", new { materialId = scan["id"]!.GetValue<int>(), count = 3, difficulty = "Easy" }, 400);
            Check(Client.Calls.Count == 0, "PDF without text cannot call AI");
            await Send(teacher, "DELETE", $"/api/materials/{firstMaterial}", null, 204);
            await Send(teacher, "POST", "/api/ai/questions/plan", new { materialId = firstMaterial, count = 3, difficulty = "Easy" }, 404);
            await Send(teacher, "POST", "/api/ai/questions/generate", new { materialId = firstMaterial, count = 3, difficulty = "Easy" }, 404);
            Check(Client.Calls.Count == 0, "deleted source cannot reach provider");

            await EdgeChecks(host, courseId, topicId);
            await QuizConcurrencyChecks(host, courseId, topicId);
        }
        using (var restarted = new TestHost())
        using (var scope = restarted.Services.CreateScope())
        {
            // Starting the host executes migrations; the context reads the same isolated SQLite file.
            using var http = restarted.CreateClient();
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            var question = await db.Questions.Include(q => q.Options).SingleAsync(q => q.Id == persistedQuestionId);
            Check(question.Status == "Approved" && question.Source == "AI" && question.Difficulty == "Medium" && question.SourceChunkIndex == 1 && question.Options.Count == 4, "question provenance and approval survive host restart");
        }
    }

    private static async Task ManualDifficultyChecks(HttpClient teacher, HttpClient student, int courseId, int topicId)
    {
        var options = Enumerable.Range(1, 4).Select(i => new { content = "Manual option " + i, isCorrect = i == 1 }).ToArray();
        var manual = await Send(teacher, "POST", "/api/questions", new
        {
            courseId, topicId, content = "Manual difficulty question", explanation = "Manual explanation", options,
            difficulty = "Hard", source = "AI", status = "Approved"
        }, 201);
        var id = manual!["id"]!.GetValue<int>();
        Check(manual["difficulty"]!.GetValue<string>() == "Hard" && manual["source"]!.GetValue<string>() == "Manual" &&
            manual["status"]!.GetValue<string>() == "Draft" && manual["sourceChunkIndex"] == null,
            "manual creation stores optional difficulty while protecting provenance and approval status");
        await Send(student, "POST", "/api/questions", new { courseId, topicId, content = "Student author", explanation = "No author permission", options, difficulty = "Hard" }, 403);
        await Send(teacher, "POST", "/api/questions", new { courseId, topicId, content = "Invalid difficulty", explanation = "Validation", options, difficulty = "Expert" }, 400);
        var cleared = await Send(teacher, "PUT", $"/api/questions/{id}", new { content = "Cleared difficulty", explanation = "Manual explanation", options, difficulty = "" }, 200);
        Check(cleared!["difficulty"] == null, "empty difficulty on Draft edit clears optional value");
        var easy = await Send(teacher, "PUT", $"/api/questions/{id}", new { content = "Easy difficulty", explanation = "Manual explanation", options, difficulty = "Easy" }, 200);
        Check(easy!["difficulty"]!.GetValue<string>() == "Easy", "Draft edit can set valid difficulty");
        var preserved = await Send(teacher, "PUT", $"/api/questions/{id}", new { content = "Preserved difficulty", explanation = "Manual explanation", options }, 200);
        Check(preserved!["difficulty"]!.GetValue<string>() == "Easy", "legacy edit omitting difficulty preserves existing value");
        await Send(student, "PUT", $"/api/questions/{id}", new { content = "Unauthorized edit", explanation = "No author permission", options, difficulty = "Medium" }, 403);
        await Send(teacher, "PUT", $"/api/questions/{id}", new { content = "Invalid edit", explanation = "Validation", options, difficulty = "easy" }, 400);
        var approved = await Send(teacher, "POST", $"/api/questions/{id}/approve", null, 200);
        Check(approved!["difficulty"]!.GetValue<string>() == "Easy" && approved["content"]!.GetValue<string>() == "Preserved difficulty", "invalid difficulty edit preserves persisted Draft values before approval");
        await Send(teacher, "PUT", $"/api/questions/{id}", new { content = "Approved edit rejected", explanation = "Immutable", options, difficulty = "Hard" }, 409);
    }

    private static async Task EdgeChecks(TestHost host, int courseId, int topicId)
    {
        int lecturerId;
        int materialId;
        var text = new string('A', 999) + "😀" + new string('B', 1000) + new string('C', 37);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            lecturerId = await db.Courses.Where(c => c.Id == courseId).Select(c => c.LecturerId).SingleAsync();
            var material = new LearningMaterial { CourseId = courseId, TopicId = topicId, Title = "Long source", OriginalFileName = "long.md", ExtractedText = text, ExtractionStatus = "Extracted" };
            db.LearningMaterials.Add(material); await db.SaveChangesAsync(); materialId = material.Id;
        }
        Client.Reset();
        using (var scope = host.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<AiQuestionService>();
            var plan = await service.PlanAsync(new(materialId, 5, "Hard"), lecturerId, default);
            Check(Client.Calls.Count == 0, "direct planning reads metadata with no provider request");
            var questions = await service.GenerateAsync(new(materialId, 5, "Hard"), lecturerId, default);
            Check(questions.Count == 5 && Client.Calls.Select(c => c.Count).SequenceEqual(plan.Chunks.Select(c => c.QuestionCount)) && string.Concat(Client.Calls.Select(c => c.Source)) == text, "long source sends exact planned ordered ranges and allocated counts");
            Check(questions.All(q => plan.Chunks.Any(c => c.Index == q.SourceChunkIndex && c.StartCharacter == q.SourceStartCharacter && c.EndCharacter == q.SourceEndCharacter)), "each long-source Draft links to its actual source chunk");
        }
        await FailedGeneration(host, materialId, lecturerId, call => call == 2 ? throw new AiClientException(503, "temporary upstream", 503) : null, 503, "later provider error leaves no partial questions");
        await FailedGeneration(host, materialId, lecturerId, call => call == 2 ? "not json" : null, 422, "later invalid JSON leaves no partial questions");
        await FailedGeneration(host, materialId, lecturerId, _ => "[]", 422, "empty array leaves no question drafts");
        Client.Reset(); Client.ExtraReservations = 1;
        await AssertFailureWithoutRows(host, materialId, lecturerId, 503, "shared attempt budget counts simulated retries across chunks");
        Check(Client.Calls.Count == 2, "request budget stops third chunk before simulated provider send");
        Client.Reset(); Client.RepeatContent = true;
        using (var scope = host.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<AiQuestionService>();
            var questions = await service.GenerateAsync(new(materialId, 5, "Easy"), lecturerId, default);
            Check(questions.Count == 1 && questions.Select(q => q.Content).Distinct(StringComparer.OrdinalIgnoreCase).Count() == questions.Count, "duplicate provider rows across chunks are saved once");
        }
        Client.Reset();
        using (var scope = host.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<AiQuestionService>();
            await ThrowsAsync(() => service.PlanAsync(new(materialId, 3, "Easy"), -1, default), 403, "non-owner cannot plan source generation");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { await service.GenerateAsync(new(materialId, 3, "Easy"), lecturerId, cancel.Token); throw new InvalidOperationException("Cancellation ignored"); }
            catch (OperationCanceledException) { Check(Client.Calls.Count == 0, "caller cancellation before generation makes no provider call"); }
        }
        Client.Reset();
        int beforeRaceQuestions, beforeRaceLogs;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            beforeRaceQuestions = await db.Questions.CountAsync();
            beforeRaceLogs = await db.AIGenerationLogs.CountAsync();
        }
        Client.Response = call =>
        {
            if (call == 1)
            {
                using var otherScope = host.Services.CreateScope();
                otherScope.ServiceProvider.GetRequiredService<LmsDbContext>().LearningMaterials
                    .Where(m => m.Id == materialId).ExecuteUpdate(update => update.SetProperty(m => m.IsDeleted, true));
            }
            return null;
        };
        using (var scope = host.Services.CreateScope())
            await ThrowsAsync(() => scope.ServiceProvider.GetRequiredService<AiQuestionService>()
                .GenerateAsync(new(materialId, 5, "Easy"), lecturerId, default), 409, "source deleted during provider work rejects commit with conflict");
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            Check(await db.Questions.CountAsync() == beforeRaceQuestions && await db.AIGenerationLogs.CountAsync() == beforeRaceLogs &&
                await db.Questions.AnyAsync(q => q.MaterialId == materialId), "deletion race rolls back new Drafts and success log while retaining previous questions");
        }
        Client.Reset();
    }

    private static async Task FailedGeneration(TestHost host, int materialId, int lecturerId, Func<int, string?> behavior, int expected, string label)
    {
        Client.Reset(); Client.Response = behavior;
        await AssertFailureWithoutRows(host, materialId, lecturerId, expected, label);
    }

    private static async Task QuizConcurrencyChecks(TestHost host, int courseId, int topicId)
    {
        int lecturerId, quizId, firstQuestionId, secondQuestionId;
        string connectionString;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            connectionString = db.Database.GetConnectionString()!;
            lecturerId = await db.Courses.Where(c => c.Id == courseId).Select(c => c.LecturerId).SingleAsync();
            var sourceQuestions = Enumerable.Range(1, 2).Select(i => new Question
            {
                CourseId = courseId, TopicId = topicId, Content = "Concurrency source question " + i,
                Explanation = "Concurrency fixture", Status = "Approved", Source = "Manual",
                Options = Enumerable.Range(1, 4).Select(o => new AnswerOption { Content = "Option " + o, IsCorrect = o == 1 }).ToList()
            }).ToList();
            db.Questions.AddRange(sourceQuestions); await db.SaveChangesAsync();
            firstQuestionId = sourceQuestions[0].Id; secondQuestionId = sourceQuestions[1].Id;
            var quiz = await scope.ServiceProvider.GetRequiredService<QuizService>().CreateAsync(lecturerId,
                new(courseId, "Concurrent quiz", QuestionIds: new() { firstQuestionId }), default);
            quizId = quiz.Id;
        }
        var barrier = new BeforeSaveInterceptor(async () =>
        {
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<QuizService>().UpdateAsync(quizId, lecturerId,
                new("Edited before stale publish", QuestionIds: new() { secondQuestionId }), default);
        });
        var options = new DbContextOptionsBuilder<LmsDbContext>().UseSqlite(connectionString).AddInterceptors(barrier).Options;
        await using (var stalePublisher = new LmsDbContext(options))
        {
            var service = new QuizService(stalePublisher, new CourseService(stalePublisher));
            await ConcurrencyConflict(() => service.PublishAsync(quizId, lecturerId, new(new() { firstQuestionId }), default),
                "publisher loaded before selection edit gets concurrency conflict");
        }
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            var actual = await db.Quizzes.AsNoTracking().Include(q => q.QuizQuestions).SingleAsync(q => q.Id == quizId);
            Check(actual.Status == "Draft" && actual.Title == "Edited before stale publish" &&
                actual.QuizQuestions.Select(q => q.QuestionId).SequenceEqual(new[] { secondQuestionId }),
                "stale publish cannot publish a different selection; committed edit stays Draft");
        }
        var editBarrier = new BeforeSaveInterceptor(async () =>
        {
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<QuizService>().UpdateAsync(quizId, lecturerId,
                new("Winning concurrent edit", QuestionIds: new() { secondQuestionId }), default);
        });
        var editOptions = new DbContextOptionsBuilder<LmsDbContext>().UseSqlite(connectionString).AddInterceptors(editBarrier).Options;
        await using (var staleEditor = new LmsDbContext(editOptions))
        {
            var service = new QuizService(staleEditor, new CourseService(staleEditor));
            await ConcurrencyConflict(() => service.UpdateAsync(quizId, lecturerId,
                new("Losing stale edit", QuestionIds: new() { firstQuestionId }), default),
                "two Draft edits cannot silently overwrite the winning edit");
        }
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            var actual = await db.Quizzes.AsNoTracking().Include(q => q.QuizQuestions).SingleAsync(q => q.Id == quizId);
            Check(actual.Status == "Draft" && actual.Title == "Winning concurrent edit" &&
                actual.QuizQuestions.Select(q => q.QuestionId).SequenceEqual(new[] { secondQuestionId }),
                "losing Draft edit rolls back child changes and keeps the winning selection");
        }
    }

    private static async Task ConcurrencyConflict(Func<Task> action, string label)
    {
        try { await action(); throw new InvalidOperationException("Expected concurrency conflict: " + label); }
        catch (DbUpdateConcurrencyException) { Check(true, label); }
    }

    private static async Task AssertFailureWithoutRows(TestHost host, int materialId, int lecturerId, int expected, string label)
    {
        int beforeQuestions, beforeOptions;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            beforeQuestions = await db.Questions.CountAsync(); beforeOptions = await db.AnswerOptions.CountAsync();
            await ThrowsAsync(() => scope.ServiceProvider.GetRequiredService<AiQuestionService>().GenerateAsync(new(materialId, 5, "Easy"), lecturerId, default), expected, label + " reports status");
        }
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            Check(await db.Questions.CountAsync() == beforeQuestions && await db.AnswerOptions.CountAsync() == beforeOptions, label);
            var log = await db.AIGenerationLogs.OrderByDescending(l => l.Id).FirstAsync();
            Check(log.Status == "Failed" && log.ProviderRequestCount <= 4 && !log.ErrorMessage.Contains("AAAA", StringComparison.Ordinal), label + " records safe failure log within attempt limit");
        }
    }

    private static async Task<JsonNode> Upload(HttpClient client, int courseId, int topicId, string name, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(topicId.ToString()), "topicId");
        content.Add(new ByteArrayContent(bytes), "file", name);
        using var response = await client.PostAsync($"/api/courses/{courseId}/materials", content);
        var raw = await response.Content.ReadAsStringAsync();
        Check((int)response.StatusCode == 201, "upload " + name + ": " + (int)response.StatusCode + " " + raw);
        return JsonNode.Parse(raw)!;
    }
    private static async Task<HttpClient> Login(TestHost host, string username, string password)
    {
        using var anonymous = host.CreateClient();
        var response = await Send(anonymous, "POST", "/api/auth/login", new { username, password }, 200);
        var client = host.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", response!["accessToken"]!.GetValue<string>()); return client;
    }
    private static async Task<JsonNode?> Send(HttpClient client, string method, string path, object? body, int expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        Check((int)response.StatusCode == expected, $"{method} {path}: {(int)response.StatusCode} expected {expected}; {((int)response.StatusCode == expected ? "" : raw)}");
        return string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw);
    }
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException("FAIL " + label); _checks++; Console.WriteLine("PASS " + label); }
    private static void Throws(Action action, int expected, string label)
    { try { action(); throw new InvalidOperationException("Expected failure: " + label); } catch (AppException ex) { Check(ex.StatusCode == expected, label); } }
    private static async Task ThrowsAsync(Func<Task> action, int expected, string label)
    { try { await action(); throw new InvalidOperationException("Expected failure: " + label); } catch (AppException ex) { Check(ex.StatusCode == expected, label); } }

    private static byte[] Docx() => Zip(new()
    {
        ["[Content_Types].xml"] = $"<Types xmlns='{ContentNs}'><Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>",
        ["_rels/.rels"] = $"<Relationships xmlns='{PackageNs}'><Relationship Id='main' Type='{OfficeNs}/officeDocument' Target='word/document.xml'/></Relationships>",
        ["word/document.xml"] = "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>Learning DOCX paragraph.</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Table learning text.</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>"
    });
    private static byte[] Pptx() => Zip(new()
    {
        ["[Content_Types].xml"] = $"<Types xmlns='{ContentNs}'><Override PartName='/ppt/presentation.xml' ContentType='application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml'/></Types>",
        ["_rels/.rels"] = $"<Relationships xmlns='{PackageNs}'><Relationship Id='main' Type='{OfficeNs}/officeDocument' Target='ppt/presentation.xml'/></Relationships>",
        ["ppt/presentation.xml"] = $"<p:presentation xmlns:p='http://schemas.openxmlformats.org/presentationml/2006/main' xmlns:r='{OfficeNs}'><p:sldIdLst><p:sldId id='256' r:id='slideA'/></p:sldIdLst></p:presentation>",
        ["ppt/_rels/presentation.xml.rels"] = $"<Relationships xmlns='{PackageNs}'><Relationship Id='slideA' Type='{OfficeNs}/slide' Target='slides/slide1.xml'/></Relationships>",
        ["ppt/slides/slide1.xml"] = "<p:sld xmlns:p='http://schemas.openxmlformats.org/presentationml/2006/main' xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main'><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>Learning PPTX slide text.</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>"
    });
    private static byte[] Zip(Dictionary<string, string> entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) foreach (var entry in entries)
        { using var stream = zip.CreateEntry(entry.Key).Open(); stream.Write(Encoding.UTF8.GetBytes(entry.Value)); }
        return memory.ToArray();
    }
    private static byte[] Pdf(bool text)
    {
        var content = text ? "BT /F1 12 Tf 20 100 Td (LMS PDF learning text) Tj ET" : "q 100 0 0 100 20 20 cm /Im1 Do Q";
        var resources = text ? "/Font << /F1 5 0 R >>" : "/XObject << /Im1 5 0 R >>";
        var objects = new List<string> { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << {resources} >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            text ? "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>" : "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 3 >>\nstream\nabc\nendstream" };
        var pdf = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = pdf.Length; pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"); return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private sealed record Call(string Source, int Count);
    private sealed class BeforeSaveInterceptor(Func<Task> beforeSave) : SaveChangesInterceptor
    {
        private Func<Task>? _beforeSave = beforeSave;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var callback = _beforeSave; _beforeSave = null;
            if (callback != null) await callback();
            return result;
        }
    }
    private sealed class CountingAi : IAIClient
    {
        public List<Call> Calls { get; } = new();
        public Func<int, string?>? Response { get; set; }
        public bool RepeatContent { get; set; }
        public int ExtraReservations { get; set; }
        private int _generation;
        public void Reset() { Calls.Clear(); Response = null; RepeatContent = false; ExtraReservations = 0; _generation++; }
        public Task<string> GenerateJsonAsync(string source, int count, string difficulty, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Calls.Add(new(source, count));
            var custom = Response?.Invoke(Calls.Count);
            if (custom != null) return Task.FromResult(custom);
            return Task.FromResult(JsonSerializer.Serialize(Enumerable.Range(1, count).Select(i => new
            {
                content = RepeatContent ? "Same source question" : $"Source question {_generation}/{Calls.Count}/{i}", explanation = "Explanation grounded in fixture source.",
                options = Enumerable.Range(1, 4).Select(o => new { content = $"Option {o}", isCorrect = o == 1 })
            })));
        }
        public Task<string> GenerateJsonAsync(string source, int count, string difficulty, AiRequestBudget budget, CancellationToken ct)
        {
            budget.Reserve(ct);
            for (var i = 0; i < ExtraReservations; i++) budget.Reserve(ct);
            return GenerateJsonAsync(source, count, difficulty, ct);
        }
    }
    private sealed class TestHost : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "LMS.sln"))) root = root.Parent;
            if (root == null) throw new InvalidOperationException("Cannot locate BE/LMS.sln");
            builder.UseContentRoot(Path.Combine(root.FullName, "Services", "Core", "LMS.Core.API"));
            builder.UseEnvironment("Development"); builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services => { services.RemoveAll<IAIClient>(); services.AddSingleton<IAIClient>(Client); });
        }
    }
}
