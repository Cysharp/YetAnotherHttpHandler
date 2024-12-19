// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Xunit;
using Xunit.Sdk;

namespace _YetAnotherHttpHandler.Test.Helpers.Testing;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[XunitTestCaseDiscoverer("_YetAnotherHttpHandler.Test.Helpers.Testing." + nameof(ConditionalFactDiscoverer), "YetAnotherHttpHandler.Test")]
public class ConditionalFactAttribute : FactAttribute
{
}