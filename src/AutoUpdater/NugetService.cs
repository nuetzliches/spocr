using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SpocR.Services;


// TODO Implement it as OOP based
// Maybe move to Nuts.Packages.Providers

namespace SpocR.AutoUpdater;

public class NugetService(
    IConsoleService consoleService
) : IPackageManager
{
    private readonly string _url = "https://api-v2v3search-0.nuget.org/query?q=spocr&take=1";

    public async Task<Version> GetLatestVersionAsync()
    {
        var latest = default(Version);
        using (var httpClient = new HttpClient())
        {
            try
            {
                httpClient.Timeout = TimeSpan.FromSeconds(10); // Timeout nach 10 Sekunden
                var response = await httpClient.GetAsync(_url);

                if (!response.IsSuccessStatusCode)
                {
                    consoleService.Warn($"Failed to check for updates: Server returned {(int)response.StatusCode} {response.ReasonPhrase}");
                    return latest;
                }

                var searchResponse = await response.Content.ReadAsAsync<SearchResponse>();
                var packages = searchResponse.Data.ToList();

                if (packages.Count == 0)
                {
                    consoleService.Warn("No package information found from NuGet");
                    return latest;
                }

                latest = Version.Parse(packages.First().Version);
            }
            catch (HttpRequestException ex)
            {
                consoleService.Warn($"Network error while checking for updates: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                consoleService.Warn("Update check timed out. Please check your internet connection.");
            }
            catch (Exception ex)
            {
                consoleService.Warn($"Unexpected error checking for updates: {ex.Message}");
            }
        }
        return latest;
    }
}

public class SearchResponse
{
    public int TotalHits { get; set; }
    public IEnumerable<Package> Data { get; set; }
}

public class Package
{
    [JsonPropertyName("@id")]
    public string ApiId { get; set; }

    [JsonPropertyName("@package")]
    public string Type { get; set; }

    public string Registration { get; set; }
    public string Id { get; set; }
    public string Version { get; set; }
    public string Description { get; set; }
    public string Summary { get; set; }
    public string Title { get; set; }
    public string IconUrl { get; set; }
    public string LicenseUrl { get; set; }
    public string ProjectUrl { get; set; }
    public IEnumerable<string> Tags { get; set; }
    public IEnumerable<string> Authors { get; set; }
    public int TotalDownloads { get; set; }
    public bool Verified { get; set; }
    public IEnumerable<PackageVersion> Versions { get; set; }
}

public class PackageVersion
{
    [JsonPropertyName("@id")]
    public string Id { get; set; }
    public string Version { get; set; }
    public int Downloads { get; set; }
}
