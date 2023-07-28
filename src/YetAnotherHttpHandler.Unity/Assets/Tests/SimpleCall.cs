using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Cysharp.Net.Http;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class SimpleCall
{
    [Test]
    public async Task Once()
    {
        // Arrange
        using var httpHandler = new YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);

        // Act
        var result = await httpClient.GetStringAsync("https://cysharp.co.jp/");

        // Assert
        Assert.IsNotEmpty(result);
    }

    [Test]
    public async Task FailedToConnect()
    {
        // Arrange
        using var httpHandler = new YetAnotherHttpHandler();
        var httpClient = new HttpClient(httpHandler);

        // Act & Assert
        var ex = Assert.ThrowsAsync<HttpRequestException>(async () => await httpClient.GetStringAsync("https://doesnotexists-1234567890/").ConfigureAwait(false) /* We need ConfigureAwait(false) to prevent deadlock during tests */);
    }
}
