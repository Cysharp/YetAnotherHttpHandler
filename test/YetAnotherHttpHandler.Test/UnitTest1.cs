namespace _YetAnotherHttpHandler.Test;

public class UnitTest1
{
    [Fact]
    public async Task Test1()
    {
        // Arrange
        using var httpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);

        // Act
        var result = await httpClient.GetStringAsync("https://localhost/");

    }
}