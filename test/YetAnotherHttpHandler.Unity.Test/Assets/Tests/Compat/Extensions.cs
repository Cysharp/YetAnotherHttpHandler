using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading;
using Grpc.Core;

internal static class TaskExtensions
{
    public static Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout)
        => WaitAsync(task, new CancellationTokenSource(timeout).Token);

    public static async Task<T> WaitAsync<T>(this Task<T> task, CancellationToken timeoutToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        timeoutToken.Register(() => tcs.SetResult(true));

        if (await Task.WhenAny(tcs.Task, task).ConfigureAwait(false) != task)
        {
            throw new TimeoutException();
        }

        return await task.ConfigureAwait(false);
    }

    public static Task WaitAsync(this Task task, TimeSpan timeout)
        => WaitAsync(task, new CancellationTokenSource(timeout).Token);

    public static async Task WaitAsync(this Task task, CancellationToken timeoutToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        timeoutToken.Register(() => tcs.SetResult(true));

        if (await Task.WhenAny(tcs.Task, task).ConfigureAwait(false) != task)
        {
            throw new TimeoutException();
        }

        await task.ConfigureAwait(false);
    }
}

internal static class HttpContentExtensions
{
    public static Task<byte[]> ReadAsByteArrayAsync(this HttpContent content, CancellationToken cancellationToken)
    {
        return content.ReadAsByteArrayAsync();
    }
}

internal static class IAsyncStreamReaderExtensions
{
    public static async IAsyncEnumerable<T> ReadAllAsync<T>(this IAsyncStreamReader<T> asyncStreamReader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await asyncStreamReader.MoveNext(cancellationToken).ConfigureAwait(false))
        {
            yield return asyncStreamReader.Current;
        }
    }
}