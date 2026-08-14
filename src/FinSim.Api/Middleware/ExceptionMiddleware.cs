using System.Diagnostics;
using System.Text.Json;

namespace FinSim.Api.Middleware
{
    /// <summary>
    /// Catches anything the controllers and services did not handle themselves.
    /// Expected failures already come back as codes ("InsufficientFunds") from
    /// the result enums — this is only for genuine bugs: null references,
    /// database timeouts, and so on.
    ///
    /// The real exception goes to the log. The client gets a stable code and a
    /// trace id it can quote back, plus the actual detail while in development.
    /// </summary>
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IHostEnvironment _env;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger,
            IHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _env = env;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The browser navigated away mid-request. Not an error worth logging.
                _logger.LogDebug("Request aborted by the client: {Path}", context.Request.Path);
            }
            catch (Exception ex)
            {
                var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

                _logger.LogError(ex,
                    "Unhandled exception on {Method} {Path} (trace {TraceId})",
                    context.Request.Method, context.Request.Path, traceId);

                // If something already started writing a response we cannot
                // replace it — the headers are gone. Let it fail as-is.
                if (context.Response.HasStarted)
                {
                    _logger.LogWarning("Response already started; cannot write an error body.");
                    return;
                }

                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json; charset=utf-8";

                var body = _env.IsDevelopment()
                    ? new
                    {
                        error = "ServerError",
                        traceId,
                        detail = ex.Message,
                        type = ex.GetType().Name,
                        stack = ex.StackTrace
                    }
                    : (object)new
                    {
                        error = "ServerError",
                        traceId
                    };

                await context.Response.WriteAsync(
                    JsonSerializer.Serialize(body,
                        new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        }));
            }
        }
    }

    public static class ExceptionMiddlewareExtensions
    {
        public static IApplicationBuilder UseFinSimExceptionHandler(this IApplicationBuilder app) =>
            app.UseMiddleware<ExceptionMiddleware>();
    }
}