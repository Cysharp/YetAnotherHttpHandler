#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.Requests;

public static class PackageExporter
{
    private static ListRequest s_listRequest;

    [MenuItem("Tools/Export Unitypackage")]
    public static void Export()
    {
        EditorUtility.DisplayProgressBar("Packges", "Exporting packages, please wait...", 0);

        s_listRequest = UnityEditor.PackageManager.Client.List(offlineMode: false, includeIndirectDependencies: true);

        EditorApplication.update += OnEditorUpdate;
    }

    private static void OnEditorUpdate()
    {
        if (s_listRequest == null || !s_listRequest.IsCompleted)
        {
            return;
        }

        EditorApplication.update -= OnEditorUpdate;

        ExportPackage("com.cysharp.yetanotherhttphandler", "Cysharp.Net.Http.YetAnotherHttpHandler.Dependencies", includeSelf: false);
        ExportPackage("org.nuget.grpc.net.client", "Grpc.Net.Client.Dependencies", includeSelf: true);

        EditorUtility.ClearProgressBar();
    }

    private static void ExportPackage(string packageName, string unityPackageName, bool includeSelf)
    {
        List<UnityEditor.PackageManager.PackageInfo> dependencies = new();

        UnityEditor.PackageManager.PackageInfo packageInfo = s_listRequest.Result.First(p => p.name.Equals(packageName, StringComparison.Ordinal));

        if (includeSelf)
        {
            dependencies.Add(packageInfo);
        }

        GetAllDependencies(packageInfo, dependencies);

        string exportPath = $"./{unityPackageName}.unitypackage";

        AssetDatabase.ExportPackage(dependencies.Select(x => x.assetPath).ToArray(), exportPath, ExportPackageOptions.Recurse);

        UnityEngine.Debug.Log($"Export complete: {Path.GetFullPath(exportPath)}");
    }

    private static void GetAllDependencies(UnityEditor.PackageManager.PackageInfo packageInfo, ICollection<UnityEditor.PackageManager.PackageInfo> dependencies)
    {
        foreach (UnityEditor.PackageManager.DependencyInfo dependency in packageInfo.dependencies)
        {
            UnityEditor.PackageManager.PackageInfo resolvedPackageInfo = s_listRequest.Result.First(p => p.name.Equals(dependency.name, StringComparison.Ordinal));

            dependencies.Add(resolvedPackageInfo);

            GetAllDependencies(resolvedPackageInfo, dependencies);
        }
    }
}

#endif
