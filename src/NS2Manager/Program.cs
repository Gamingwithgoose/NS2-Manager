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
    public string RunnerMode { get; set; } = "WSL (recommended)";
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
    private readonly ComboBox runnerMode = new() { Width = 175, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new("Consolas", 9.5f) };
    private readonly ListBox versions = new() { Width = 620, Height = 90 };
    private readonly Button check = new() { Text = "Check for updates", AutoSize = true };
    private readonly Button install = new() { Text = "Install selected update", AutoSize = true };
    private readonly Button start = new() { Text = "Start ns2jb (RPC)", AutoSize = true };
    private readonly Button cake = new() { Text = "List games (OunceCake)", AutoSize = true };
    private readonly Button chooseGame = new() { Text = "Open selected game", AutoSize = true, Enabled = false };
    private readonly ListBox games = new() { Width = 560, Height = 100, HorizontalScrollbar = true };
    private readonly List<(int Index, string Name, string Id)> discoveredGames = [];
    private Process? ounceProcess;
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
        runnerMode.Items.AddRange(["WSL (recommended)", "Windows (experimental)"]);
        runnerMode.SelectedItem = runnerMode.Items.Contains(settings.RunnerMode) ? settings.RunnerMode : "WSL (recommended)";

        var page = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(12), AutoScroll = true };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(page);

        var network = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, WrapContents = true };
        network.Controls.AddRange([new Label { Text = "PC LAN IP:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, host,
            new Label { Text = "Switch 2 IP:", AutoSize = true, Padding = new Padding(8, 7, 0, 0) }, console,
            new Label { Text = "Runner:", AutoSize = true, Padding = new Padding(10, 7, 0, 0) }, runnerMode]);
        page.Controls.Add(network, 0, 0);

        var updates = new GroupBox { Text = "Dependencies / Updates (manual approval only)", Height = 170, Dock = DockStyle.Top };
        var depPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8), AutoScroll = true };
        var depButtons = new FlowLayoutPanel { Width = 760, Height = 40 };
        depButtons.Controls.AddRange([check, install]);
        depPanel.Controls.Add(versions); depPanel.Controls.Add(depButtons); updates.Controls.Add(depPanel);
        page.Controls.Add(updates, 0, 1);

        var session = new GroupBox { Text = "Exploit host and content server", Height = 300, Dock = DockStyle.Top };
        var sessionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };
        var runRow = new FlowLayoutPanel { Width = 790, Height = 37 };
        runRow.Controls.AddRange([new Label { Text = "Run:", Width = 38, Padding = new Padding(0, 7, 0, 0) }, run, findRun]);
        var titleRow = new FlowLayoutPanel { Width = 790, Height = 36 };
        titleRow.Controls.AddRange([new Label { Text = "Title ID:", Width = 75, Padding = new Padding(0, 7, 0, 0) }, title,
            new Label { Text = "Optional; leave blank to browse installed games", AutoSize = true, Padding = new Padding(5, 7, 0, 0) }]);
        var actions = new FlowLayoutPanel { Width = 790, Height = 40 };
        actions.Controls.AddRange([start, cake, chooseGame, backup, cancelBackup, stop, status]);
        sessionPanel.Controls.AddRange([runRow, titleRow, actions, games]); session.Controls.Add(sessionPanel);
        page.Controls.Add(session, 0, 2);

        var logs = new GroupBox { Text = "Live output", Dock = DockStyle.Fill, Padding = new Padding(8) };
        logs.Controls.Add(log); page.Controls.Add(logs, 0, 3);

        foreach (var d in deps) { d.InstalledSha = File.Exists(d.RevisionFile) ? File.ReadAllText(d.RevisionFile).Trim() : null; versions.Items.Add(d); }
        versions.SelectedIndex = 0;
        check.Click += async (_, _) => await CheckUpdatesAsync();
        install.Click += async (_, _) => await InstallSelectedAsync();
        start.Click += async (_, _) => await StartHostAsync();
        cake.Click += async (_, _) => await StartCakeAsync();
        chooseGame.Click += async (_, _) => await SelectGameAsync();
        games.DoubleClick += async (_, _) => await SelectGameAsync();
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
        settings.RunDirectory = run.Text.Trim(); settings.TitleId = title.Text.Trim(); settings.RunnerMode = runnerMode.SelectedItem?.ToString() ?? "WSL (recommended)";
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
            if (runnerMode.SelectedItem?.ToString() == "WSL (recommended)")
            {
                // WSL --cd accepts Windows paths on current WSL; no shell interpolation.
                var probe = new ProcessStartInfo("wsl.exe") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                const string wslNodeInit = "if [ -s \"$HOME/.nvm/nvm.sh\" ]; then . \"$HOME/.nvm/nvm.sh\"; fi; ";
                probe.ArgumentList.Add("--");
                probe.ArgumentList.Add("bash");
                probe.ArgumentList.Add("-lc");
                probe.ArgumentList.Add(wslNodeInit + "node --version");
                using var test = Process.Start(probe) ?? throw new IOException("Could not launch WSL.");
                var version = (await test.StandardOutput.ReadToEndAsync()).Trim();
                var errors = (await test.StandardError.ReadToEndAsync()).Trim();
                await test.WaitForExitAsync();
                if (test.ExitCode != 0)
                    throw new IOException("Node.js was not found inside the default WSL Linux distribution (including nvm). This does NOT mean Windows Node is missing. Run 'wsl -l -v' to check which distro is the default, and install/configure Node.js 22+ in that distro. " + errors);
                var match = Regex.Match(version, @"^v(\d+)\.");
                if (!match.Success || int.Parse(match.Groups[1].Value) < 22)
                    throw new IOException("WSL Node.js 22+ required, detected: " + version);
                // Pass the IP as a positional shell argument; never interpolate user input into shell text.
                await StartProcessAsync("wsl.exe", ["--cd", folder, "--", "bash", "-lc",
                    wslNodeInit + "exec node run.cjs --rpc \"$1\"", "ns2-manager", pc], folder);
                WriteLog("WSL Node runner launched using the Windows checkout. Do not start another runner at the same time.");
            }
            else
            {
                await StartProcessAsync("node", ["run.cjs", "--rpc", pc], folder);
                WriteLog("WARNING: Windows-native FS acquisition has failed on some consoles. Use WSL mode if this recurs.");
            }
            WriteLog("Continue console-side steps using the matching bootstrap. A launched process does not imply a successful exploit.");
        }
        catch (Exception ex) { WriteLog("Cannot start ns2jb: " + ex.Message); }
    }
    private async Task StartCakeAsync()
    {
        try
        {
            SaveSettings();
            if (ounceProcess != null && !ounceProcess.HasExited)
                throw new InvalidOperationException("OunceCake is already running. Select a game from the list, or stop the existing process before starting another.");
            var folder = deps[1].Root;
            var id = title.Text.Trim().ToUpperInvariant();
            if (id.Length > 0 && !Regex.IsMatch(id, @"\A[0-9A-F]{16}\z"))
                throw new InvalidOperationException("Optional Title ID must be exactly 16 hexadecimal digits, or blank.");
            if (!File.Exists(Path.Combine(folder, "ouncecake.cjs")))
                throw new FileNotFoundException("Install OunceCake under Dependencies first.");
            var dir = Path.GetFullPath(run.Text.Trim());
            if (!File.Exists(Path.Combine(dir, "switch2", "current-acquisition.json")) ||
                !File.Exists(Path.Combine(dir, "switch2", "ro", "root", "current-root.json")))
                throw new InvalidOperationException("Choose the completed RPC run directory (session metadata missing).");
            if (!processes.Any(p => !p.HasExited))
                throw new InvalidOperationException("Start a live ns2jb RPC session in this manager first.");

            games.Items.Clear();
            discoveredGames.Clear();
            chooseGame.Enabled = false;

            var psi = new ProcessStartInfo("node")
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("ouncecake.cjs");
            psi.ArgumentList.Add(dir);
            if (id.Length > 0) psi.ArgumentList.Add(id);
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add("8080");

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) HandleOunceLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) WriteLog("OunceCake ERR: " + e.Data); };
            p.Exited += (_, _) =>
            {
                try { WriteLog($"OunceCake exited (PID {p.Id}, code {p.ExitCode})."); }
                catch { }
                try { if (!IsDisposed && IsHandleCreated) BeginInvoke(() => chooseGame.Enabled = false); } catch { }
            };
            if (!p.Start()) throw new IOException("Unable to start OunceCake.");
            ounceProcess = p;
            processes.Add(p);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            WriteLog(id.Length == 0 ? "OunceCake discovering installed titles; select one below when listed." :
                $"OunceCake launching explicit title {id}.");
        }
        catch (Exception ex) { WriteLog("Cannot start OunceCake: " + ex.Message); }
        await Task.CompletedTask;
    }

    private void HandleOunceLine(string line)
    {
        WriteLog(line);
        // OunceCake's gameMenu() prints: 1. Game Name [16-char ID] (Internal, SD)
        var match = Regex.Match(line, @"^\s*(\d+)\.\s+(.+?)\s+\[([0-9A-Fa-f]{16})\]\s+\(([^)]*)\)\s*$");
        if (!match.Success || IsDisposed || !IsHandleCreated) return;
        var index = int.Parse(match.Groups[1].Value);
        var name = match.Groups[2].Value.Trim();
        var id = match.Groups[3].Value.ToUpperInvariant();
        var storage = match.Groups[4].Value;
        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                if (discoveredGames.Any(x => x.Index == index)) return;
                discoveredGames.Add((index, name, id));
                games.Items.Add($"{index}. {name} [{id}] ({storage})");
                if (games.SelectedIndex < 0) games.SelectedIndex = 0;
                chooseGame.Enabled = ounceProcess != null && !ounceProcess.HasExited;
            });
        }
        catch { }
    }

    private async Task SelectGameAsync()
    {
        if (games.SelectedIndex < 0 || games.SelectedIndex >= discoveredGames.Count)
        {
            WriteLog("Select a listed game first."); return;
        }
        var p = ounceProcess;
        if (p == null || p.HasExited)
        {
            WriteLog("OunceCake is no longer waiting for selection. Start it again."); return;
        }
        var game = discoveredGames[games.SelectedIndex];
        try
        {
            await p.StandardInput.WriteLineAsync(game.Index.ToString());
            await p.StandardInput.FlushAsync();
            title.Text = game.Id;
            chooseGame.Enabled = false;
            WriteLog($"Selected {game.Name} [{game.Id}]. Waiting for the WebDAV server ready message.");
        }
        catch (Exception ex) { WriteLog("Game selection failed: " + ex.Message); }
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
