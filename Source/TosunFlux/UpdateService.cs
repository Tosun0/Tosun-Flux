using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace TosunFlux;

internal sealed record UpdateInfo(Version Version, string DownloadUrl, string Sha256);

internal static class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Tosun0/Tosun-Flux/releases/latest";
    private const string InstallerAssetName = "Tosun Flux Setup.exe";
    private const string NormalizedInstallerAssetName = "Tosun.Flux.Setup.exe";
    private static readonly HttpClient Client = CreateClient();

    public static async Task<UpdateInfo?> CheckAsync(Version currentVersion)
    {
        using var response = await Client.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var latestVersion) || latestVersion <= currentVersion)
            return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString();
            if (!string.Equals(assetName, InstallerAssetName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(assetName, NormalizedInstallerAssetName, StringComparison.OrdinalIgnoreCase))
                continue;

            var downloadUrl = asset.GetProperty("browser_download_url").GetString();
            var digest = asset.TryGetProperty("digest", out var digestValue) ? digestValue.GetString() : null;
            if (downloadUrl is null || digest is null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                return null;
            return new UpdateInfo(latestVersion, downloadUrl, digest[7..]);
        }

        return null;
    }

    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<int> progress)
    {
        var updateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tosun Flux", "Updates");
        Directory.CreateDirectory(updateDirectory);
        var destination = Path.Combine(updateDirectory, $"Tosun Flux Setup {update.Version}.exe");

        using var response = await Client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using (var input = await response.Content.ReadAsStreamAsync())
        await using (var output = File.Create(destination))
        {
            var buffer = new byte[128 * 1024];
            long received = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer);
                if (count == 0)
                    break;
                await output.WriteAsync(buffer.AsMemory(0, count));
                received += count;
                if (total is > 0)
                    progress.Report((int)Math.Clamp(received * 100 / total.Value, 0, 100));
            }
        }

        await using var downloaded = File.OpenRead(destination);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(downloaded));
        if (!actualHash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destination);
            throw new InvalidDataException("업데이트 설치파일의 무결성 검증에 실패했습니다.");
        }

        progress.Report(100);
        CleanupPreviousInstallers(updateDirectory, destination);
        return destination;
    }

    private static void CleanupPreviousInstallers(string updateDirectory, string currentInstaller)
    {
        var currentPath = Path.GetFullPath(currentInstaller);
        foreach (var path in Directory.EnumerateFiles(updateDirectory, "Tosun Flux Setup *.exe"))
        {
            if (string.Equals(Path.GetFullPath(path), currentPath, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // 사용 중인 이전 인스톨러는 다음 업데이트 때 다시 정리합니다.
            }
            catch (UnauthorizedAccessException)
            {
                // 접근할 수 없는 이전 인스톨러는 삭제하지 않고 보존합니다.
            }
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Tosun-Flux/1.1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
