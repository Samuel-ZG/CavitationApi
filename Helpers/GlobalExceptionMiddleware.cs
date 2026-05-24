// ============================================================
//  GLOBAL EXCEPTION HANDLER MIDDLEWARE
//  Archivo: Helpers/GlobalExceptionMiddleware.cs
//
//  Agregar en Program.cs antes de app.UseCors():
//  app.UseMiddleware<GlobalExceptionMiddleware>();
// ============================================================

using System.Net;
using System.Text.Json;

namespace CavitationApi.Helpers;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Acceso no autorizado: {Path}", context.Request.Path);
            await WriteErrorAsync(context, HttpStatusCode.Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Operación inválida: {Path}", context.Request.Path);
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Recurso no encontrado: {Path}", context.Request.Path);
            await WriteErrorAsync(context, HttpStatusCode.NotFound, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error no controlado: {Path}", context.Request.Path);
            await WriteErrorAsync(
                context,
                HttpStatusCode.InternalServerError,
                "Error interno del servidor. Por favor contacte al administrador.");
        }
    }

    private static async Task WriteErrorAsync(
        HttpContext context, HttpStatusCode status, string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode  = (int)status;

        var body = JsonSerializer.Serialize(new
        {
            statusCode = (int)status,
            message,
            timestamp = DateTime.UtcNow
        });

        await context.Response.WriteAsync(body);
    }
}
