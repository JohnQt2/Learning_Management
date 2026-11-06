using System.Net;
using System.Text;
using System.Text.Json;
using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Adapters;
using LMS.Core.Infrastructure.Persistence;
using LMS.CoreApplicationServices.AdminModule.Dtos;
using LMS.CoreApplicationServices.AdminModule.Implements;
using LMS.CoreApplicationServices.AIModule.Dtos;
using LMS.CoreApplicationServices.AIModule.Implements;
using LMS.CoreApplicationServices.AuthenticationModule.Dtos;
using LMS.CoreApplicationServices.AuthenticationModule.Implements;
using LMS.CoreApplicationServices.Common;
using LMS.CoreApplicationServices.CourseModule.Implements;
using LMS.CoreApplicationServices.MaterialModule.Implements;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"FAIL: {name}");
    passed++; Console.WriteLine($"PASS: {name}");
}
async Task Error<T>(Func<Task> action, Func<T, bool> condition, string name) where T : Exception
{
    try { await action(); }
    catch (T ex) { Check(condition(ex), name); return; }
    throw new Exception($"FAIL: {name} did not reject");
}
Task AppError(Func<Task> action, int status, string name) => Error<AppException>(action, e => e.StatusCode == status, name);
Task AiError(Func<Task> action, int status, string name) => Error<AiClientException>(action,
    e => e.StatusCode == status && !e.Message.Contains("fixture-api-key") && !e.Message.Contains("private-document"), name);

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var failMaterial = new MaterialFailureInterceptor();
var options = new DbContextOptionsBuilder<LmsDbContext>().UseSqlite(connection).AddInterceptors(failMaterial).Options;
await using var db = new LmsDbContext(options);
await db.Database.EnsureCreatedAsync();
var hasher = new PasswordHasher<User>();
var auth = new AuthService(db, hasher);
var admin = new AdminService(db, hasher);
var student = await auth.RegisterStudentAsync(new("student", "Sinh viên thử nghiệm", "Fixture@12345"), default);
Check(student.Role == "Student" && student.PasswordHash != "Fixture@12345", "student registration hashes password and fixes role");
Check((await auth.LoginAsync(new(" STUDENT ", "Fixture@12345"), default)).Id == student.Id, "login normalizes username");
await AppError(() => auth.RegisterStudentAsync(new("student", "Duplicate User", "Fixture@12345"), default), 409, "duplicate username conflict");
await AppError(() => auth.LoginAsync(new("student", "incorrect"), default), 401, "incorrect password rejected");
await AppError(() => auth.RegisterStudentAsync(new("bad user", "Invalid Name", "Fixture@12345"), default), 400, "invalid username rejected");
Check((await auth.ProfileAsync(student.Id, default)).FullName == "Sinh viên thử nghiệm", "profile reads persisted full name");
await AppError(() => auth.ChangePasswordAsync(student.Id, new("wrong", "NewFixture@12345"), default), 400, "password change requires old password");
await AppError(() => auth.ChangePasswordAsync(student.Id, new("Fixture@12345", "short"), default), 400, "password change enforces length");
await auth.ChangePasswordAsync(student.Id, new("Fixture@12345", "NewFixture@12345"), default);
Check(student.SecurityVersion == 1, "password change invalidates earlier tokens");
await AppError(() => auth.LoginAsync(new("student", "Fixture@12345"), default), 401, "old password rejected after change");
Check((await auth.LoginAsync(new("student", "NewFixture@12345"), default)).Id == student.Id, "new password accepted");
await auth.LogoutAsync(student.Id, default);
Check(student.SecurityVersion == 2, "logout invalidates account tokens");
var adminUser = new User { Username = "administrator", FullName = "Administrator", Role = "Admin", PasswordHash = "unused" };
db.Users.Add(adminUser); await db.SaveChangesAsync();
await AppError(() => admin.LockAsync(adminUser.Id, adminUser.Id, new(true), default), 400, "admin self lock refused");
await admin.LockAsync(student.Id, adminUser.Id, new(true), default);
Check(student.IsLocked && student.SecurityVersion == 3, "locking revokes existing tokens");
await AppError(() => auth.LoginAsync(new("student", "NewFixture@12345"), default), 401, "locked user cannot log in");
await AppError(() => auth.ProfileAsync(student.Id, default), 401, "locked user profile rejected");
await admin.LockAsync(student.Id, adminUser.Id, new(false), default);
Check(!student.IsLocked && student.SecurityVersion == 4, "unlocking does not revive old tokens");
var lecturerDto = await admin.CreateLecturerAsync(new("lecturer", "Giảng viên", "Fixture@12345"), default);
Check(lecturerDto.Role == "Lecturer" && (await admin.GetAsync(lecturerDto.Id, default)).Username == "lecturer", "admin creates lecturer and reads details");
await AppError(() => admin.CreateLecturerAsync(new("lecturer", "Giảng viên", "Fixture@12345"), default), 409, "duplicate lecturer conflict");
await using (var staleDb = new LmsDbContext(options))
{
    await staleDb.Users.FindAsync(student.Id);
    await auth.LogoutAsync(student.Id, default);
    await AppError(() => new AuthService(staleDb, hasher).LogoutAsync(student.Id, default), 409, "concurrent account changes do not reuse security version");
}

