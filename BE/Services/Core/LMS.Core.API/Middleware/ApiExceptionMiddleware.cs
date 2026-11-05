using LMS.CoreApplicationServices.Common;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace LMS.Core.API.Middleware;

public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _log;
    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> log) { _next = next; _log = log; }
    public async Task InvokeAsync(HttpContext context)
    {
        try { await _next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (BadHttpRequestException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(new { error = "Request không hợp lệ hoặc vượt giới hạn kích thước" });
        }
        catch (JsonException)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "JSON không hợp lệ" });
        }
        catch (AppException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            context.Response.StatusCode = 409;
            await context.Response.WriteAsJsonAsync(new { error = "Dữ liệu vừa thay đổi. Vui lòng tải lại trước khi thử lại." });
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            context.Response.StatusCode = 409;
            await context.Response.WriteAsJsonAsync(new { error = "Dữ liệu đã tồn tại hoặc yêu cầu đã được thực hiện" });
        }
        catch (Exception ex)
        {
            // Do not log request payloads, SQL values, credentials or provider error bodies.
            _log.LogError("Unhandled API exception {ExceptionType}; trace {TraceId}", ex.GetType().Name, context.TraceIdentifier);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new { error = "Lỗi hệ thống. Vui lòng liên hệ quản trị viên.", traceId = context.TraceIdentifier });
        }
    }
}
