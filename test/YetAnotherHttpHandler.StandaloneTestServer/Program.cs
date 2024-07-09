using _YetAnotherHttpHandler.Test;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var app = TestServerForHttp1AndHttp2.BuildApplication(builder);

app.Run();