var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "lms-security-" + Guid.NewGuid().ToString("N")));
var storageRoot = Path.Combine(testRoot, "uploads");
Directory.CreateDirectory(testRoot);
try
{
    var storage = new LocalFileStorage(storageRoot);
    var text = "ASP.NET xử lý yêu cầu HTTP bằng controller. Dữ liệu được lưu vào cơ sở dữ liệu.";
    var saved = await storage.SaveAndExtractAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)), "../../lesson.txt", default);
    Check(saved.Text == text && saved.StoragePath.Length == 36 && !saved.StoragePath.Contains("lesson"), "TXT storage uses random managed basename");
    using (var read = new StreamReader(storage.OpenRead(saved.StoragePath)))
        Check(await read.ReadToEndAsync() == text, "stored TXT round trip");
    var outside = Path.Combine(testRoot, "keep.txt"); await File.WriteAllTextAsync(outside, "preserve");
    storage.Delete("../keep.txt");
    Check(File.Exists(outside), "delete rejects traversal and preserves outside file");
    await Error<FileNotFoundException>(() => Task.Run(() => storage.OpenRead("../keep.txt")), _ => true, "download rejects traversal");
    var countBefore = Directory.GetFiles(storageRoot).Length;
    await Error<InvalidDataException>(() => storage.SaveAndExtractAsync(new MemoryStream(new byte[5_000_001]), "large.txt", default), _ => true, "actual stream byte limit enforced");
    await Error<InvalidDataException>(() => storage.SaveAndExtractAsync(new MemoryStream(new byte[] { 0xC3, 0x28 }), "bad.txt", default), _ => true, "invalid UTF8 rejected");
    await Error<InvalidDataException>(() => storage.SaveAndExtractAsync(new MemoryStream(new byte[] { 0, 1, 2 }), "binary.txt", default), _ => true, "binary TXT rejected");
    await Error<InvalidDataException>(() => storage.SaveAndExtractAsync(new MemoryStream(Encoding.UTF8.GetBytes("fake pdf")), "fake.pdf", default), _ => true, "PDF signature checked");
    await Error<InvalidDataException>(() => storage.SaveAndExtractAsync(new MemoryStream(), "empty.txt", default), _ => true, "empty upload rejected");
    Check(Directory.GetFiles(storageRoot).Length == countBefore, "failed uploads clean only their new files");
    var blankPdf = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
    blankPdf.AddPage(300, 200);
    var pdf = await storage.SaveAndExtractAsync(new MemoryStream(blankPdf.Build()), "paper.pdf", default);
    Check(pdf.Text == "" && File.Exists(Path.Combine(storageRoot, pdf.StoragePath)), "PDF stores without pretending text extraction");
    using (var stream = storage.OpenRead(pdf.StoragePath)) Check(stream.Length > 0, "PDF download opens actual stored file");

    var course = new Course { Code = "SEC101", Title = "Security", JoinCode = "SECJOIN1", LecturerId = lecturerDto.Id };
    db.Courses.Add(course); await db.SaveChangesAsync();
    var topic = new Topic { CourseId = course.Id, Name = "HTTP" };
    db.Topics.Add(topic); await db.SaveChangesAsync();
    var courses = new CourseService(db);
    var materials = new MaterialService(db, storage, courses);
    failMaterial.Enabled = true;
    var beforeMetadata = Directory.GetFiles(storageRoot).Length;
    await Error<DbUpdateException>(() => materials.UploadAsync(course.Id, topic.Id, lecturerDto.Id,
        new MemoryStream(Encoding.UTF8.GetBytes(text)), "save-failure.txt", Encoding.UTF8.GetByteCount(text), default), _ => true, "metadata save failure propagates");
    failMaterial.Enabled = false;
    Check(Directory.GetFiles(storageRoot).Length == beforeMetadata && !await db.LearningMaterials.AnyAsync(), "metadata failure removes new file and never leaves a DB pointer");
    failMaterial.FailAfterCommit = true;
    await Error<DbUpdateException>(() => materials.UploadAsync(course.Id, topic.Id, lecturerDto.Id,
        new MemoryStream(Encoding.UTF8.GetBytes(text)), "commit-uncertain.txt", Encoding.UTF8.GetByteCount(text), default), _ => true, "post-commit transport failure propagates");
    failMaterial.FailAfterCommit = false;
    var committedMaterial = await db.LearningMaterials.AsNoTracking().SingleAsync();
    Check(File.Exists(Path.Combine(storageRoot, committedMaterial.StoragePath)), "ambiguous commit preserves file referenced by committed metadata");
    var material = await materials.UploadAsync(course.Id, topic.Id, lecturerDto.Id,
        new MemoryStream(Encoding.UTF8.GetBytes(text)), "real.txt", Encoding.UTF8.GetByteCount(text), default);
    await AppError(() => materials.DownloadAsync(material.Id, student.Id, "Student", default), 403, "unregistered student cannot download material");
    var ai = new AiQuestionService(db, new StaticAiClient(), courses);
    var generated = await ai.GenerateAsync(new(material.Id, 3, "Medium"), lecturerDto.Id, default);
    Check(generated.Count == 1 && generated[0].Status == "Draft" && generated[0].Source == "AI", "AI valid subset persists as drafts with source");
    Check(await db.AIGenerationLogs.AnyAsync(l => l.MaterialId == material.Id && l.Status == "Success"), "AI generation audit log stored");
    await AppError(() => ai.GenerateAsync(new(material.Id, 3, "arbitrary prompt"), lecturerDto.Id, default), 400, "AI difficulty cannot inject instructions");
    await AppError(() => new AiQuestionService(db, new DisabledAiClient(), courses).GenerateAsync(new(material.Id, 3, "Easy"), lecturerDto.Id, default), 503, "unconfigured AI fails without mock fallback");
    Check((await db.AIGenerationLogs.Where(l => l.Status == "Failed").ToListAsync()).All(l => !l.ErrorMessage.Contains(text)), "AI failure log excludes source document");
}
finally
{
    var tempPrefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!testRoot.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(testRoot).StartsWith("lms-security-"))
        throw new Exception("Refusing unexpected cleanup path");
    Directory.Delete(testRoot, recursive: true);
}

