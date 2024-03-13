using NugetForUnity.PluginAPI;
using System.Collections;
using System.Collections.Generic;
using System.IO.Compression;
using NugetForUnity.PluginAPI.ExtensionPoints;
using NugetForUnity.PluginAPI.Models;
using UnityEngine;

public class BuildTransitiveFilterPlugin : INugetPlugin
{
    public void Register(INugetPluginRegistry registry)
    {
        registry.RegisterPackageInstallFileHandler(new PackageInstallFileHandler());
    }

    private class PackageInstallFileHandler : IPackageInstallFileHandler
    {
        public bool HandleFileExtraction(INugetPackage package, ZipArchiveEntry entry, string extractDirectory)
        {
            //Debug.Log($"Install: {entry.FullName}; package={package.Id}; extractDirectory={extractDirectory}");
            return entry.FullName.Contains("buildTransitive");
        }
    }
}
