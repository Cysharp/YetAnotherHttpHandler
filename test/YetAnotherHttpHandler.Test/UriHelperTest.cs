using Cysharp.Net.Http;

namespace _YetAnotherHttpHandler.Test;

public class UriHelperTest
{
    [Fact]
    public void ToSafeUriString_Ascii()
    {
        // Arrange
        var uri = new Uri("https://localhost.local:12345/path/path2?query&query2");

        // Act
        var safeUri = UriHelper.ToSafeUriString(uri);

        // Assert
        Assert.Equal("https://localhost.local:12345/path/path2?query&query2", safeUri);
    }

    [Fact]
    public void ToSafeUriString_NonAscii()
    {
        // Arrange
        var uri = new Uri("https://ローカルホスト:12345/パス/パス2?クエリーストリング&%E3%82%AF%E3%82%A8%E3%83%AA%E3%83%BC%E3%82%B9%E3%83%88%E3%83%AA%E3%83%B3%E3%82%B02");

        // Act
        var safeUri = UriHelper.ToSafeUriString(uri);

        // Assert
        Assert.Equal("https://xn--lck2a7b3dwdk9h:12345/%E3%83%91%E3%82%B9/%E3%83%91%E3%82%B92?%E3%82%AF%E3%82%A8%E3%83%AA%E3%83%BC%E3%82%B9%E3%83%88%E3%83%AA%E3%83%B3%E3%82%B0&%E3%82%AF%E3%82%A8%E3%83%AA%E3%83%BC%E3%82%B9%E3%83%88%E3%83%AA%E3%83%B3%E3%82%B02", safeUri);
    }
}