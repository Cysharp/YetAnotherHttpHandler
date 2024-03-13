using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using NugetForUnity.Models;
using UnityEngine;

namespace NugetForUnity
{
    public class ExposeInstalledPackagesManager
    {
        public static bool IsInstalled([NotNull] INugetPackageIdentifier package, bool checkIsAlreadyImportedInEngine)
            => InstalledPackagesManager.IsInstalled(package, checkIsAlreadyImportedInEngine);
        public static bool IsInstalled([NotNull] string packageId, bool checkIsAlreadyImportedInEngine)
            => InstalledPackagesManager.IsInstalled(packageId, checkIsAlreadyImportedInEngine);

        public static List<INugetPackage> GetInstalledRootPackages()
            => InstalledPackagesManager.GetInstalledRootPackages();
    }
}
