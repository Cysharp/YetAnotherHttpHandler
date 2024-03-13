#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NugetForUnity;
using NugetForUnity.Configuration;
using NugetForUnity.Models;
using UnityEditor;
using UnityEngine;

public static class PackageExporter
{
    [MenuItem("Tools/Export Unitypackage")]
    public static void Export()
    {
        EditorUtility.DisplayProgressBar("Packges", "Exporting packages, please wait...", 0);

        var packagesYahaDependencies = new[] { "System.IO.Pipelines", "System.Runtime.CompilerServices.Unsafe" };
        var packagesGrpcNetClient = new[] { "Grpc.Net.Client" };
        ExportNuGetPackage(packagesYahaDependencies, "Cysharp.Net.Http.YetAnotherHttpHandler.Dependencies", Array.Empty<string>());
        ExportNuGetPackage(packagesGrpcNetClient, "Grpc.Net.Client.Dependencies", packagesYahaDependencies);
    }

    private static void ExportNuGetPackage(IReadOnlyList<string> packageIds, string unityPackageName, IReadOnlyList<string> excludePackageIds)
    {
        string exportPath = $"./{unityPackageName}.unitypackage";

        var packages = new HashSet<INugetPackageIdentifier>();
        var installedPackages = InstalledPackagesManager.InstalledPackages.ToArray();
        foreach (var package in installedPackages.Where(x => packageIds.Contains(x.Id)))
        {
            foreach (var dep in TraverseDependencies(installedPackages, package, includesSelf: true))
            {
                if (excludePackageIds.Contains(dep.Id))
                {
                    continue;
                }
                packages.Add(dep);
            }
        }

        var assetPaths = packages
            .Select(x =>
            {
                var directory = GetPackageInstallDirectory(x);
                return "Assets" + directory.Substring(Application.dataPath.Length);
            })
            .ToArray();

        Debug.Log($"Exporting: {Path.GetFullPath(exportPath)}");
        foreach (var package in assetPaths)
        {
            Debug.Log(package);
        }

        AssetDatabase.ExportPackage(assetPaths, exportPath, ExportPackageOptions.Recurse);

        Debug.Log($"Export complete");

        static IEnumerable<INugetPackage> TraverseDependencies(IReadOnlyList<INugetPackage> installedPackages, INugetPackage package, bool includesSelf)
        {
            if (includesSelf)
            {
                yield return package;
            }

            foreach (var dep in package.CurrentFrameworkDependencies)
            {
                var depPackage = installedPackages.FirstOrDefault(x => x.Id == dep.Id);
                if (depPackage != null)
                {
                    yield return depPackage;

                    foreach (var depDep in TraverseDependencies(installedPackages, depPackage, includesSelf: false))
                    {
                        yield return depDep;
                    }
                }
            }
        }
    }

    private static string GetPackageInstallDirectory(INugetPackageIdentifier package)
    {
        return Path.Combine(ConfigurationManager.NugetConfigFile.RepositoryPath, $"{package.Id}.{package.Version}");
    }
}
#endif
