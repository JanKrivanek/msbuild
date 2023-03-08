// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NugetUtils
{
    public class NugetDownloader
    {
        private readonly string _baseUrl;
        private readonly string _cacheDir;

        public NugetDownloader(string? baseUrl = null, string? cacheDir = null)
        {
            _baseUrl = baseUrl ?? "https://api.nuget.org/v3-flatcontainer/";
            _cacheDir = cacheDir ?? "nugets";
        }

        public NugetDownloader() { }

        public void Foo(string packageName, string? version)
        {
            version = version ?? GetLatestVersion(packageName);

            //todo: cache + latest from cache arg

            string downloadEndpoint = $"{_baseUrl}{packageName}/{version}/{packageName}.{version}.nupkg";
            HttpClientExtensions.SharedHttpClient.DownloadFileAsync(new Uri(downloadEndpoint),
                $"{packageName}-{version}");
        }

        private static string GetLatestVersion(string packageName)
        {
            throw new NotImplementedException("TBD");
        }
    }
}
