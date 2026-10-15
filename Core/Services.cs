using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace OneFlowHub.Core;

// ───────────────────────── settings ─────────────────────────
public sealed class AppSettings
{
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool CheckUpdatesOnStart { get; set; } = true;
    public string CatalogUrl { get; set; } =
        "https://raw.githubusercontent.com/KhubuliaVato/tvflow-hub/main/catalog.json";
    // where the hub looks for its OWN updates
    public string HubOwner { get; set; } = "KhubuliaVato";
    public string HubRepo { get; set; } = "1flow-hub";
    public string HubAsset { get; set; } = "1FlowHub.zip";

    public static AppSettings Load() => Json.TryReadFile<AppSettings>(Paths.SettingsFile) ?? new AppSettings();
    public void Save() => Json.TryWriteFile(Paths.SettingsFile, this);

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void ApplyAutostart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (enable)
            {
                var exe = Environment.ProcessPath ?? "";
                key.SetValue("1FlowHub", $"\"{exe}\" --tray");
            }
            else key.DeleteValue("1FlowHub", throwOnMissingValue: false);
        }
        catch (Exception ex) { Log.Error(ex); }
    }
}

// ───────────────────────── GitHub ─────────────────────────
public sealed class AssetInfo
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Url { get; set; } = "";
}

public sealed class ReleaseInfo
{
    public string Tag { get; set; } = "";
    public string Name { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset Published { get; set; }
    public string Url { get; set; } = "";
    public List<AssetInfo> Assets { get; set; } = new();
    [JsonIgnore] public string Version => VersionUtil.Clean(Tag);
}

public static class GitHub
{
    public static readonly HttpClient Http = CreateClient();

    /// <summary>true after a successful GitHub call, false after a failed one (cache was used).</summary>
    public static bool Online { get; private set; } = true;
    public static DateTime? LastSync { get; private set; }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("1FlowHub/2.0");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    private sealed class CacheFile
    {
        public DateTimeOffset At { get; set; }
        public List<ReleaseInfo> Releases { get; set; } = new();
    }

    /// <summary>Latest releases (newest first). Falls back to the disk cache when offline; null if nothing is known.</summary>
    public static async Task<List<ReleaseInfo>?> GetReleasesAsync(string owner, string repo, int count = 5, bool force = false)
    {
        var cachePath = Path.Combine(Paths.Cache, $"rel_{owner}_{repo}.json");
        var cached = Json.TryReadFile<CacheFile>(cachePath);
        if (!force && cached != null && DateTimeOffset.Now - cached.At < TimeSpan.FromMinutes(10))
        {
            var at = cached.At.LocalDateTime;
            if (LastSync == null || at > LastSync) LastSync = at;
            return cached.Releases;
        }
        try
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{owner}/{repo}/releases?per_page={count}");
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var list = new List<ReleaseInfo>();
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.GetProperty("draft").GetBoolean() || r.GetProperty("prerelease").GetBoolean()) continue;
                var rel = new ReleaseInfo
                {
                    Tag = r.GetProperty("tag_name").GetString() ?? "",
                    Name = r.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Body = r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                    Url = r.GetProperty("html_url").GetString() ?? "",
                    Published = r.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
                        ? p.GetDateTimeOffset() : DateTimeOffset.Now,
                };
                foreach (var a in r.GetProperty("assets").EnumerateArray())
                    rel.Assets.Add(new AssetInfo
                    {
                        Name = a.GetProperty("name").GetString() ?? "",
                        Size = a.GetProperty("size").GetInt64(),
                        Url = a.GetProperty("browser_download_url").GetString() ?? "",
                    });
                list.Add(rel);
            }
            Json.TryWriteFile(cachePath, new CacheFile { At = DateTimeOffset.Now, Releases = list });
            Online = true; LastSync = DateTime.Now;
            return list;
        }
        catch (Exception ex)
        {
            Log.Info($"releases {owner}/{repo}: {ex.Message}");
            Online = false;
            return cached?.Releases;
        }
    }

    public static async Task DownloadAsync(string url, string dest, IProgress<double>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("1FlowHub/2.0");
        using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buf = new byte[81920];
        long done = 0; int read;
        while ((read = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read), ct);
            done += read;
            if (total > 0) progress?.Report(done * 100.0 / total);
        }
        progress?.Report(100);
    }
}

// ───────────────────────── catalog ─────────────────────────
public sealed class CatalogEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? IconText { get; set; }
    public string? IconUrl { get; set; }
    public string Description { get; set; } = "";
    public string RepoOwner { get; set; } = "";
    public string RepoName { get; set; } = "";
    public string? AssetMatch { get; set; }
    public string? ExecutableName { get; set; }
    public string? Category { get; set; }
    /// <summary>"ready" (default) | "beta" (test mode) | "soon" (not released yet, download disabled)</summary>
    public string? Status { get; set; }
}

public static class Catalog
{
    public static async Task<List<CatalogEntry>> LoadAsync(string url)
    {
        var cachePath = Path.Combine(Paths.Cache, "catalog.json");
        try
        {
            var text = File.Exists(url) ? await File.ReadAllTextAsync(url) : await GitHub.Http.GetStringAsync(url);
            var list = JsonSerializer.Deserialize<List<CatalogEntry>>(text, Json.Options);
            if (list is { Count: > 0 })
            {
                File.WriteAllText(cachePath, text);
                return list;
            }
        }
        catch (Exception ex) { Log.Info("catalog: " + ex.Message); }

        var cached = Json.TryReadFile<List<CatalogEntry>>(cachePath);
        if (cached is { Count: > 0 }) return cached;

        using var s = typeof(Catalog).Assembly.GetManifestResourceStream("catalog.json");
        if (s != null)
        {
            var embedded = JsonSerializer.Deserialize<List<CatalogEntry>>(s, Json.Options);
            if (embedded != null) return embedded;
        }
        return new List<CatalogEntry>();
    }
}

