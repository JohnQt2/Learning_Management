using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LMS.Core.Infrastructure.Adapters;
using Microsoft.Extensions.Configuration;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL " + name);
    checks++; Console.WriteLine("PASS " + name);
}
async Task<AiClientException> Failure(Func<Task> action, int status, string name)
{
    try { await action(); throw new InvalidOperationException("Expected failure: " + name); }
    catch (AiClientException error) { Check(error.StatusCode == status, name); return error; }
}
IConfiguration Config(params (string Key, string Value)[] overrides)
{
    var values = new Dictionary<string, string?>
    {
        ["Ai:GeminiApiKey"] = "fixture-private-key", ["Ai:GeminiModel"] = GeminiAiClient.DefaultModel,
        ["Ai:RetryBaseDelaySeconds"] = "0.1", ["Ai:MaxRetryDelaySeconds"] = "1", ["Ai:MaxRetries"] = "1"
    };
    foreach (var setting in overrides) values[setting.Key] = setting.Value;
    return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
GeminiAiClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, IConfiguration? config = null)
    => new(new HttpClient(new Handler(respond)) { Timeout = Timeout.InfiniteTimeSpan }, config ?? Config());
HttpResponseMessage Success(string questionText = "[]") => new(HttpStatusCode.OK)
{
    Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = questionText } } } } } }), Encoding.UTF8, "application/json")
};
HttpResponseMessage Status(HttpStatusCode code, RetryConditionHeaderValue? retryAfter = null)
{
    var result = new HttpResponseMessage(code) { Content = new StringContent("fixture-private-key source-sensitive provider body") };
    result.Headers.RetryAfter = retryAfter;
    return result;
}
const string source = "source-sensitive: database transactions preserve consistency.";

var calls = 0;
var budget = new AiRequestBudget(4);
var recovered = await Client((_, _) => Task.FromResult(++calls == 1 ? Status(HttpStatusCode.ServiceUnavailable) : Success()))
    .GenerateJsonAsync(source, 1, "Medium", budget, default);
Check(calls == 2 && budget.Used == 2 && recovered == "[]", "503 retries once and counts both HTTP attempts");

calls = 0;
await Client((_, _) => Task.FromResult(++calls == 1 ? Status(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(TimeSpan.Zero)) : Success()))
    .GenerateJsonAsync(source, 1, "Easy", default);
Check(calls == 2, "429 delta Retry-After allows one bounded retry");

calls = 0;
await Client((_, _) => Task.FromResult(++calls == 1 ? Status(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(-1))) : Success()))
    .GenerateJsonAsync(source, 1, "Easy", default);
Check(calls == 2, "past Retry-After HTTP date is handled safely");

calls = 0;
var elapsed = Stopwatch.StartNew();
await Client((_, _) => Task.FromResult(++calls == 1 ? Status(HttpStatusCode.ServiceUnavailable, new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMilliseconds(250))) : Success()))
    .GenerateJsonAsync(source, 1, "Hard", default);
Check(calls == 2 && elapsed.ElapsedMilliseconds >= 200, "future Retry-After date is honored before next HTTP request");

foreach (var code in new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
{
    calls = 0;
    var error = await Failure(() => Client((_, _) => { calls++; return Task.FromResult(Status(code, new RetryConditionHeaderValue(TimeSpan.FromSeconds(3)))); })
        .GenerateJsonAsync(source, 1, "Medium", default), (int)code, $"{(int)code} long Retry-After returned safely");
    Check(calls == 1 && error.RetryAfter >= TimeSpan.FromSeconds(3), "Retry-After beyond configured cap is preserved and never shortened");
}

foreach (var code in new[] { 400, 401, 402, 403, 404, 500, 502, 504 })
{
    calls = 0;
    var expected = code == 500 ? 503 : code == 504 ? 504 : 502;
    var error = await Failure(() => Client((_, _) => { calls++; return Task.FromResult(Status((HttpStatusCode)code)); })
        .GenerateJsonAsync(source, 1, "Medium", default), expected, $"upstream {code} maps safe application status");
    Check(calls == 1 && error.ProviderStatusCode == code && !error.Message.Contains("fixture-private-key") && !error.Message.Contains("source-sensitive"), $"upstream {code} is not retried and excludes private response data");
}

calls = 0;
await Failure(() => Client((_, _) => { calls++; return Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)); }, Config(("Ai:MaxRetries", "2")))
    .GenerateJsonAsync(source, 1, "Medium", default), 503, "persistent temporary failure terminates");
Check(calls == 3, "configured two retries means at most three sends");

calls = 0;
await Failure(() => Client((_, _) => { calls++; return Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)); }, Config(("Ai:MaxRetries", "100")))
    .GenerateJsonAsync(source, 1, "Medium", default), 503, "oversized retry configuration terminates");
Check(calls == 3, "retry configuration is clamped to two retries");

calls = 0;
await Failure(() => Client((_, _) => { calls++; return Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)); }, Config(("Ai:MaxRetries", "0")))
    .GenerateJsonAsync(source, 1, "Medium", default), 503, "disabled retry preserves provider failure");
Check(calls == 1, "zero MaxRetries sends exactly once");

calls = 0;
var singleBudget = new AiRequestBudget(1);
await Failure(() => Client((_, _) => { calls++; return Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)); })
    .GenerateJsonAsync(source, 1, "Medium", singleBudget, default), 503, "request budget bounds retries");
