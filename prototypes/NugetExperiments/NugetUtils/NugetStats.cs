// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Xml.Linq;
using HtmlAgilityPack;

namespace NugetUtils
{
    public class NugetStats
    {
        private const string nugetStatsUrl = @"https://www.nuget.org/stats/packages";
        private List<PackageStatsInfo>? _communityPackages;
        private List<PackageStatsInfo>? _allPackages;
        private readonly Task _initializeTask;

        public IReadOnlyList<PackageStatsInfo> CommunityPackages => _communityPackages!;
        public IReadOnlyList<PackageStatsInfo> AllPackages => _allPackages!;

        public NugetStats()
        {
            _initializeTask = Initialize();
        }

        public async Task WaitForInitialization()
        {
            await _initializeTask.ConfigureAwait(false);
        }

        private async Task Initialize()
        {
            using HttpResponseMessage response = await HttpClientExtensions.SharedHttpClient.GetAsync(nugetStatsUrl);
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

            _communityPackages = ParseTable(communityTable).ToList();
            _allPackages = ParseTable(allTable).ToList();
        }

        private IEnumerable<PackageStatsInfo> ParseTable(HtmlNode table)
        {
            foreach (HtmlNode row in table.Descendants("tr"))
            {
                // intentionally skipping header rows (with th data)
                var dataCells = row.SelectNodes("td");
                if ((dataCells?.Count ?? 0) == 0)
                {
                    // header
                    continue;
                }

                if (dataCells!.Count != 3)
                {
                    throw new Exception(
                        $"Unexpected count of data cells ({dataCells.Count}). Likely unknown table format change.");
                }

                yield return new PackageStatsInfo(ExtractInnerLinkText(dataCells[1]),
                    int.Parse(ExtractInnerLinkText(dataCells[2]), NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture));
            }
        }

        private static string ExtractInnerLinkText(HtmlNode node)
        {
            return node.SelectSingleNode("a").InnerText;
        }
    }
}