// ───────────────────────── installer ─────────────────────────
public sealed class InstallRecord
{
    public string Tag { get; set; } = "";
    public DateTimeOffset InstalledAt { get; set; }
}

public static class Installer
{
    public static string DirOf(string id) => Path.Combine(Paths.Apps, id);
    private static string RecordPath(string id) => Path.Combine(DirOf(id), "install.json");

    public static InstallRecord? GetInstalled(string id) => Json.TryReadFile<InstallRecord>(RecordPath(id));

    public static AssetInfo? PickAsset(ReleaseInfo rel, string? match)
    {
        if (rel.Assets.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(match))
        {
            var exact = rel.Assets.FirstOrDefault(a => a.Name.Equals(match, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            var part = rel.Assets.FirstOrDefault(a => a.Name.Contains(match, StringComparison.OrdinalIgnoreCase));
            if (part != null) return part;
        }
        return rel.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task InstallAsync(string id, ReleaseInfo rel, string? assetMatch,
        IProgress<(double pct, string text)> progress, CancellationToken ct = default)
    {
        var asset = PickAsset(rel, assetMatch) ?? throw new InvalidOperationException("release-ში ფაილი ვერ მოიძებნა");
        var zip = Path.Combine(Paths.Temp, $"{id}-{rel.Tag}.zip");
        var target = DirOf(id);
        Directory.CreateDirectory(target);

        var dl = new Progress<double>(p => progress.Report((p * 0.85, $"იტვირთება {p:0}%")));
        await GitHub.DownloadAsync(asset.Url, zip, dl, ct);

        progress.Report((88, "ინსტალაცია…"));
        await Task.Run(() => ExtractFlatten(zip, target), ct);

        Json.TryWriteFile(RecordPath(id), new InstallRecord { Tag = rel.Tag, InstalledAt = DateTimeOffset.Now });
        try { File.Delete(zip); } catch { }
        progress.Report((100, "მზადაა"));
    }

    /// <summary>Extract zip into target (overwriting), stripping a single top-level folder if there is one.</summary>
    internal static void ExtractFlatten(string zipPath, string target)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var files = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        string? root = null;
        var tops = files.Select(e => e.FullName.Replace('\\', '/').Split('/')[0]).Distinct().ToList();
        if (tops.Count == 1 && files.All(e => e.FullName.Replace('\\', '/').Contains('/'))) root = tops[0] + "/";

        var full = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        foreach (var e in files)
        {
            var rel = e.FullName.Replace('\\', '/');
            if (root != null && rel.StartsWith(root)) rel = rel[root.Length..];
            var dest = Path.GetFullPath(Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip guard
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, overwrite: true);
        }
    }

    public static string? FindExe(string id, string? exeName)
    {
        var dir = DirOf(id);
        if (!Directory.Exists(dir)) return null;
        if (!string.IsNullOrWhiteSpace(exeName))
        {
            var hit = Directory.EnumerateFiles(dir, exeName, SearchOption.AllDirectories).FirstOrDefault();
            if (hit != null) return hit;
        }
        return Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    public static bool Launch(string id, string? exeName)
    {
        var exe = FindExe(id, exeName);
        if (exe == null) return false;
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = true });
        return true;
    }

    public static void Uninstall(string id)
    {
        var dir = DirOf(id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}

// ───────────────────────── hub self-update ─────────────────────────
public static class SelfUpdater
{
    public static string CurrentVersion =>
        VersionUtil.Clean(typeof(SelfUpdater).Assembly.GetName().Version?.ToString(3));

    public static async Task<(ReleaseInfo rel, AssetInfo asset)?> CheckAsync(AppSettings s, bool force = false)
    {
        var rels = await GitHub.GetReleasesAsync(s.HubOwner, s.HubRepo, 5, force);
        if (rels == null) return null;
        foreach (var r in rels)
        {
            var asset = r.Assets.FirstOrDefault(a => a.Name.Equals(s.HubAsset, StringComparison.OrdinalIgnoreCase));
            if (asset == null) continue;
            return VersionUtil.IsNewer(r.Version, CurrentVersion) ? (r, asset) : null;
        }
        return null;
    }

    /// <summary>Download the new build, then hand over to a tiny cmd script that swaps files after we exit.</summary>
    public static async Task ApplyAsync(ReleaseInfo rel, AssetInfo asset, IProgress<double> progress)
    {
        var zip = Path.Combine(Paths.Temp, "hub-update.zip");
        var stage = Path.Combine(Paths.Temp, "hub-stage");
        if (Directory.Exists(stage)) Directory.Delete(stage, true);

        await GitHub.DownloadAsync(asset.Url, zip, progress);
        await Task.Run(() => Installer.ExtractFlatten(zip, stage));

        var exe = Environment.ProcessPath!;
        var dir = Path.GetDirectoryName(exe)!;
        var pid = Environment.ProcessId;
        var cmd = Path.Combine(Paths.Temp, "hub-update.cmd");
        File.WriteAllText(cmd, $"""
            @echo off
            :wait
            tasklist /fi "PID eq {pid}" 2>nul | find "{pid}" >nul && (timeout /t 1 /nobreak >nul & goto wait)
            xcopy "{stage}\*" "{dir}\" /E /Y /Q >nul
            start "" "{exe}"
            del "%~f0"
            """);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }
}
