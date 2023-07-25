// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace _YetAnotherHttpHandler.Test.Helpers.Testing;

public interface ITestCondition
{
    bool IsMet { get; }

    string SkipReason { get; }
}