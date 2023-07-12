# YetAnotherHttpHandler.Packaging.Test
This project verifies that the native library can be referenced from the package.

## Instruction

- Build and pack `YetAnotherHttpHandler` project.
- Run `dotnet nuget locals all --clear`
	- ⚠ WARN: Flush all NuGet package caches on your computer.
- Run `dotnet restore`
- Run `dotnet clean`
- Run `dotnet test`