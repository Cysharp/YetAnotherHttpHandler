namespace _YetAnotherHttpHandler.Test;

static class StreamExtensions
{
    public static async Task<byte[]> ToArrayAsync(this Stream stream)
    {
        var memStream = new MemoryStream();
        await stream.CopyToAsync(memStream);
        return memStream.ToArray();
    }
}