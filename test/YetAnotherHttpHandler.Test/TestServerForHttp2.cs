using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace _YetAnotherHttpHandler.Test;

class TestServerForHttp2 : ITestServerBuilder
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

        // HTTP/2
        app.MapGet("/error-reset", (HttpContext httpContext) =>
        {
            // https://learn.microsoft.com/ja-jp/aspnet/core/fundamentals/servers/kestrel/http2?view=aspnetcore-7.0#reset-1
            var resetFeature = httpContext.Features.Get<IHttpResetFeature>();
            resetFeature!.Reset(errorCode: 2); // INTERNAL_ERROR
            return Results.Empty;
        });

        return app;
    }
}