using Cysharp.Net.Http;

namespace _YetAnotherHttpHandler.Test;

public class YetAnotherHttpHandlerTest
{
    [Fact]
    public async Task Disposed()
    {
        // Arrange
        var handler = new YetAnotherHttpHandler();
        handler.Dispose();
        var httpClient = new HttpClient(handler);

        // Act
        var ex = await Record.ExceptionAsync(async () => await httpClient.GetStringAsync("http://localhost"));

        // Assert
        Assert.IsType<ObjectDisposedException>(ex);
    }
}