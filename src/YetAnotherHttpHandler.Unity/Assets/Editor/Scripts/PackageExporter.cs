#if UNITY_EDITOR

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class PackageExporter
{
    [MenuItem("Tools/Export Unitypackage")]
    public static void Export()
    {
        PackDependencies("Cysharp.Net.Http.YetAnotherHttpHandler.Dependencies", "YetAnotherHttpHandler.deps.txt");
        PackDependencies("Grpc.Net.Client.Dependencies", "Grpc.Net.Client.deps.txt");
    }

    static void PackDependencies(string unityPackageName, string dependenciesListFileName)
    {
        Debug.Log($"Creating package '{unityPackageName}'...");
        var pluginsDir = Path.Combine(Application.dataPath, "Plugins");
        var libraryNames = File.ReadAllLines(Path.Combine(pluginsDir, dependenciesListFileName));
        Debug.Log($"Includes library: {string.Join(';', libraryNames)}");

        var exportPath = $"./{unityPackageName}.unitypackage";
        AssetDatabase.ExportPackage(
            libraryNames.Select(x => $"Assets/Plugins/{x}").ToArray(),
            exportPath,
            ExportPackageOptions.Recurse);

        UnityEngine.Debug.Log("Export complete: " + Path.GetFullPath(exportPath));
    }
}

#endif
