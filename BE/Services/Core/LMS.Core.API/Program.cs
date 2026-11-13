using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using System.Net;
using LMS.Core.Domain.Entities;
using LMS.Core.Infrastructure.Adapters;
using LMS.Core.Infrastructure.Persistence;
using LMS.CoreApplicationServices.AdminModule.Implements;
using LMS.CoreApplicationServices.AIModule.Implements;
using LMS.CoreApplicationServices.AuthenticationModule.Abstracts;
using LMS.CoreApplicationServices.AuthenticationModule.Implements;
using LMS.CoreApplicationServices.CourseModule.Abstracts;
using LMS.CoreApplicationServices.CourseModule.Implements;
using LMS.CoreApplicationServices.MaterialModule.Implements;
using LMS.CoreApplicationServices.QuestionModule.Implements;
using LMS.CoreApplicationServices.QuizModule.Implements;
using LMS.CoreApplicationServices.ResultModule.Implements;
using LMS.Core.API.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using LMS.Core.API.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.AddLocalDotEnv();
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
    options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(new
    {
        error = "Dữ liệu đầu vào không hợp lệ",
        errors = context.ModelState.Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(x => x.Key, x => x.Value!.Errors.Select(_ => "Giá trị không hợp lệ").ToArray())
    }));
var storageOptions = builder.Configuration.GetSection("Storage").Get<MaterialStorageOptions>() ?? new MaterialStorageOptions();
storageOptions.Validate();
var maxUploadRequestBytes = storageOptions.MaxUploadBytes + 65_536;
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxUploadRequestBytes);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUploadRequestBytes);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? (builder.Environment.IsDevelopment()
        ? new[] { "http://localhost:5173", "http://127.0.0.1:5173" }
        : throw new InvalidOperationException("Configure Cors:AllowedOrigins explicitly outside Development."));
if (corsOrigins.Length == 0 || corsOrigins.Any(x => !Uri.TryCreate(x, UriKind.Absolute, out var u) ||
    (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) ||
    u.AbsolutePath != "/" || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment) ||
    !string.IsNullOrEmpty(u.UserInfo) || x.EndsWith('/')))
    throw new InvalidOperationException("Cors:AllowedOrigins must contain valid http(s) origins.");
builder.Services.AddCors(options => options.AddPolicy("frontend", p => p
    .WithOrigins(corsOrigins).AllowAnyMethod().AllowAnyHeader()
    .WithExposedHeaders("Content-Disposition", "Retry-After")));

var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
if (provider == "Sqlite")
{
    var cs = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=lms-ai.sqlite";
    var sqlite = new SqliteConnectionStringBuilder(cs);
    if (sqlite.DataSource != ":memory:" && !Path.IsPathRooted(sqlite.DataSource))
        sqlite.DataSource = Path.GetFullPath(sqlite.DataSource, builder.Environment.ContentRootPath);
    if (sqlite.DataSource != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(sqlite.DataSource)!);
    builder.Services.AddDbContext<SqliteLmsDbContext>(o => o.UseSqlite(sqlite.ToString()));
    builder.Services.AddScoped<LmsDbContext>(s => s.GetRequiredService<SqliteLmsDbContext>());
}
else if (provider == "InMemory")
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("InMemory database is only allowed in Development for local demo.");
    builder.Services.AddDbContext<LmsDbContext>(o => o.UseInMemoryDatabase("LmsAiWeek11Demo"));
}
else if (provider == "SqlServer")
{
    var cs = builder.Configuration.GetConnectionString("LmsDb");
    if (string.IsNullOrWhiteSpace(cs)) throw new InvalidOperationException("Set ConnectionStrings__LmsDb for SQL Server.");
    builder.Services.AddDbContext<SqlServerLmsDbContext>(o => o.UseSqlServer(cs));
    builder.Services.AddScoped<LmsDbContext>(s => s.GetRequiredService<SqlServerLmsDbContext>());
}
else throw new InvalidOperationException("Database:Provider must be Sqlite, SqlServer or InMemory.");