var valid = StaticAiClient.ValidRow;
Check(QuestionJsonValidator.Parse("[null," + valid + "]", 3).Count == 1, "validator filters null row independently");
Check(QuestionJsonValidator.Parse("[" + valid + "," + valid + "]", 3).Count == 1, "validator removes repeated question content");
await AppError(() => Task.Run(() => QuestionJsonValidator.Parse("[" + valid.Replace("\"isCorrect\":false", "\"isCorrect\":\"false\"") + "]", 3)), 422, "validator rejects nonboolean correctness");
await AppError(() => Task.Run(() => QuestionJsonValidator.Parse("[" + valid.Replace("\"explanation\":\"Lời giải\",", "") + "]", 3)), 422, "validator requires explanation");
await AppError(() => Task.Run(() => QuestionJsonValidator.Parse("not json", 3)), 422, "validator rejects malformed JSON");
await AppError(() => Task.Run(() => QuestionJsonValidator.Parse("[{}]", 3)), 422, "validator rejects invalid-only batch");

IConfiguration Config(params (string Key, string? Value)[] overrides)
{
    var values = new Dictionary<string, string?> { ["Ai:GeminiApiKey"] = "fixture-api-key", ["Ai:GeminiModel"] = GeminiAiClient.DefaultModel, ["Ai:MaxRetries"] = "0" };
    foreach (var item in overrides) values[item.Key] = item.Value;
    return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
GeminiAiClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action, IConfiguration? config = null)
    => new(new HttpClient(new FixtureHandler(action)) { Timeout = Timeout.InfiniteTimeSpan }, config ?? Config());