Check(calls == 1 && singleBudget.Used == 1, "no waiting or second send once shared budget is exhausted");
var exhausted = new AiRequestBudget(1);
exhausted.Reserve(default);
calls = 0;
var exhaustedError = await Failure(() => Client((_, _) => { calls++; return Task.FromResult(Success()); })
    .GenerateJsonAsync(source, 1, "Medium", exhausted, default), 503, "exhausted shared budget fails locally");
Check(calls == 0 && exhaustedError.ProviderStatusCode == null, "local budget exhaustion never contacts provider");

foreach (var count in new[] { 0, 11 })
{
    var invalidBudget = new AiRequestBudget(4);
    await Failure(() => Client((_, _) => throw new Exception("Unexpected request"))
        .GenerateJsonAsync(source, count, "Medium", invalidBudget, default), 400, $"invalid chunk count {count} rejected");
    Check(invalidBudget.Used == 0, "validation happens before reserving provider budget");
}
foreach (var count in new[] { 1, 2, 3, 10 })
{
    await Client(async (request, ct) =>
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var instruction = body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString();
        Check(instruction!.Contains($"Tạo {count} câu hỏi") && request.Headers.Contains("x-goog-api-key") && !request.RequestUri!.Query.Contains("key"), $"chunk count {count} accepted with private key only in header");
        return Success();
    }).GenerateJsonAsync(source, count, "Medium", default);
}

calls = 0;
await Failure(() => Client((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not JSON") }); })
    .GenerateJsonAsync(source, 1, "Medium", default), 502, "invalid outer JSON rejected");
Check(calls == 1, "invalid JSON never triggers retry");
calls = 0;
var invalidQuestions = await Client((_, _) => { calls++; return Task.FromResult(Success("invalid question structure")); }).GenerateJsonAsync(source, 1, "Medium", default);
Check(calls == 1 && invalidQuestions == "invalid question structure", "question content validation stays in application layer without provider retry");
calls = 0;
await Failure(() => Client((_, _) => { calls++; throw new HttpRequestException("source-sensitive fixture-private-key"); })
    .GenerateJsonAsync(source, 1, "Medium", default), 502, "network failure is sanitized");
Check(calls == 1, "network errors are not retried");
calls = 0;
await Failure(() => Client((_, _) => { calls++; throw new TaskCanceledException(); })
    .GenerateJsonAsync(source, 1, "Medium", default), 504, "transport timeout maps safe failure");
Check(calls == 1, "timeouts are not retried");

using (var cancelled = new CancellationTokenSource())
{
    calls = 0;
    var firstResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var pending = Client((_, _) =>
    {
        calls++; firstResponse.TrySetResult();
        return Task.FromResult(Status(HttpStatusCode.TooManyRequests, new RetryConditionHeaderValue(TimeSpan.FromSeconds(10))));
    }, Config(("Ai:MaxRetryDelaySeconds", "20"))).GenerateJsonAsync(source, 1, "Medium", cancelled.Token);
    await firstResponse.Task;
    cancelled.Cancel();
    try { await pending; throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { Check(calls == 1, "cancellation during Retry-After wait prevents another request"); }
}

var parallelBudget = new AiRequestBudget(2);
var reserved = 0;
await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
{
    try { parallelBudget.Reserve(default); Interlocked.Increment(ref reserved); }
    catch (AiClientException) { }
})));
Check(reserved == 2 && parallelBudget.Used == 2, "shared request budget is atomic under concurrent reservation");
IAIClient oldFake = new OldOnlyClient();
var compatibleBudget = new AiRequestBudget(1);
Check(await oldFake.GenerateJsonAsync(source, 1, "Medium", compatibleBudget, default) == "[]" && compatibleBudget.Used == 1, "default overload reserves budget for existing old-signature adapters");
await Failure(() => oldFake.GenerateJsonAsync(source, 1, "Medium", compatibleBudget, default), 503, "old-signature adapter cannot bypass shared budget");

using (var firstMock = JsonDocument.Parse(await new MockAiClient().GenerateJsonAsync("First chapter describes database transactions.", 1, "Medium", default)))
using (var secondMock = JsonDocument.Parse(await new MockAiClient().GenerateJsonAsync("Second chapter describes access control policies.", 1, "Medium", default)))
    Check(firstMock.RootElement[0].GetProperty("content").GetString() != secondMock.RootElement[0].GetProperty("content").GetString(),
        "demo question content retains a source anchor across different document chunks");
using (var unicodeMock = JsonDocument.Parse(await new MockAiClient().GenerateJsonAsync(new string('a', 179) + "😀abc", 1, "Medium", default)))
    Check(unicodeMock.RootElement[0].GetProperty("options")[0].GetProperty("content").GetString() == new string('a', 179),
        "demo source excerpt never splits a UTF-16 surrogate pair");

Console.WriteLine($"AI ADAPTER TESTS PASSED: {checks} assertions; HTTP transport fixtures only, no provider calls.");

sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
}
sealed class OldOnlyClient : IAIClient
{
    public Task<string> GenerateJsonAsync(string sourceText, int count, string difficulty, CancellationToken ct) => Task.FromResult("[]");
}
