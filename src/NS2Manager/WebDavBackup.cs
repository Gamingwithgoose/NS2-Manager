using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace NS2Manager;

/// <summary>Read-only WebDAV downloader. Never sends PUT, DELETE, MOVE, COPY, MKCOL or PROPPATCH.</summary>
internal sealed class WebDavBackup
{
    private readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Uri root;
    private readonly Action<string> log;
    private readonly HashSet<string> visited = new(StringComparer.Ordinal);
    private readonly List<BackupEntry> entries = [];
    private long downloaded;
        private const int MaxFiles = 500_000;

    private sealed record RemoteEntry(Uri Uri, bool Directory, long? Size);
    private sealed record BackupEntry(string Path, long Bytes, string Sha256);

    public WebDavBackup(string consoleIp, int port, Action<string> logger)
    {
        if (!IPAddress.TryParse(consoleIp, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("Invalid console IPv4 address.");
        if (port is < 1 or > 65535) throw new ArgumentException("Invalid port.");
        root = new Uri($"http://{ip}:{port}/");
        log = logger;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NS2Manager/0.1");
    }

    public async Task DownloadAsync(string folder, CancellationToken cancellationToken)
    {
        var dest = Path.GetFullPath(folder);
        Directory.CreateDirectory(dest);
        var manifest = Path.Combine(dest, "ns2manager-backup-manifest.json");
        if (File.Exists(manifest)) throw new IOException("Manifest already exists at destination. Choose a new empty folder.");
        if (Directory.EnumerateFileSystemEntries(dest).Any())
            throw new IOException("Destination folder must be empty to prevent overwrites.");
        try
        {
            log($"Reading WebDAV index at {root}...");
            await WalkAsync(root, dest, 0, cancellationToken);
            var journal = new { source = root.ToString(), finishedUtc = DateTimeOffset.UtcNow, files = entries, downloadedBytes = downloaded };
            await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            log($"Backup complete: {entries.Count} files; {downloaded:N0} bytes; manifest: {manifest}");
        }
        finally { http.Dispose(); }
    }

    private async Task WalkAsync(Uri directory, string destination, int depth, CancellationToken ct)
    {
        if (depth > 64) throw new IOException("Directory depth limit reached.");
        if (!visited.Add(directory.AbsoluteUri)) return;
        var children = await ListAsync(directory, ct);
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            if (entries.Count >= MaxFiles) throw new IOException("File count limit reached.");
            string rel = Uri.UnescapeDataString(root.MakeRelativeUri(child.Uri).ToString()).Replace('/', Path.DirectorySeparatorChar);
            if (rel.EndsWith(Path.DirectorySeparatorChar)) rel = rel.TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(rel) || rel.Split(Path.DirectorySeparatorChar).Any(p => p is "." or ".." or ""))
                throw new IOException("Unsafe WebDAV path: " + child.Uri);
            var path = Path.GetFullPath(Path.Combine(destination, rel));
            if (!path.StartsWith(destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Remote path escapes backup destination.");
            if (child.Directory)
            {
                Directory.CreateDirectory(path);
                await WalkAsync(child.Uri, destination, depth + 1, ct);
            }
            else
            {
                if (File.Exists(path)) throw new IOException("Duplicate file: " + rel);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await DownloadFileAsync(child, path, rel, ct);
            }
        }
    }

    private async Task<List<RemoteEntry>> ListAsync(Uri directory, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), directory);
        request.Headers.TryAddWithoutValidation("Depth", "1");
        request.Content = new StringContent("<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/><d:getcontentlength/></d:prop></d:propfind>");
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != (HttpStatusCode)207)
            throw new IOException($"WebDAV PROPFIND unsupported or failed: HTTP {(int)response.StatusCode}. No files were written for this directory.");
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
        XNamespace dav = "DAV:";
        List<RemoteEntry> result = [];
        foreach (var item in doc.Descendants(dav + "response"))
        {
            var href = item.Element(dav + "href")?.Value;
            if (string.IsNullOrEmpty(href)) continue;
            var uri = new Uri(directory, href);
            if (uri.Scheme != root.Scheme || uri.Host != root.Host || uri.Port != root.Port || !root.IsBaseOf(uri))
                throw new IOException("Cross-origin or out-of-root WebDAV link rejected.");
            if (uri.AbsolutePath.TrimEnd('/') == directory.AbsolutePath.TrimEnd('/')) continue;
            // Only immediate children; reject unexpectedly deep listings.
            var relative = directory.MakeRelativeUri(uri).ToString().TrimEnd('/');
            if (relative.Contains('/') || relative.Contains("%2f", StringComparison.OrdinalIgnoreCase)) continue;
            var okProp = item.Descendants(dav + "propstat").FirstOrDefault(p => p.Element(dav + "status")?.Value.Contains(" 200 ") == true);
            var props = okProp?.Element(dav + "prop");
            if (props == null) continue;
            bool isDir = props.Element(dav + "resourcetype")?.Element(dav + "collection") != null;
            long? size = long.TryParse(props.Element(dav + "getcontentlength")?.Value, out var n) ? n : null;
            result.Add(new RemoteEntry(uri, isDir, size));
        }
        return result;
    }

    private async Task DownloadFileAsync(RemoteEntry remote, string localPath, string relative, CancellationToken ct)
    {
        var temp = localPath + ".partial";
        using var response = await http.GetAsync(remote.Uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        long count = 0;
        await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
        await using (var stream = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[131072];
            int n;
            while ((n = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, n), ct);
                count += n; downloaded += n;
            }
        }
        if (remote.Size is long expected && count != expected)
            throw new IOException($"Size mismatch for {relative}: got {count}, expected {expected}");
        await using var hashInput = File.OpenRead(temp);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(hashInput, ct)).ToLowerInvariant();
        File.Move(temp, localPath);
        entries.Add(new BackupEntry(relative.Replace('\\', '/'), count, sha));
        log($"{entries.Count} files • {downloaded:N0} bytes • {relative}");
    }
}
