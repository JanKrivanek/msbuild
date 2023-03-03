// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using System.Xml.Linq;
using HtmlAgilityPack;

namespace NugetUtils
{
    public class NugetStats
    {
        private const string nugetStatsUrl = @"https://www.nuget.org/stats/packages";
        private HttpClient _client = new HttpClient();

        public async Task AsyncMethod()
        {
            using HttpResponseMessage response = await _client.GetAsync(nugetStatsUrl);
            using HttpContent content = response.Content;
            string res = await content.ReadAsStringAsync();

            HtmlDocument document = new HtmlDocument();
            document.LoadHtml(res);
            var tables = document.DocumentNode.Descendants("table").ToList();

            //data-bind="visible: showAllPackageDownloads"
            //data-bind="visible: !showAllPackageDownloads()"
            //aria-label="Community packages with the most downloads"

            HtmlNode communityTable = tables.First(t =>
                t.Attributes.Any(a => a.Value.Contains("community", StringComparison.CurrentCultureIgnoreCase)));

            HtmlNode allTable = tables.First(t => !object.ReferenceEquals(t, communityTable));
        }

        private void ParseTable(HtmlNode table)
        {
            //tbd
        }
    }
}
