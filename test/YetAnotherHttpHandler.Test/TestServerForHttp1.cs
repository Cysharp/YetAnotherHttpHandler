using System.IO.Pipelines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace _YetAnotherHttpHandler.Test;

public class TestServerForHttp1 : ITestServerBuilder
{
    public static WebApplication BuildApplication(WebApplicationBuilder builder)
    {
        var app = builder.Build();

        app.MapGet("/", () => Results.Content("__OK__"));
        app.MapGet("/not-found", () => Results.Content("__Not_Found__", statusCode: 404));
        app.MapGet("/response-headers", (HttpContext httpContext) =>
        {
            httpContext.Response.Headers["x-test"] = "foo";
            return Results.Content("__OK__");
        });
        app.MapGet("/ハロー", () => Results.Content("Konnichiwa"));
        app.MapPost("/slow-upload", async (HttpContext ctx, PipeReader reader) =>
        {
            while (true)
            {
                await Task.Delay(1000);
                var readResult = await reader.ReadAsync();
                reader.AdvanceTo(readResult.Buffer.End);
                if (readResult.IsCompleted || readResult.IsCanceled) break;
            }
            return Results.Content("OK");
        });

        return app;
    }
}