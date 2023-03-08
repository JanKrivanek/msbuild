// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NugetUtils
{
    internal static class HttpClientExtensions
    {
        public static readonly HttpClient SharedHttpClient = new HttpClient();

        public static async Task DownloadFileAsync(this HttpClient client, Uri uri, string fileName)
        {
            await using var s = await client.GetStreamAsync(uri).ConfigureAwait(false);
            await using var fs = new FileStream(fileName, FileMode.CreateNew);
            await s.CopyToAsync(fs).ConfigureAwait(false);
        }
    }
}
