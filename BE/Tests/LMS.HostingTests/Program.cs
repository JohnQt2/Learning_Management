using System.Collections;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using LMS.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LMS.Core.API.Configuration;
using Microsoft.AspNetCore.Builder;

namespace LMS.HostingTests;

internal static class HostingEntry
{
    private static int _checks;
    private static string _directory = "";

    private static async Task Main()
    {
        _directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "lms-hosting-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(_directory);
        try
        {
            var envRoot = Path.Combine(_directory, "dotenv");
            Directory.CreateDirectory(envRoot);
            File.WriteAllText(Path.Combine(envRoot, ".env"), "Ai__Mode=Disabled\nAi__GeminiModel=file-model\nAi__TimeoutSeconds=17\n");
            using (var variables = new TestEnvironment(new() { ["Ai__GeminiModel"] = "process-model", ["DotEnv__Enabled"] = "true" }))
            {
                var envBuilder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = envRoot, EnvironmentName = "Development", Args = ["--Ai:TimeoutSeconds=23"] });
                envBuilder.AddLocalDotEnv();
                Check(envBuilder.Configuration["Ai:Mode"] == "Disabled", "dotenv loads local file");
                Check(envBuilder.Configuration["Ai:GeminiModel"] == "process-model", "process environment overrides dotenv");
                Check(envBuilder.Configuration["Ai:TimeoutSeconds"] == "23", "command line overrides dotenv");
            }
            var dotenv = DotEnvConfiguration.Parse(["# comment", "Ai__Mode=Disabled", "Jwt__Key='test=value#literal' # comment", "export Storage__RootPath=Uploads # comment", "VITE_API_BASE=http://localhost:5088"]);
            Check(dotenv["Ai:Mode"] == "Disabled" && dotenv["Jwt:Key"] == "test=value#literal" && dotenv["Storage:RootPath"] == "Uploads", "dotenv parses nested keys, quotes, equals and comments");
            Check(!dotenv.ContainsKey("VITE_API_BASE"), "dotenv excludes frontend keys");
            try { DotEnvConfiguration.Parse(["Jwt__Key='private-value"]); throw new Exception("Malformed dotenv accepted"); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("line 1") && !ex.Message.Contains("private-value"), "dotenv invalid syntax hides secrets"); }
            RejectStartup("production requires JWT secret", "Jwt__Key", ("Jwt__Key", ""));
            RejectStartup("production rejects short JWT secret", "Jwt__Key", ("Jwt__Key", "short"));
            RejectStartup("production requires explicit CORS", "Cors:AllowedOrigins", ("Cors__AllowedOrigins__0", null));
            RejectStartup("CORS rejects origin containing path", "Cors:AllowedOrigins", ("Cors__AllowedOrigins__0", "https://frontend.example/path"));
            RejectStartup("production rejects mock AI", "Ai:Mode", ("Ai__Mode", "Mock"));
            RejectStartup("production rejects demo seed", "Demo seed is restricted", ("Demo__Seed", "true"), ("Demo__Password", Password()));
            RejectStartup("production rejects InMemory database", "InMemory database is only allowed", ("Database__Provider", "InMemory"));
            RejectStartup("invalid database provider rejected", "Database:Provider must be", ("Database__Provider", "Unknown"));
            RejectStartup("SQL Server requires connection string", "ConnectionStrings__LmsDb", ("Database__Provider", "SqlServer"), ("ConnectionStrings__LmsDb", ""));
            var longPassword = (Password() + new string('a', 129))[..129];
            RejectStartup("seed rejects password beyond login maximum", "10-128", "Development", ("Demo__Seed", "true"), ("Demo__Password", longPassword));
            RejectStartup("bootstrap rejects password beyond login maximum", "10-128", "Development", ("Bootstrap__AdminUsername", "fixture_admin"), ("Bootstrap__AdminPassword", longPassword));
            await ProductionReadiness();
            await AuthRateLimit();
            await AiRateLimit();
            await BootstrapLifecycle();
            await BootstrapCollision();
            Console.WriteLine($"PASS hosting configuration: {_checks} assertions; isolated SQLite, generated credentials, no provider calls.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            var tempPrefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!_directory.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(_directory).StartsWith("lms-hosting-"))
                throw new InvalidOperationException("Unexpected hosting test cleanup path");
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string Password() => "Generated!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + "a1";

    private static Dictionary<string, string?> Settings(string name, string environment)
    {
        var scenarioRoot = Path.Combine(_directory, name);
        Directory.CreateDirectory(scenarioRoot);
        return new()
        {
            ["ASPNETCORE_ENVIRONMENT"] = environment, ["DOTNET_ENVIRONMENT"] = environment,
            ["DotEnv__Enabled"] = "false",
            ["Database__Provider"] = "Sqlite", ["Database__AutoMigrate"] = environment == "Development" ? "true" : "false",
            ["ConnectionStrings__Sqlite"] = "Data Source=" + Path.Combine(scenarioRoot, "test.sqlite"),
            ["ConnectionStrings__LmsDb"] = "", ["Storage__RootPath"] = Path.Combine(scenarioRoot, "Uploads"),
            ["Jwt__Key"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(48)),
            ["Cors__AllowedOrigins__0"] = "http://localhost:5173", ["Ai__Mode"] = "Disabled",
            ["Demo__Seed"] = "false", ["Demo__Password"] = "", ["Bootstrap__AdminUsername"] = "",
            ["Bootstrap__AdminPassword"] = "", ["RateLimiting__AuthPermitLimit"] = "100",
            // TestServer has no TLS listener. Production deployments retain the default true.
            ["Security__RequireHttps"] = "false"
        };
    }

    private static void RejectStartup(string name, string reason, params (string Key, string? Value)[] overrides)
        => RejectStartup(name, reason, "Production", overrides);

    private static void RejectStartup(string name, string reason, string environment, params (string Key, string? Value)[] overrides)
    {
        var settings = Settings("reject-" + Guid.NewGuid().ToString("N"), environment);
        foreach (var value in overrides) settings[value.Key] = value.Value;
        using var variables = new TestEnvironment(settings);
        using var factory = new TestHost(environment);
        try { using var client = factory.CreateClient(); }
        catch (Exception ex)
        {
            Check(ex.ToString().Contains(reason, StringComparison.OrdinalIgnoreCase), name);
            return;
        }
        throw new InvalidOperationException("FAIL: " + name + " started unexpectedly");
    }

    private static async Task ProductionReadiness()
    {
        var settings = Settings("production", "Production");
        var path = new SqliteConnectionStringBuilder(settings["ConnectionStrings__Sqlite"]).DataSource;
        await using (var connection = new SqliteConnection(settings["ConnectionStrings__Sqlite"]))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE PendingSentinel (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL); INSERT INTO PendingSentinel VALUES (1,'preserve');";
            await command.ExecuteNonQueryAsync();
        }
        using (var variables = new TestEnvironment(settings))
        using (var factory = new TestHost("Production"))
        using (var client = factory.CreateClient())
        {
            using var health = await client.GetAsync("/api/health");
            Check(health.StatusCode == HttpStatusCode.OK, "configured production host starts");
            Check(health.Headers.GetValues("X-Content-Type-Options").Single() == "nosniff" && health.Headers.CacheControl?.NoStore == true, "production security and cache headers");
            var body = JsonNode.Parse(await health.Content.ReadAsStringAsync())!;
            Check(body["aiMode"] is null && !body["aiEnabled"]!.GetValue<bool>(), "production health omits development AI mode");
            using var ready = await client.GetAsync("/api/health/ready");
            Check(ready.StatusCode == HttpStatusCode.ServiceUnavailable, "production pending migrations report not ready");
            using var swagger = await client.GetAsync("/swagger/index.html");
            using var swaggerJson = await client.GetAsync("/swagger/v1/swagger.json");
            Check(swagger.StatusCode == HttpStatusCode.NotFound && swaggerJson.StatusCode == HttpStatusCode.NotFound, "production Swagger UI and spec are unavailable");
            using var missingJwt = await client.GetAsync("/api/auth/me");
            Check(missingJwt.StatusCode == HttpStatusCode.Unauthorized && JsonNode.Parse(await missingJwt.Content.ReadAsStringAsync())?["error"] is not null, "production authentication failure uses JSON");
        }
        await using var inspect = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await inspect.OpenAsync();
        await using var check = inspect.CreateCommand();
        check.CommandText = "SELECT Value FROM PendingSentinel WHERE Id=1";
        Check((string?)await check.ExecuteScalarAsync() == "preserve", "production never changes existing rows during startup");
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='__EFMigrationsHistory'";
        Check(Convert.ToInt32(await check.ExecuteScalarAsync()) == 0, "production never auto-migrates without explicit command");
    }

    private static async Task AuthRateLimit()
    {
        var settings = Settings("auth-limiter", "Development");
        settings["RateLimiting__AuthPermitLimit"] = "1";
        using var variables = new TestEnvironment(settings);
        using var factory = new TestHost("Development");
        using var client = factory.CreateClient();
        using var corsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        corsRequest.Headers.Add("Origin", "http://localhost:5173");
        using var corsResponse = await client.SendAsync(corsRequest);
        Check(corsResponse.Headers.GetValues("Access-Control-Allow-Origin").Single() == "http://localhost:5173" &&
            corsResponse.Headers.GetValues("Access-Control-Expose-Headers").Single().Contains("Content-Disposition", StringComparison.OrdinalIgnoreCase) &&
            corsResponse.Headers.GetValues("Access-Control-Expose-Headers").Single().Contains("Retry-After", StringComparison.OrdinalIgnoreCase),
            "cross-origin frontend can read download filename and retry delay headers");
        using var first = await client.PostAsJsonAsync("/api/auth/login", new { username = "missing_user", password = Password() });
        Check(first.StatusCode == HttpStatusCode.Unauthorized, "auth limiter permits initial login request");
        using var blocked = await client.PostAsJsonAsync("/api/auth/register", new { username = "not_created", fullName = "Fixture Student", password = Password() });
        Check(blocked.StatusCode == HttpStatusCode.TooManyRequests, "auth rate limit is shared by login and registration");
        Check(blocked.Headers.RetryAfter?.Delta == TimeSpan.FromSeconds(60), "auth rejection exposes Retry-After");
        Check(JsonNode.Parse(await blocked.Content.ReadAsStringAsync())?["error"] is not null, "auth rate limit returns JSON error");
        using var scope = factory.Services.CreateScope();
        Check(!await scope.ServiceProvider.GetRequiredService<LmsDbContext>().Users.AnyAsync(), "rate-limited registration writes no account and default seed is disabled");
    }

    private static async Task AiRateLimit()
    {
        var settings = Settings("ai-limiter", "Development");
        var password = Password();
        settings["Demo__Seed"] = "true"; settings["Demo__Password"] = password;
        using var variables = new TestEnvironment(settings);
        using var factory = new TestHost("Development");
        using var teacher = await Login(factory, "lecturer", password);
        for (var number = 1; number <= 6; number++)
        {
            using var response = await teacher.PostAsJsonAsync("/api/ai/questions/generate", new { materialId = 1, count = 3, difficulty = "Easy" });
            Check(response.StatusCode == HttpStatusCode.ServiceUnavailable, $"AI configured limit permits request {number} without provider fallback");
        }
        using var blocked = await teacher.PostAsJsonAsync("/api/ai/questions/generate", new { materialId = 1, count = 3, difficulty = "Easy" });
        Check(blocked.StatusCode == HttpStatusCode.TooManyRequests && blocked.Headers.RetryAfter?.Delta == TimeSpan.FromSeconds(60), "seventh AI request is rate limited with retry header");
        using var admin = await Login(factory, "admin", password);
        var teacherPassword = Password();
        using var created = await admin.PostAsJsonAsync("/api/admin/users/lecturers", new { username = "second_teacher", fullName = "Second Lecturer", password = teacherPassword });
        Check(created.StatusCode == HttpStatusCode.Created, "admin provisions independent lecturer for limiter scope");
        using var otherTeacher = await Login(factory, "second_teacher", teacherPassword);
        using var separate = await otherTeacher.PostAsJsonAsync("/api/ai/questions/generate", new { materialId = 1, count = 3, difficulty = "Easy" });
        Check(separate.StatusCode == HttpStatusCode.ServiceUnavailable, "AI rate limit is partitioned by authenticated user");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
        Check(!await db.Questions.AnyAsync() && !await db.AIGenerationLogs.AnyAsync(), "disabled and rate-limited AI writes no generated data");
    }

    private static async Task BootstrapLifecycle()
    {
        var settings = Settings("bootstrap", "Development");
        var firstPassword = Password();
        settings["Bootstrap__AdminUsername"] = "first_admin";
        settings["Bootstrap__AdminPassword"] = firstPassword;
        string adminHash, studentHash;
        using (var variables = new TestEnvironment(settings))
        using (var factory = new TestHost("Development"))
        using (var administrator = await Login(factory, "first_admin", firstPassword))
        {
            using var profile = await administrator.GetAsync("/api/auth/me");
            Check(JsonNode.Parse(await profile.Content.ReadAsStringAsync())?["role"]?.GetValue<string>() == "Admin", "bootstrap provisions functional administrator");
            using var publicClient = factory.CreateClient();
            using var registration = await publicClient.PostAsJsonAsync("/api/auth/register", new { username = "preserved_student", fullName = "Preserved Student", password = Password() });
            Check(registration.StatusCode == HttpStatusCode.Created, "bootstrap host accepts real student registration");
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            adminHash = (await db.Users.SingleAsync(u => u.Username == "first_admin")).PasswordHash;
            studentHash = (await db.Users.SingleAsync(u => u.Username == "preserved_student")).PasswordHash;
        }
        var replacementPassword = Password();
        settings["Bootstrap__AdminUsername"] = "should_not_create";
        settings["Bootstrap__AdminPassword"] = replacementPassword;
        using (var variables = new TestEnvironment(settings))
        using (var factory = new TestHost("Development"))
        using (var administrator = await Login(factory, "first_admin", firstPassword))
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            Check(await db.Users.CountAsync() == 2 && await db.Users.CountAsync(u => u.Role == "Admin") == 1, "bootstrap restart adds no extra administrator");
            var oldAdmin = await db.Users.SingleAsync(u => u.Username == "first_admin");
            var oldStudent = await db.Users.SingleAsync(u => u.Username == "preserved_student");
            Check(oldAdmin.PasswordHash == adminHash && oldStudent.PasswordHash == studentHash && oldStudent.Role == "Student", "bootstrap restart preserves hashes and existing roles");
            using var publicClient = factory.CreateClient();
            using var replacement = await publicClient.PostAsJsonAsync("/api/auth/login", new { username = "first_admin", password = replacementPassword });
            Check(replacement.StatusCode == HttpStatusCode.Unauthorized, "bootstrap configuration cannot reset an existing admin password");
        }
    }

    private static async Task BootstrapCollision()
    {
        var settings = Settings("bootstrap-collision", "Development");
        string originalHash;
        using (var variables = new TestEnvironment(settings))
        using (var factory = new TestHost("Development"))
        using (var client = factory.CreateClient())
        {
            using var response = await client.PostAsJsonAsync("/api/auth/register", new { username = "existing_student", fullName = "Existing Student", password = Password() });
            Check(response.StatusCode == HttpStatusCode.Created, "bootstrap collision fixture contains existing student");
            using var scope = factory.Services.CreateScope();
            originalHash = (await scope.ServiceProvider.GetRequiredService<LmsDbContext>().Users.SingleAsync()).PasswordHash;
        }
        settings["Bootstrap__AdminUsername"] = "existing_student";
        settings["Bootstrap__AdminPassword"] = Password();
        using (var variables = new TestEnvironment(settings))
        using (var factory = new TestHost("Development"))
        {
            var rejected = false;
            try { using var client = factory.CreateClient(); }
            catch (Exception ex) { rejected = ex.ToString().Contains("already in use", StringComparison.OrdinalIgnoreCase); }
            Check(rejected, "bootstrap refuses to promote an existing student");
        }
        await using var inspect = new SqliteConnection(new SqliteConnectionStringBuilder(settings["ConnectionStrings__Sqlite"]) { Mode = SqliteOpenMode.ReadOnly }.ToString());
        await inspect.OpenAsync();
        await using var command = inspect.CreateCommand();
        command.CommandText = "SELECT Role,PasswordHash FROM Users WHERE Username='existing_student'";
        await using var reader = await command.ExecuteReaderAsync();
        Check(await reader.ReadAsync() && reader.GetString(0) == "Student" && reader.GetString(1) == originalHash, "failed bootstrap preserves account role and password hash");
    }

    private static async Task<HttpClient> Login(TestHost factory, string username, string password)
    {
        using var anonymous = factory.CreateClient();
        using var response = await anonymous.PostAsJsonAsync("/api/auth/login", new { username, password });
        Check(response.StatusCode == HttpStatusCode.OK, "configured account authenticates through Program host");
        var token = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["accessToken"]!.GetValue<string>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        _checks++; Console.WriteLine("PASS: " + name);
    }

    private sealed class TestHost(string environment) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "LMS.sln"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("Cannot locate BE/LMS.sln for hosting tests");
            builder.UseContentRoot(Path.Combine(root.FullName, "Services", "Core", "LMS.Core.API"));
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }

    private sealed class TestEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _original;
        private readonly HashSet<string> _changed;
        private static readonly string[] Prefixes =
        ["Cors__", "Jwt__", "Database__", "ConnectionStrings__", "Storage__", "Demo__", "Bootstrap__", "Ai__", "RateLimiting__", "Security__", "ReverseProxy__", "DotEnv__"];

        public TestEnvironment(Dictionary<string, string?> settings)
        {
            var existing = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
                .Where(e => Prefixes.Any(p => ((string)e.Key).StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(e => (string)e.Key, e => (string?)e.Value, StringComparer.OrdinalIgnoreCase);
            _changed = existing.Keys.Concat(settings.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _original = _changed.ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.OrdinalIgnoreCase);
            foreach (var key in _changed) Environment.SetEnvironmentVariable(key, null);
            foreach (var setting in settings) Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }
        public void Dispose()
        {
            foreach (var value in _original) Environment.SetEnvironmentVariable(value.Key, value.Value);
        }
    }
}
