namespace NugetUtils;

public struct PackageStatsInfo
{
    public PackageStatsInfo(string name, int downloadsCount)
    {
        Name = name;
        DownloadsCount = downloadsCount;
    }

    public string Name { get; }
    public int DownloadsCount { get; }
}
