using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NS2Manager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class Settings
{
    public string HostIp { get; set; } = "192.168.1.100";
    public string ConsoleIp { get; set; } = "192.168.1.138";
    public string RunDirectory { get; set; } = "";
    public string TitleId { get; set; } = "";
}

internal sealed class Dependency
{
    public required string Name { get; init; }
    public required string Owner { get; init; }
    public required string Repo { get; init; }
    public string? InstalledSha { get; set; }
    public string? LatestSha { get; set; }
    public string Root => Path.Combine(AppPaths.Data, "tools", Name);
    public string RevisionFile => Path.Combine(Root, ".ns2manager-revision");
    public override string ToString() => $"{Name}: installed {Short(InstalledSha)} / latest {Short(LatestSha)}";
    private static string Short(string? sha) => sha is null ? "(unknown)" : sha[..Math.Min(10, sha.Length)];
}

internal static class AppPaths
{
    public static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NS2Manager");
    public static readonly string Settings = Path.Combine(Data, "settings.json");
}

internal sealed class MainForm : Form
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly Dependency[] deps = [
        new() { Name = "ns2jb", Owner = "Cellenseres", Repo = "ns2jb" },
        new() { Name = "OunceCake", Owner = "ariankordi", Repo = "OunceCake" }
    ];
    private readonly TextBox host = new() { Width = 150 };
    private readonly TextBox console = new() { Width = 150 };
    private readonly TextBox run = new() { Width = 525 };
    private readonly TextBox title = new() { Width = 175, MaxLength = 16 };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new("Consolas", 9.5f) };
    private readonly ListBox versions = new() { Width = 620, Height = 90 };
    private readonly Button check = new() { Text = "Check for updates", AutoSize = true };
    private readonly Button install = new() { Text = "Install selected update", AutoSize = true };
    private readonly Button start = new() { Text = "Start ns2jb (RPC)", AutoSize = true };
    private readonly Button cake = new() { Text = "Start OunceCake", AutoSize = true };
    private readonly Button stop = new() { Text = "Stop host processes", AutoSize = true };
    private readonly Button findRun = new() { Text = "Find latest run", AutoSize = true };
    private readonly Button backup = new() { Text = "Backup title files…", AutoSize = true };
    private readonly Button cancelBackup = new() { Text = "Cancel backup", AutoSize = true, Enabled = false };
    private CancellationTokenSource? backupCancellation;
    private readonly Label status = new() { AutoSize = true, Text = "Idle" };
    private readonly List<Process> processes = [];
    private Settings settings = new();
    private bool busy;

    public MainForm()
    {
        Text = "NS2 Manager - Windows host prototype";
        ClientSize = new Size(850, 680);
        MinimumSize = new Size(730, 560);
        Font = new Font("Segoe UI", 10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NS2Manager/0.1 (+https://github.com/Gamingwithgoose/NS2-Manager)");
        Directory.CreateDirectory(AppPaths.Data);
        try { settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.Settings)) ?? new(); }
        catch { /* keep defaults */ }
        host.Text = settings.HostIp; console.Text = settings.ConsoleIp;
        run.Text = settings.RunDirectory; title.Text = settings.TitleId;

        var page = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(12), AutoScroll = true };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(page);

        var network = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, WrapContents = true };
        network.Controls.AddRange([new Label { Text = "PC LAN IP:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, host,
            new Label { Text = "Switch 2 IP:", AutoSize = true, Padding = new Padding(8, 7, 0, 0) }, console,
            new Label { Text = "Node.js 22+ required", AutoSize = true, Padding = new Padding(10, 7, 0, 0) }]);
        page.Controls.Add(network, 0, 0);

        var updates = new GroupBox { Text = "Dependencies / Updates (manual approval only)", Height = 170, Dock = DockStyle.Top };
        var depPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8), AutoScroll = true };
        var depButtons = new FlowLayoutPanel { Width = 760, Height = 40 };
        depButtons.Controls.AddRange([check, install]);
        depPanel.Controls.Add(versions); depPanel.Controls.Add(depButtons); updates.Controls.Add(depPanel);
        page.Controls.Add(updates, 0, 1);

        var session = new GroupBox { Text = "Exploit host and content server", Height = 183, Dock = DockStyle.Top };
        var sessionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };
        var runRow = new FlowLayoutPanel { Width = 790, Height = 37 };
        runRow.Controls.AddRange([new Label { Text = "Run:", Width = 38, Padding = new Padding(0, 7, 0, 0) }, run, findRun]);
        var titleRow = new FlowLayoutPanel { Width = 790, Height = 36 };
        titleRow.Controls.AddRange([new Label { Text = "Title ID:", Width = 75, Padding = new Padding(0, 7, 0, 0) }, title,
            new Label { Text = "16 hexadecimal digits; no automatic installed-title discovery yet", AutoSize = true, Padding = new Padding(5, 7, 0, 0) }]);
        var actions = new FlowLayoutPanel { Width = 790, Height = 40 };
        actions.Controls.AddRange([start, cake, backup, cancelBackup, stop, status]);
        sessionPanel.Controls.AddRange([runRow, titleRow, actions]); session.Controls.Add(sessionPanel);
        page.Controls.Add(session, 0, 2);

        var logs = new GroupBox { Text = "Live output", Dock = DockStyle.Fill, Padding = new Padding(8) };
        logs.Controls.Add(log); page.Controls.Add(logs, 0, 3);

        foreach (var d in deps) { d.InstalledSha = File.Exists(d.RevisionFile) ? File.ReadAllText(d.RevisionFile).Trim() : null; versions.Items.Add(d); }
        versions.SelectedIndex = 0;
        check.Click += async (_, _) => await CheckUpdatesAsync();
        install.Click += async (_, _) => await InstallSelectedAsync();
        start.Click += async (_, _) => await StartHostAsync();
        cake.Click += async (_, _) => await StartCakeAsync();
        backup.Click += async (_, _) => await BackupAsync();
        cancelBackup.Click += (_, _) => backupCancellation?.Cancel();
        stop.Click += (_, _) => StopProcesses();
        findRun.Click += (_, _) => FindLatestRun();
        FormClosing += (_, _) => { backupCancellation?.Cancel(); SaveSettings(); StopProcesses(); http.Dispose(); };
        WriteLog("This GUI is a host-side prototype. No automatic console writes or update installs.");
    }

    private void WriteLog(string s)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => WriteLog(s)); return; }
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}");
    }
    private void SetBusy(bool value)
    {
        busy = value;
        check.Enabled = install.Enabled = !busy;
        status.Text = busy ? "Working…" : "Idle";
    }
    private void SaveSettings()
    {
        settings.HostIp = host.Text.Trim(); settings.ConsoleIp = console.Text.Trim();
        settings.RunDirectory = run.Text.Trim(); settings.TitleId = title.Text.Trim();
        File.WriteAllText(AppPaths.Settings, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static string ValidateIp(string text)
    {
        if (!IPAddress.TryParse(text.Trim(), out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any)) throw new InvalidOperationException("Enter a valid non-loopback IPv4 address.");
        return ip.ToString();
    }
    private void RefreshVersions()
    {
        var selected = versions.SelectedIndex;
        versions.Items.Clear(); foreach (var d in deps) versions.Items.Add(d);
        versions.SelectedIndex = Math.Clamp(selected, 0, deps.Length - 1);
    }
    private async Task CheckUpdatesAsync()
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            foreach (var d in deps)
            {
                using var response = await http.GetAsync($"https://api.github.com/repos/{d.Owner}/{d.Repo}/commits?per_page=1");
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                d.LatestSha = document.RootElement[0].GetProperty("sha").GetString();
                WriteLog($"{d.Name}: installed {d.InstalledSha ?? "none"}, upstream {d.LatestSha}");
            }
            RefreshVersions();
        }
        catch (Exception ex) { WriteLog("Update check failed: " + ex.Message); }
        finally { SetBusy(false); }
    }
    private async Task InstallSelectedAsync()
    {
        if (busy || versions.SelectedItem is not Dependency d) return;
        if (processes.Any(p => !p.HasExited)) { MessageBox.Show("Stop running host processes before updating."); return; }
        if (string.IsNullOrWhiteSpace(d.LatestSha)) { MessageBox.Show("Check for updates first."); return; }
        if (d.LatestSha == d.InstalledSha) { WriteLog($"{d.Name} is already at the selected revision."); return; }
        var confirmation = MessageBox.Show($"Install {d.Owner}/{d.Repo} at commit {d.LatestSha}?\n\nUpstream code is experimental. Review its changes before running. Previous installation will be backed up.",
            "Approve dependency update", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirmation != DialogResult.Yes) return;
        SetBusy(true);
        string? stage = null;
        try
        {
            var sha = d.LatestSha;
            var archiveUrl = $"https://api.github.com/repos/{d.Owner}/{d.Repo}/zipball/{sha}";
            stage = Path.Combine(AppPaths.Data, "stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            string zipPath = Path.Combine(stage, "download.zip");
            using (var response = await http.GetAsync(archiveUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 150 * 1024 * 1024)
                    throw new IOException("Unexpectedly large dependency archive.");
                await using var file = File.Create(zipPath);
                await using var stream = await response.Content.ReadAsStreamAsync();
                await stream.CopyToAsync(file);
            }
            var extracted = Path.Combine(stage, "extracted"); Directory.CreateDirectory(extracted);
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                long expanded = 0;
                foreach (var entry in zip.Entries)
                {
                    expanded += entry.Length;
                    if (expanded > 600L * 1024 * 1024) throw new IOException("Expanded archive is too large.");
                    var path = entry.FullName.Replace('\\', '/');
                    var split = path.IndexOf('/');
                    if (split < 0) continue;
                    var relative = path[(split + 1)..];
                    if (string.IsNullOrEmpty(relative)) continue;
                    if (relative.StartsWith('/') || relative.Split('/').Any(x => x == ".." || x == "."))
                        throw new IOException("Unsafe archive path.");
                    var target = Path.GetFullPath(Path.Combine(extracted, relative));
                    if (!target.StartsWith(Path.GetFullPath(extracted) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Archive path escapes destination.");
                    if (path.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                    // ZIP external attributes may contain Unix symlinks; refuse them.
                    if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Symlink not allowed.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: false);
                }
            }
            var required = d.Name == "ns2jb" ? "run.cjs" : "ouncecake.cjs";
            if (!File.Exists(Path.Combine(extracted, required))) throw new IOException($"Downloaded source lacks {required}.");
            var targetDir = d.Root; var rollback = targetDir + ".previous";
            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
            if (Directory.Exists(rollback)) Directory.Delete(rollback, true);
            if (Directory.Exists(targetDir)) Directory.Move(targetDir, rollback);
            try
            {
                Directory.Move(extracted, targetDir);
                File.WriteAllText(d.RevisionFile, sha!);
            }
            catch
            {
                if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                if (Directory.Exists(rollback)) Directory.Move(rollback, targetDir);
                throw;
            }
            d.InstalledSha = sha;
            WriteLog($"{d.Name} installed at {sha}. Previous version retained in {rollback}");
            RefreshVersions();
        }
        catch (Exception ex) { WriteLog("Install failed (existing installation preserved when possible): " + ex.Message); }
        finally { if (stage != null) try { Directory.Delete(stage, true); } catch { } SetBusy(false); }
    }
    private async Task BackupAsync()
    {
        if (busy) return;
        try
        {
            var ip = ValidateIp(console.Text);
            if (!processes.Any(p => !p.HasExited)) throw new InvalidOperationException("Start the live OunceCake server first.");
            using var picker = new FolderBrowserDialog { Description = "Choose an EMPTY folder for the extracted title-file backup", UseDescriptionForTitle = true, ShowNewFolderButton = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            SetBusy(true);
            backupCancellation = new CancellationTokenSource();
            cancelBackup.Enabled = true;
            SaveSettings();
            await new WebDavBackup(ip, 8080, WriteLog).DownloadAsync(picker.SelectedPath, backupCancellation.Token);
        }
        catch (OperationCanceledException) { WriteLog("Backup cancelled; incomplete files may remain with .partial extension. Destination was not marked complete."); }
        catch (Exception ex) { WriteLog("Backup stopped: " + ex.Message); }
        finally { cancelBackup.Enabled = false; backupCancellation?.Dispose(); backupCancellation = null; SetBusy(false); }
    }
    private async Task StartHostAsync()
    {
        try
        {
            SaveSettings();
            var pc = ValidateIp(host.Text);
            var folder = deps[0].Root;
            if (!File.Exists(Path.Combine(folder, "run.cjs"))) throw new FileNotFoundException("Install ns2jb under Dependencies first.");
            await StartProcessAsync("node", ["run.cjs", "--rpc", pc], folder);
            WriteLog("Runner launched. Continue console-side steps using the matching bootstrap. A launched process does not imply a successful exploit.");
        }
        catch (Exception ex) { WriteLog("Cannot start ns2jb: " + ex.Message); }
    }
    private async Task StartCakeAsync()
    {
        try
        {
            SaveSettings();
            var folder = deps[1].Root;
            var id = title.Text.Trim().ToUpperInvariant();
            if (!Regex.IsMatch(id, @"\A[0-9A-F]{16}\z")) throw new InvalidOperationException("Title ID must be exactly 16 hex digits.");
            if (!File.Exists(Path.Combine(folder, "ouncecake.cjs"))) throw new FileNotFoundException("Install OunceCake under Dependencies first.");
            var dir = Path.GetFullPath(run.Text.Trim());
            if (!File.Exists(Path.Combine(dir, "switch2", "current-acquisition.json")) ||
                !File.Exists(Path.Combine(dir, "switch2", "ro", "root", "current-root.json")))
                throw new InvalidOperationException("Choose the current live, completed RPC run directory (missing expected session files).");
            if (!processes.Any(p => !p.HasExited)) throw new InvalidOperationException("Start a live ns2jb RPC session in this manager first.");
            await StartProcessAsync("node", ["ouncecake.cjs", dir, id, "--port", "8080"], folder);
            WriteLog($"OunceCake starting for {id}. Use http://{ValidateIp(console.Text)}:8080/ after server readiness is confirmed in logs.");
        }
        catch (Exception ex) { WriteLog("Cannot start OunceCake: " + ex.Message); }
    }
    private Task StartProcessAsync(string executable, IEnumerable<string> args, string workingDirectory)
    {
        var psi = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) WriteLog(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) WriteLog("ERR: " + e.Data); };
        p.Exited += (_, _) => { try { WriteLog($"Process exited (PID {p.Id}, code {p.ExitCode})."); } catch { } };
        if (!p.Start()) throw new IOException("Unable to start process.");
        processes.Add(p);
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        WriteLog($"Started {Path.GetFileName(executable)} (PID {p.Id}) from {workingDirectory}.");
        return Task.CompletedTask;
    }
    private void FindLatestRun()
    {
        var root = Path.Combine(deps[0].Root, "runs");
        if (!Directory.Exists(root)) { WriteLog("No runs directory found."); return; }
        var dirs = Directory.GetDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "switch2", "current-acquisition.json")))
            .OrderByDescending(Directory.GetLastWriteTimeUtc).ToArray();
        if (dirs.Length == 0) { WriteLog("No completed acquisition metadata was found."); return; }
        run.Text = dirs[0];
        WriteLog("Found candidate run: " + dirs[0] + ". Verify that this is the active session before using OunceCake.");
    }
    private void StopProcesses()
    {
        foreach (var p in processes.ToArray())
        {
            try { if (!p.HasExited) { p.Kill(entireProcessTree: true); p.WaitForExit(1500); } }
            catch (Exception ex) { WriteLog("Stop warning: " + ex.Message); }
            finally { p.Dispose(); }
        }
        processes.Clear();
        WriteLog("Stopped manager-launched processes. This does not restore console state.");
    }
}