var jwtKey = builder.Configuration["Jwt:Key"];
// Use an ephemeral secret only for local Development demos. Production always requires an explicit secret.
if (string.IsNullOrWhiteSpace(jwtKey) && builder.Environment.IsDevelopment())
{
    jwtKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
    Console.WriteLine("[DEV] A temporary JWT signing key was generated (new login after restart).");
}
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("Set Jwt__Key to a random secret of at least 32 UTF-8 bytes; do not commit it.");
var issuer = builder.Configuration["Jwt:Issuer"] ?? "LMS-AI";
var audience = builder.Configuration["Jwt:Audience"] ?? "LMS-AI-Frontend";
var secretKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
builder.Services.AddSingleton(new TokenConfig(issuer, audience, secretKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true, IssuerSigningKey = secretKey,
        ValidateIssuer = true, ValidIssuer = issuer,
        ValidateAudience = true, ValidAudience = audience,
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(20)
    };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            var raw = ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(raw, out var userId)) { ctx.Fail("Invalid user ID"); return; }
            var db = ctx.HttpContext.RequestServices.GetRequiredService<LmsDbContext>();
            var account = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ctx.HttpContext.RequestAborted);
            var versionClaim = ctx.Principal?.FindFirst("sv")?.Value;
            if (account is null || account.IsLocked ||
                account.Role != ctx.Principal?.FindFirst(ClaimTypes.Role)?.Value ||
                !int.TryParse(versionClaim, out var securityVersion) || securityVersion != account.SecurityVersion)
                ctx.Fail("Session is no longer valid");
        },
        OnChallenge = async ctx =>
        {
            ctx.HandleResponse();
            ctx.Response.StatusCode = 401;
            ctx.Response.Headers.WWWAuthenticate = "Bearer";
            await ctx.Response.WriteAsJsonAsync(new { error = "Phiên đăng nhập không hợp lệ hoặc đã hết hạn" });
        },
        OnForbidden = async ctx =>
        {
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsJsonAsync(new { error = "Bạn không có quyền thực hiện thao tác này" });
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICourseService, CourseService>();
builder.Services.AddScoped<MaterialService>();
builder.Services.AddScoped<QuestionService>();
builder.Services.AddScoped<AiQuestionService>();
builder.Services.AddScoped<QuizService>();
builder.Services.AddScoped<ResultService>();
builder.Services.AddScoped<AdminService>();
var storageRoot = builder.Configuration["Storage:RootPath"] ?? "Uploads";
builder.Services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(Path.GetFullPath(storageRoot, builder.Environment.ContentRootPath), storageOptions));
// Real AI is opt-in; all manual LMS functionality works with no external service.
var aiMode = builder.Configuration["Ai:Mode"] ?? "Disabled";
if (aiMode.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddHttpClient<IAIClient, GeminiAiClient>(client => client.Timeout = Timeout.InfiniteTimeSpan)
        .RemoveAllLoggers();
else if (aiMode.Equals("Mock", StringComparison.OrdinalIgnoreCase) && builder.Environment.IsDevelopment())
    builder.Services.AddSingleton<IAIClient, MockAiClient>();
else if (aiMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IAIClient, DisabledAiClient>();
else throw new InvalidOperationException("Ai:Mode must be Disabled, Gemini, or Mock (Development only).");

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Quá nhiều yêu cầu. Vui lòng thử lại sau một phút." }, ct);
    };
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Clamp(builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 30), 1, 300),
            Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
    options.AddPolicy("ai", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Trust only explicitly configured reverse proxy IP addresses (loopback is trusted by default).
    foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});

var app = builder.Build();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment() && app.Configuration.GetValue("Security:RequireHttps", true))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseCors("frontend");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok", provider, version = "1.1.0",
    aiEnabled = aiMode.Equals("Gemini", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(app.Configuration["Ai:GeminiApiKey"]),
    aiMode = app.Environment.IsDevelopment() ? aiMode : null
}));
app.MapGet("/api/health/ready", async (LmsDbContext db, CancellationToken ct) =>
{
    try { return await db.Database.CanConnectAsync(ct) &&
            (!db.Database.IsRelational() || !(await db.Database.GetPendingMigrationsAsync(ct)).Any())
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    catch { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
});
app.MapControllers();

// Migrate only in local Development by default. Production upgrades are explicit commands.
var migrateOnly = args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase);
if (migrateOnly || (app.Environment.IsDevelopment() && builder.Configuration.GetValue("Database:AutoMigrate", true)))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
    if (provider == "InMemory") await db.Database.EnsureCreatedAsync();
    else await DatabaseBootstrap.MigrateAsync(db);
}
if (builder.Configuration.GetValue("Demo:Seed", false))
{
    if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("Demo seed is restricted to Development.");
    using var scope = app.Services.CreateScope();
    await DemoSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<LmsDbContext>(),
        builder.Configuration["Demo:Password"] ?? "");
}
if (!string.IsNullOrWhiteSpace(builder.Configuration["Bootstrap:AdminUsername"]))
{
    using var scope = app.Services.CreateScope();
    await DemoSeeder.BootstrapAdminAsync(scope.ServiceProvider.GetRequiredService<LmsDbContext>(),
        builder.Configuration["Bootstrap:AdminUsername"]!, builder.Configuration["Bootstrap:AdminPassword"] ?? "",
        builder.Configuration["Bootstrap:AdminFullName"] ?? "Quản trị viên");
}
if (migrateOnly) return;
app.Run();

public sealed record TokenConfig(string Issuer, string Audience, SymmetricSecurityKey Key);
public partial class Program { } // Integration test host