var successful = JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = "[" + valid + "]" } } } } } });
var requests = 0;
var realAdapterFixture = Client(async (request, ct) =>
{
    requests++;
    Check(request.RequestUri?.AbsoluteUri == $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiAiClient.DefaultModel}:generateContent", "Gemini official endpoint and configured model");
    Check(request.Headers.Contains("x-goog-api-key") && !request.RequestUri!.Query.Contains("key"), "Gemini key stays in backend header");
    using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
    Check(payload.RootElement.GetProperty("generationConfig").GetProperty("responseFormat").GetProperty("text").GetProperty("mimeType").GetString() == "APPLICATION_JSON", "Gemini structured JSON response format");
    Check(!payload.RootElement.GetProperty("generationConfig").TryGetProperty("candidateCount", out _), "adapter omits unnecessary candidate count for newer models");
    Check(!payload.RootElement.GetProperty("systemInstruction").ToString().Contains("private-document") && payload.RootElement.GetProperty("contents").ToString().Contains("private-document"), "source document stays outside system instructions");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(successful) };
});
Check(QuestionJsonValidator.Parse(await realAdapterFixture.GenerateJsonAsync("private-document", 3, "Medium", default), 3).Count == 1 && requests == 1, "adapter parses valid Gemini response with exactly one request");
await AiError(() => Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("fixture-api-key private-document") })).GenerateJsonAsync("private-document", 3, "Medium", default), 429, "quota failure sanitized without retry");
await AiError(() => Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))).GenerateJsonAsync("private-document", 3, "Medium", default), 502, "provider permission failure sanitized");
foreach (var (provider, expected) in new[] { (402, 502), (400, 502), (404, 502), (500, 503), (503, 503), (504, 504), (418, 502) })
{
    var calls = 0;
    await Error<AiClientException>(() => Client((_, _) =>
    {
        calls++;
        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)provider) { Content = new StringContent("fixture-api-key private-document") });
    }).GenerateJsonAsync("private-document", 3, "Medium", default), error =>
        error.StatusCode == expected && error.ProviderStatusCode == provider && error.Message.Contains($"Google HTTP {provider}") &&
        !error.Message.Contains("fixture-api-key") && !error.Message.Contains("private-document") && calls == 1,
        $"Google HTTP {provider} gives specific safe error and does not retry");
}
await AiError(() => Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") })).GenerateJsonAsync("private-document", 3, "Medium", default), 422, "blocked or missing candidates handled");
await AiError(() => Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("invalid") })).GenerateJsonAsync("private-document", 3, "Medium", default), 502, "malformed provider response handled");
await AiError(() => Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 512001)) })).GenerateJsonAsync("private-document", 3, "Medium", default), 502, "provider response byte limit");
await AiError(() => Client((_, _) => throw new HttpRequestException("fixture-api-key private-document")).GenerateJsonAsync("private-document", 3, "Medium", default), 502, "network failure excludes sensitive exception text");
await AiError(() => Client((_, _) => throw new TaskCanceledException()).GenerateJsonAsync("private-document", 3, "Medium", default), 504, "upstream timeout maps to gateway timeout");
await Error<AiClientException>(() => Client(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); }, Config(("Ai:TimeoutSeconds", "5"))).GenerateJsonAsync("private-document", 3, "Medium", default),
    error => error.StatusCode == 504 && error.Message.Contains("5 giây") && !error.Message.Contains("private-document"),
    "configured timeout cancels in-flight transport and reports configured wait safely");
await AiError(() => Client((_, _) => throw new Exception("must not call"), Config(("Ai:GeminiApiKey", null))).GenerateJsonAsync("private-document", 3, "Medium", default), 503, "missing key never performs network request");
await AiError(() => Client((_, _) => throw new Exception("must not call"), Config(("Ai:GeminiModel", "../other-host"))).GenerateJsonAsync("private-document", 3, "Medium", default), 503, "model rejects URL or traversal");
await AiError(() => Client((_, _) => throw new Exception("must not call")).GenerateJsonAsync(new string('a', 16001), 3, "Medium", default), 400, "oversized AI source never sent");
using (var cancel = new CancellationTokenSource())
{
    cancel.Cancel();
    await Error<OperationCanceledException>(() => Client((_, _) => throw new Exception("must not call")).GenerateJsonAsync("private-document", 3, "Medium", cancel.Token), _ => true, "caller cancellation propagated");
}
Console.WriteLine($"Security and adapter tests: {passed} checks passed. Gemini fixture transport only; live provider UNVERIFIED.");

sealed class FixtureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request, cancellationToken);
}
sealed class StaticAiClient : IAIClient
{
    public const string ValidRow = """
        {"content":"Câu hỏi hợp lệ?","explanation":"Lời giải","options":[{"content":"A","isCorrect":true},{"content":"B","isCorrect":false},{"content":"C","isCorrect":false},{"content":"D","isCorrect":false}]}
        """;
    public Task<string> GenerateJsonAsync(string sourceText, int count, string difficulty, CancellationToken ct)
        => Task.FromResult("[null," + ValidRow + ",{}]");
}
sealed class MaterialFailureInterceptor : SaveChangesInterceptor
{
    public bool Enabled { get; set; }
    public bool FailAfterCommit { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && eventData.Context!.ChangeTracker.Entries<LearningMaterial>().Any(e => e.State == EntityState.Added))
            throw new DbUpdateException("Fixture metadata write failure");
        return ValueTask.FromResult(result);
    }
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (FailAfterCommit) throw new DbUpdateException("Fixture failure after database commit");
        return ValueTask.FromResult(result);
    }
}
