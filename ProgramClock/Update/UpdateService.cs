using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace ProgramClock.Update;

/// <summary>Describes the latest published release and its downloadable asset.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string DownloadUrl, long Size);

/// <summary>
/// Checks GitHub Releases for a newer build, downloads + extracts the self-contained exe, and
/// performs a self-replacing restart. This is the ONLY code in ProgramClock that touches the
/// network; it runs only when the user clicks an update button or opts in to the daily check.
/// No telemetry or usage data is ever sent — these are plain HTTPS GETs to the public GitHub API.
/// </summary>
public static class UpdateService
{
    private const string Owner = "CoreyMills";
    private const string Repo = "ProgramClock";
    private const string AssetName = "ProgramClock-win-x64.zip";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // GitHub's API rejects requests without a User-Agent.
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ProgramClock", CurrentVersion.ToString()));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>The version baked into this assembly (from the csproj &lt;Version&gt;).</summary>
    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>
    /// Queries the latest release. Returns its <see cref="UpdateInfo"/>, or null if the request
    /// fails, the release has no matching asset, or the tag isn't a parseable version.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagEl)) return null;
            var tag = tagEl.GetString();
            if (string.IsNullOrWhiteSpace(tag)) return null;

            if (!TryParseVersion(tag, out var version)) return null;

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("name", out var nameEl) &&
                    string.Equals(nameEl.GetString(), AssetName, StringComparison.OrdinalIgnoreCase) &&
                    asset.TryGetProperty("browser_download_url", out var dlEl))
                {
                    var dl = dlEl.GetString();
                    if (string.IsNullOrWhiteSpace(dl)) return null;
                    long size = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;
                    return new UpdateInfo(version, tag!, dl, size);
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True if the release version is strictly newer than the running build.</summary>
    public static bool IsUpdateAvailable(UpdateInfo info) => info.Version > CurrentVersion;

    /// <summary>
    /// Downloads the release zip to a temp folder (reporting 0–100 progress), extracts it, and
    /// returns the full path to the extracted <c>ProgramClock.exe</c>.
    /// </summary>
    public static async Task<string> DownloadAsync(
        UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ProgramClock_update");
        // Start from a clean staging directory.
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        var zipPath = Path.Combine(dir, "update.zip");

        using (var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                   .ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? info.Size;

            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                readTotal += read;
                if (total > 0)
                    progress?.Report(Math.Min(100.0, readTotal * 100.0 / total));
            }
            progress?.Report(100.0);
        }

        var extractDir = Path.Combine(dir, "extracted");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        var exe = Directory.EnumerateFiles(extractDir, "ProgramClock.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (exe is null)
            throw new FileNotFoundException("The downloaded update did not contain ProgramClock.exe.");

        return exe;
    }

    /// <summary>
    /// Spawns a detached batch script that waits for this process to exit, copies the new exe over
    /// the current one, and relaunches it. The caller should shut the app down immediately after.
    /// </summary>
    public static void ApplyAndRestart(string newExePath)
    {
        var currentExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the current executable path.");
        int pid = Environment.ProcessId;

        var dir = Path.GetDirectoryName(newExePath)!;
        var batPath = Path.Combine(dir, "apply_update.bat");

        // Wait for our PID to disappear, then overwrite + relaunch. The script deletes itself last.
        var bat = new StringBuilder();
        bat.AppendLine("@echo off");
        bat.AppendLine(":waitloop");
        bat.AppendLine($"tasklist /fi \"PID eq {pid}\" | findstr /i ProgramClock >nul");
        bat.AppendLine("if not errorlevel 1 (");
        bat.AppendLine("  timeout /t 1 /nobreak >nul");
        bat.AppendLine("  goto waitloop");
        bat.AppendLine(")");
        bat.AppendLine("timeout /t 1 /nobreak >nul");
        bat.AppendLine($"copy /y \"{newExePath}\" \"{currentExe}\" >nul");
        bat.AppendLine($"start \"\" \"{currentExe}\"");
        bat.AppendLine("del \"%~f0\"");

        File.WriteAllText(batPath, bat.ToString());

        var psi = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(batPath);
        Process.Start(psi);
    }

    /// <summary>Parses a release tag like "v1.2.3" or "1.2.3" into a <see cref="Version"/>.</summary>
    private static bool TryParseVersion(string tag, out Version version)
    {
        var trimmed = tag.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[1..];
        return Version.TryParse(trimmed, out version!);
    }
}
