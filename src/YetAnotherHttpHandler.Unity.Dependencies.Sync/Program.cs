// See https://aka.ms/new-console-template for more information

using System.Text.Json;

var projectAssetJsonPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, @"..\YetAnotherHttpHandler.Unity.Dependencies\obj\project.assets.json"));
Console.WriteLine($"projectAssetJsonPath: {projectAssetJsonPath}");
if (!File.Exists(projectAssetJsonPath))
{
    Console.Error.WriteLine("Error: project.assets.json is not found. Please execute `dotnet build ../YetAnotherHttpHandler.Dependencies` before run this.");
    return -1;
}

var pluginBaseDir = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, @"..\YetAnotherHttpHandler.Unity\Assets\Plugins"));
Console.WriteLine($"pluginBaseDir: {pluginBaseDir}");
if (!Directory.Exists(pluginBaseDir))
{
    Console.Error.WriteLine($"Error: Cannot load project.assets.json.");
    return -1;
}

// Load project.assets.json
var jsonSerializerOptions = new JsonSerializerOptions()
{
    PropertyNameCaseInsensitive = true,
};
var projectAssets = JsonSerializer.Deserialize<PackageAssetsJson>(File.ReadAllText(projectAssetJsonPath), jsonSerializerOptions);
if (projectAssets == null)
{
    Console.Error.WriteLine($"Error: Plugins directory is not found in YetAnotherHttpHandler.Unity project.");
    return -1;
}

Console.WriteLine($"PackageFolders: {string.Join(";", projectAssets.PackageFolders.Keys)}");

// Determine the files that need to be copied
var copyItems = new List<CopyToPlugins>();
var targetNetStandard2_1 = projectAssets.Targets[".NETStandard,Version=v2.1"];
foreach (var dep in targetNetStandard2_1)
{
    var licenseFiles = projectAssets.Libraries[dep.Key].Files.Where(x => x.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase));
    copyItems.Add(new CopyToPlugins(dep.Key, dep.Value.Runtime.Keys.Concat(licenseFiles).ToArray()));
}

// List the files that will be copied.
foreach (var item in copyItems)
{
    var srcDir = projectAssets.PackageFolders.Select(x => Path.Combine(x.Key, item.BaseName.ToLower())).FirstOrDefault(x => Directory.Exists(x));
    if (srcDir is null)
    {
        Console.Error.WriteLine($"Error: NuGet package '{item.BaseName}' not found in any package directories.");
        return -1;
    }
    var destDir = Path.Combine(pluginBaseDir, item.BaseName);
    foreach (var file in item.Files)
    {
        var src = Path.Combine(srcDir, file);
        var dest = Path.Combine(destDir, file);

        var destFileDir = Path.GetDirectoryName(dest);
        if (!Directory.Exists(destFileDir))
        {
            Console.WriteLine($"Create directory: {destFileDir}");
            Directory.CreateDirectory(destFileDir);
        }

        Console.WriteLine($"Copy: {src} -> {dest}");
        File.Copy(src, dest, overwrite: true);
    }
}

// Create the dependencies lists to build .unitypackage.
HashSet<string> depsForYaha;
HashSet<string> depsForGrpc;
{
    var depsListFileName = "YetAnotherHttpHandler.deps.txt";
    var depsRootPackage = "System.IO.Pipelines";
    var outputDepsListPath = Path.Combine(pluginBaseDir, depsListFileName);
    var deps = ResolveDependencies(depsRootPackage, targetNetStandard2_1);
    Console.WriteLine($"Write dependencies list: {outputDepsListPath}");
    File.WriteAllText(outputDepsListPath, string.Join("\r\n", deps));
    depsForYaha = deps;
}
{
    var depsListFileName = "Grpc.Net.Client.deps.txt";
    var depsRootPackage = "Grpc.Net.Client";
    var outputDepsListPath = Path.Combine(pluginBaseDir, depsListFileName);
    var deps = ResolveDependencies(depsRootPackage, targetNetStandard2_1, depsForYaha); // No need to include System.IO.Pipelines and other packages.
    Console.WriteLine($"Write dependencies list: {outputDepsListPath}");
    File.WriteAllText(outputDepsListPath, string.Join("\r\n", deps.OrderBy(x => x)));
    depsForGrpc = deps;
}

// List synced packages.
Console.WriteLine("Dependencies for YetAnotherHttpHandler:");
foreach (var dep in depsForYaha)
{
    var lib = targetNetStandard2_1.Keys.FirstOrDefault(x => x.StartsWith(dep + '/'));
    Console.WriteLine("- " + lib.Replace('/', ' '));
}
Console.WriteLine("Dependencies for Grpc.Net.Client:");
foreach (var dep in depsForGrpc)
{
    var lib = targetNetStandard2_1.Keys.FirstOrDefault(x => x.StartsWith(dep + '/'));
    Console.WriteLine("- " + lib.Replace('/', ' '));
}

return 0;

static HashSet<string> ResolveDependencies(string library, IReadOnlyDictionary<string, Dependency> installedDeps, HashSet<string>? excludes = null)
{
    var hashSet = new HashSet<string>();
    hashSet.Add(library);
    var targetLib = installedDeps.FirstOrDefault(x => x.Key.StartsWith(library + "/"));
    foreach (var dep in targetLib.Value.Dependencies?.Keys ?? Array.Empty<string>())
    {
        if (excludes is not null && excludes.Contains(dep)) continue;

        hashSet.Add(dep);
        foreach (var depdep in ResolveDependencies(dep, installedDeps, excludes))
        {
            hashSet.Add(depdep);
        }
    }
    return hashSet;
}
record CopyToPlugins(string BaseName, IReadOnlyList<string> Files);

public record AssetDetails(string Related);
public record Dependency(string Type, IDictionary<string, string>? Dependencies, IReadOnlyDictionary<string, AssetDetails> Compile, IReadOnlyDictionary<string, AssetDetails> Runtime);
public record Library(string Sha512, string Type, string Path, IReadOnlyList<string> Files);
public record PackageAssetsJson(
    int Version,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, Dependency>> Targets,
    IReadOnlyDictionary<string, Library> Libraries,
    IReadOnlyDictionary<string, object> PackageFolders
);