using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
        => NUnit.Framework.Assert.That(actual, NUnit.Framework.Is.EqualTo(expected).Using((IEqualityComparer<T>)EqualityComparer<T>.Default));
    public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        => NUnit.Framework.CollectionAssert.AreEqual(expected, actual);
    public static void Equal<T>(T[] expected, T[] actual)
        => NUnit.Framework.CollectionAssert.AreEqual(expected, actual);

    public static void False(bool actual)
        => NUnit.Framework.Assert.False(actual);

    public static void True(bool actual)
        => NUnit.Framework.Assert.True(actual);

    public static void Null<T>(T actual)
        => NUnit.Framework.Assert.Null(actual);

    public static void NotNull<T>(T actual)
        => NUnit.Framework.Assert.NotNull(actual);

    public static T IsAssignableFrom<T>(object actual)
    {
        NUnit.Framework.Assert.IsInstanceOf<T>(actual);
        return (T)actual;
    }

    public static void Contains(string expected, string actual)
        => NUnit.Framework.Assert.That(() => actual.Contains(expected));

    public static T IsType<T>(object actual)
    {
        NUnit.Framework.Assert.True(actual.GetType() == typeof(T), $"Expected: {typeof(T)}\nActual: {actual.GetType()}");
        return (T)actual;
    }
}

internal static class Record
{
    public static async Task<Exception> ExceptionAsync(Func<Task> asyncFunc)
    {
        try
        {
            await asyncFunc().ConfigureAwait(false);
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }
}