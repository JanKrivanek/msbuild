// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using NugetUtils;

namespace Runner
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            //await new NugetStats().AsyncMethod();

            NugetStats nugetStats = new();
            await nugetStats.WaitForInitialization().ConfigureAwait(false);
            foreach (PackageStatsInfo nugetStatsCommunityPackage in nugetStats.CommunityPackages)
            {
                Console.WriteLine(nugetStatsCommunityPackage.Name + ":" + nugetStatsCommunityPackage.DownloadsCount);
            }
        }
    }
}
