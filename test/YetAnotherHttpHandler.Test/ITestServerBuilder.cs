using Microsoft.AspNetCore.Builder;

namespace _YetAnotherHttpHandler.Test;

public interface ITestServerBuilder
{
    static abstract WebApplication BuildApplication(WebApplicationBuilder builder);
}