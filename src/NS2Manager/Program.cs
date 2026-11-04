using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
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
    public string RunnerMode { get; set; } = "WSL (recommended)";
}

internal sealed class Dependency
{
    public required string Name { get; init; }
    public required string Owner { get; init; }
    public required string Repo { get; init; }
    public bool IsGitLab { get; init; }
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
        new() { Name = "ns2jb-wifi", Owner = "nyathea", Repo = "ns2jb-wifi", IsGitLab = true },
        new() { Name = "OunceCake", Owner = "ariankordi", Repo = "OunceCake" }
    ];
    private readonly TextBox host = new() { Width = 150 };
    private readonly TextBox console = new() { Width = 150 };
    private readonly TextBox run = new() { Width = 525 };
    private readonly ComboBox runnerMode = new() { Width = 175, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new("Consolas", 9.5f) };
    private readonly ListBox versions = new() { Width = 620, Height = 90 };
    private readonly Button check = new() { Text = "Check for updates", AutoSize = true };
    private readonly Button install = new() { Text = "Install selected update", AutoSize = true };
    private readonly Button start = new() { Text = "Start ns2jb", AutoSize = true };
    private readonly Button startWifi = new() { Text = "Start ns2jb-WiFi", AutoSize = true };
    private readonly Button cake = new() { Text = "List games (OunceCake)", AutoSize = true, Enabled = false };
    private readonly Button chooseGame = new() { Text = "Open selected game", AutoSize = true, Enabled = false };
    private readonly DataGridView games = new()
    {
        Width = 775, Height = 174, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle
    };
    private readonly List<(int Index, string Name, string Id)> discoveredGames = [];
    private Process? ounceProcess;
    private Process? hostProcess;
    private Dependency? activeHostDependency;
    private bool hostStarting;
    private string? activeRunDirectory;
    private bool ounceReady;
    private bool hostVerified;
    private string? servingTitleId;
    private Uri? servingUrl;
    private string? pendingBackupDirectory;
    private string? pendingBackupTitleId;
    private readonly Button stop = new() { Text = "Stop host processes", AutoSize = true };
    private readonly Button stopCake = new() { Text = "Stop OunceCake", AutoSize = true, Enabled = false };
    private readonly Button findRun = new() { Text = "Find latest run", AutoSize = true };
    private readonly Button backup = new() { Text = "Backup title files…", AutoSize = true, Enabled = false };
    private readonly Button cancelBackup = new() { Text = "Cancel backup", AutoSize = true, Enabled = false };
    private CancellationTokenSource? backupCancellation;
    private readonly Label status = new() { AutoSize = true, Text = "Idle" };
    private readonly List<Process> processes = [];
    private Settings settings = new();
    private bool busy;

    public MainForm()
    {
        Text = "NS2 Manager";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        ClientSize = new Size(880, 790);
        MinimumSize = new Size(830, 680);
        Font = new Font("Segoe UI", 10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NS2Manager/0.1 (+https://github.com/Gamingwithgoose/NS2-Manager)");
        Directory.CreateDirectory(AppPaths.Data);
        try { settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.Settings)) ?? new(); }
        catch { /* keep defaults */ }
        host.Text = settings.HostIp; console.Text = settings.ConsoleIp;
        run.Text = settings.RunDirectory;
        runnerMode.Items.AddRange(["WSL", "Windows"]);
        var savedRunner = settings.RunnerMode.StartsWith("WSL", StringComparison.OrdinalIgnoreCase) ? "WSL" : "Windows";
        runnerMode.SelectedItem = savedRunner;

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

        var session = new GroupBox { Text = "Exploit host and content server", Height = 350, Dock = DockStyle.Top };
        var sessionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };
        var runRow = new FlowLayoutPanel { Width = 790, Height = 37 };
        runRow.Controls.AddRange([new Label { Text = "Run:", Width = 38, Padding = new Padding(0, 7, 0, 0) }, run, findRun]);
        var actions = new FlowLayoutPanel { Width = 790, Height = 37, WrapContents = false };
        actions.Controls.AddRange([start, startWifi, cake, chooseGame]);
        var transferActions = new FlowLayoutPanel { Width = 790, Height = 37, WrapContents = false };
        transferActions.Controls.AddRange([backup, cancelBackup, stopCake, stop, status]);
        games.Columns.Add(new DataGridViewTextBoxColumn { Name = "Game", HeaderText = "Installed game", FillWeight = 65 });
        games.Columns.Add(new DataGridViewTextBoxColumn { Name = "Storage", HeaderText = "Storage", FillWeight = 22 });
        games.Columns.Add(new DataGridViewButtonColumn { Name = "GameBackup", HeaderText = "Action", Text = "Backup", UseColumnTextForButtonValue = true, FillWeight = 13 });
        games.CellContentClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == games.Columns["GameBackup"]!.Index)
                await BackupGameAsync(e.RowIndex);
        };
        sessionPanel.Controls.AddRange([runRow, actions, transferActions, games]); session.Controls.Add(sessionPanel);
        page.Controls.Add(session, 0, 2);

        var logs = new GroupBox { Text = "Live output", Dock = DockStyle.Fill, Padding = new Padding(8) };
        logs.Controls.Add(log); page.Controls.Add(logs, 0, 3);

        foreach (var d in deps) { d.InstalledSha = File.Exists(d.RevisionFile) ? File.ReadAllText(d.RevisionFile).Trim() : null; versions.Items.Add(d); }
        versions.SelectedIndex = 0;
        check.Click += async (_, _) => await CheckUpdatesAsync();
        install.Click += async (_, _) => await InstallSelectedAsync();
        start.Click += async (_, _) => await StartHostAsync(deps[0]);
        startWifi.Click += async (_, _) => await StartHostAsync(deps[1]);
        cake.Click += async (_, _) =>
        {
            if (ounceProcess != null && !ounceProcess.HasExited)
            {
                WriteLog("Stopping the current OunceCake mount to rediscover games; ns2jb remains running.");
                StopOunceCake();
            }
            await StartCakeAsync(string.Empty);
        };
        chooseGame.Click += async (_, _) => await SelectGameAsync();
        games.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex != games.Columns["GameBackup"]!.Index)
                await SelectGameAsync();
        };
        backup.Click += async (_, _) => await BackupAsync();
        cancelBackup.Click += (_, _) => backupCancellation?.Cancel();
        stop.Click += (_, _) => StopProcesses();
        stopCake.Click += (_, _) => StopOunceCake();
        findRun.Click += (_, _) => FindLatestRun();
        FormClosing += (_, _) => { backupCancellation?.Cancel(); SaveSettings(); StopProcesses(); http.Dispose(); };
        WriteLog("Ready. Start ns2jb to establish a session with your Switch 2.");
    }

    private void WriteLog(string s)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => WriteLog(s)); return; }
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}");
    }
    private void RefreshHostButtons()
    {
        bool canStart = !hostStarting && (hostProcess == null || hostProcess.HasExited);
        start.Enabled = canStart;
        startWifi.Enabled = canStart;
    }

    private void RefreshCakeAvailability()
    {
        cake.Enabled = hostVerified && hostProcess != null && !hostProcess.HasExited;
    }

    private void RefreshOunceCakeStopAvailability()
    {
        stopCake.Enabled = ounceProcess != null && !ounceProcess.HasExited;
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
        settings.RunDirectory = run.Text.Trim(); settings.RunnerMode = runnerMode.SelectedItem?.ToString() ?? "WSL";
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
                try
                {
                    if (d.IsGitLab)
                    {
                        // GitLab project 88311107 is nyathea/ns2jb-wifi.
                        const string projectApi = "https://gitlab.com/api/v4/projects/88311107";
                        using var projectResponse = await http.GetAsync(projectApi);
                        projectResponse.EnsureSuccessStatusCode();
                        using var project = JsonDocument.Parse(await projectResponse.Content.ReadAsStringAsync());
                        var branch = project.RootElement.GetProperty("default_branch").GetString();
                        if (string.IsNullOrWhiteSpace(branch)) throw new IOException("GitLab did not report a default branch.");
                        using var branchResponse = await http.GetAsync(
                            projectApi + "/repository/branches/" + Uri.EscapeDataString(branch));
                        branchResponse.EnsureSuccessStatusCode();
                        using var branchDoc = JsonDocument.Parse(await branchResponse.Content.ReadAsStringAsync());
                        d.LatestSha = branchDoc.RootElement.GetProperty("commit").GetProperty("id").GetString();
                    }
                    else
                    {
                        using var response = await http.GetAsync($"https://api.github.com/repos/{d.Owner}/{d.Repo}/commits?per_page=1");
                        response.EnsureSuccessStatusCode();
                        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        d.LatestSha = document.RootElement[0].GetProperty("sha").GetString();
                    }
                    WriteLog($"{d.Name}: installed {d.InstalledSha ?? "none"}, upstream {d.LatestSha}");
                }
                catch (Exception ex)
                {
                    d.LatestSha = null;
                    WriteLog($"Could not check {d.Name}: {ex.Message}");
                }
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
        var confirmation = MessageBox.Show($"Install {d.Owner}/{d.Repo} at commit {d.LatestSha}?\n\nReview the upstream changes before installing. The current installation will be retained for rollback.",
            "Approve dependency update", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirmation != DialogResult.Yes) return;
        SetBusy(true);
        string? stage = null;
        try
        {
            var sha = d.LatestSha;
            var archiveUrl = d.IsGitLab
                ? $"https://gitlab.com/api/v4/projects/88311107/repository/archive.zip?sha={Uri.EscapeDataString(sha!)}"
                : $"https://api.github.com/repos/{d.Owner}/{d.Repo}/zipball/{sha}";
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
            var required = d.Name.StartsWith("ns2jb", StringComparison.Ordinal) ? "run.cjs" : "ouncecake.cjs";
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
        if (servingUrl == null || !ounceReady || ounceProcess == null || ounceProcess.HasExited)
        {
            WriteLog("No mounted OunceCake game is ready for backup."); return;
        }
        using var picker = new FolderBrowserDialog { Description = "Choose an EMPTY folder for the mounted game's extracted files", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (picker.ShowDialog(this) == DialogResult.OK) await BackupToFolderAsync(picker.SelectedPath);
    }

    private async Task BackupToFolderAsync(string destination)
    {
        if (busy) { WriteLog("A transfer or update is already running."); return; }
        if (servingUrl == null || !ounceReady || ounceProcess == null || ounceProcess.HasExited)
        {
            WriteLog("Cannot backup: OunceCake's server is no longer ready."); return;
        }
        SetBusy(true);
        backup.Enabled = false;
        backupCancellation = new CancellationTokenSource();
        cancelBackup.Enabled = true;
        try
        {
            SaveSettings();
            // Never substitute the console IP: the WebDAV server advertises its own address.
            WriteLog("Backing up selected title from " + servingUrl);
            await new WebDavBackup(servingUrl.Host, servingUrl.Port, WriteLog)
                .DownloadAsync(destination, backupCancellation.Token);
        }
        catch (OperationCanceledException) { WriteLog("Backup cancelled; incomplete .partial files may remain."); }
        catch (Exception ex) { WriteLog("Backup stopped: " + ex.Message); }
        finally
        {
            cancelBackup.Enabled = false;
            backupCancellation?.Dispose();
            backupCancellation = null;
            SetBusy(false);
            backup.Enabled = ounceReady && ounceProcess != null && !ounceProcess.HasExited;
        }
    }

    private async Task BackupGameAsync(int rowIndex)
    {
        if (busy) { WriteLog("A transfer is already running."); return; }
        if (rowIndex < 0 || rowIndex >= discoveredGames.Count) return;
        var game = discoveredGames[rowIndex];
        using var picker = new FolderBrowserDialog
        {
            Description = $"Choose an EMPTY destination folder for {game.Name} [{game.Id}]",
            UseDescriptionForTitle = true, ShowNewFolderButton = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        var destination = picker.SelectedPath;
        if (Directory.EnumerateFileSystemEntries(destination).Any())
        {
            WriteLog("Choose an empty destination folder; no existing files will be overwritten.");
            return;
        }
        if (ounceReady && servingTitleId == game.Id)
        {
            await BackupToFolderAsync(destination);
            return;
        }
        if (ounceProcess == null || ounceProcess.HasExited)
        {
            WriteLog("OunceCake is not running. Start List games first."); return;
        }
        pendingBackupDirectory = destination;
        pendingBackupTitleId = game.Id;
        if (!ounceReady && servingTitleId == null)
        {
            games.Rows[rowIndex].Selected = true;
            games.CurrentCell = games.Rows[rowIndex].Cells[0];
            await SelectGameAsync();
            return;
        }
        // OunceCake serves one title at a time. Restart only it, preserving the host RPC.
        WriteLog($"Switching OunceCake to {game.Name}; ns2jb stays running.");
        var previous = ounceProcess;
        try
        {
            if (!previous.HasExited)
            {
                previous.Kill(entireProcessTree: true);
                await previous.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            pendingBackupDirectory = null; pendingBackupTitleId = null;
            WriteLog("Unable to switch OunceCake: " + ex.Message);
            return;
        }
        await StartCakeAsync(game.Id);
        servingTitleId = game.Id;
    }

    private async Task StartHostAsync(Dependency backend)
    {
        if (hostStarting || (hostProcess != null && !hostProcess.HasExited))
        {
            WriteLog("A host session is already starting or running. Stop it before switching ns2jb versions.");
            return;
        }
        hostStarting = true;
        RefreshHostButtons();
        try
        {
            hostVerified = false;
            activeRunDirectory = null;
            RefreshCakeAvailability();
            ounceReady = false;
            backup.Enabled = false;
            SaveSettings();
            var pc = ValidateIp(host.Text);
            var folder = backend.Root;
            if (!File.Exists(Path.Combine(folder, "run.cjs"))) throw new FileNotFoundException($"Install {backend.Name} under Dependencies first.");
            activeHostDependency = backend;
            if (runnerMode.SelectedItem?.ToString() == "WSL")
            {
                // WSL --cd accepts Windows paths on current WSL; no shell interpolation.
                var probe = new ProcessStartInfo("wsl.exe") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
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
                // pc is strictly validated as IPv4 before being placed in the Bash command.
                // wsl.exe argument forwarding can discard Bash positional arguments, so pass
                // the validated IP literally instead of relying on $1.
                hostProcess = await StartProcessAsync("wsl.exe", ["--cd", folder, "--", "bash", "-lc",
                    wslNodeInit + "exec node run.cjs --rpc " + pc], folder);
                WriteLog($"{backend.Name} started through WSL. The two ns2jb backends cannot run simultaneously.");
            }
            else
            {
                hostProcess = await StartProcessAsync("node", ["run.cjs", "--rpc", pc], folder);
                WriteLog($"{backend.Name} started using Windows Node. If FS acquisition fails, check the acquisition log before trying again.");
            }
            WriteLog("Continue console-side steps using the matching bootstrap. A launched process does not imply a successful exploit.");
        }
        catch (Exception ex)
        {
            if (hostProcess == null || hostProcess.HasExited) activeHostDependency = null;
            WriteLog($"Cannot start {backend.Name}: " + ex.Message);
        }
        finally
        {
            hostStarting = false;
            RefreshHostButtons();
        }
    }
    private async Task StartCakeAsync(string? requestedTitle = null)
    {
        try
        {
            SaveSettings();
            if (!hostVerified || hostProcess == null || hostProcess.HasExited)
                throw new InvalidOperationException("Wait until ns2jb reports 4/4 verified before starting OunceCake.");
            if (ounceProcess != null && !ounceProcess.HasExited)
                throw new InvalidOperationException("OunceCake is already running. Use Stop OunceCake only or the game list to switch mounts.");
            // Resolve by dependency name so adding another host backend cannot redirect OunceCake.
            var folder = deps.Single(d => d.Name == "OunceCake").Root;
            var id = (requestedTitle ?? string.Empty).Trim().ToUpperInvariant();
            if (id.Length > 0 && !Regex.IsMatch(id, @"\A[0-9A-F]{16}\z"))
                throw new InvalidOperationException("Optional Title ID must be exactly 16 hexadecimal digits, or blank.");
            if (!File.Exists(Path.Combine(folder, "ouncecake.cjs")))
                throw new FileNotFoundException("Install OunceCake under Dependencies first.");
            var dir = Path.GetFullPath(run.Text.Trim());
            if (!File.Exists(Path.Combine(dir, "switch2", "current-acquisition.json")) ||
                !File.Exists(Path.Combine(dir, "switch2", "ro", "root", "current-root.json")))
                throw new InvalidOperationException("Choose the completed RPC run directory (session metadata missing).");
            if (hostProcess == null || hostProcess.HasExited)
                throw new InvalidOperationException("The ns2jb host process is no longer running. Restart the console before starting a new exploit attempt.");
            if (activeRunDirectory == null ||
                !string.Equals(Path.GetFullPath(activeRunDirectory), dir, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This run directory does not match the current manager-launched host session. Do not use a previous run.");
            var resultPath = Path.Combine(dir, "result.json");
            if (!File.Exists(resultPath))
                throw new InvalidOperationException("The selected ns2jb session has not reached verified RPC mode (result.json missing).");
            using (var result = JsonDocument.Parse(File.ReadAllText(resultPath)))
            {
                if (!result.RootElement.TryGetProperty("verified", out var verified) || verified.ValueKind != JsonValueKind.True)
                    throw new InvalidOperationException("The selected ns2jb session has not verified filesystem access.");
            }
            var activeHostFolder = activeHostDependency?.Root ??
                throw new InvalidOperationException("No active ns2jb backend was selected.");
            var statusInfo = new ProcessStartInfo("node") { WorkingDirectory = activeHostFolder,
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true };
            statusInfo.ArgumentList.Add("rpc.cjs");
            statusInfo.ArgumentList.Add(dir);
            statusInfo.ArgumentList.Add("status");
            using (var statusProbe = Process.Start(statusInfo) ?? throw new IOException("Cannot run RPC status probe."))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try { await statusProbe.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    try { statusProbe.Kill(entireProcessTree: true); } catch { }
                    throw new IOException("RPC status probe timed out. Session may be stale. Not starting OunceCake.");
                }
                var output = await statusProbe.StandardOutput.ReadToEndAsync();
                var errors = await statusProbe.StandardError.ReadToEndAsync();
                if (statusProbe.ExitCode != 0)
                    throw new IOException("The RPC endpoint is unavailable: " + errors.Trim());
                WriteLog("Live RPC status verified: " + output.Trim().Replace(Environment.NewLine, " "));
            }

            ounceReady = false;
            servingUrl = null;
            servingTitleId = null;
            backup.Enabled = false;
            if (id.Length == 0)
            {
                games.Rows.Clear();
                discoveredGames.Clear();
            }
            chooseGame.Enabled = false;

            var psi = new ProcessStartInfo("node")
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
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
                try { if (!IsDisposed && IsHandleCreated) BeginInvoke(() =>
                {
                    if (!ReferenceEquals(ounceProcess, p)) return;
                    RefreshOunceCakeStopAvailability();
                    chooseGame.Enabled = false;
                    ounceReady = false; backup.Enabled = false; servingUrl = null; servingTitleId = null;
                    if (pendingBackupDirectory != null)
                    {
                        WriteLog("OunceCake exited before the queued backup could start.");
                        pendingBackupDirectory = null; pendingBackupTitleId = null;
                    }
                }); } catch { }
            };
            if (!p.Start()) throw new IOException("Unable to start OunceCake.");
            ounceProcess = p;
            RefreshOunceCakeStopAvailability();
            if (id.Length > 0) servingTitleId = id;
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
        var served = Regex.Match(line, @"Serving on (http://[0-9.]+:[0-9]+/)", RegexOptions.IgnoreCase);
        if (served.Success && !IsDisposed && IsHandleCreated)
        {
            var candidate = served.Groups[1].Value;
            try { BeginInvoke(async () =>
            {
                if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return;
                servingUrl = uri;
                ounceReady = true;
                backup.Enabled = !busy;
                WriteLog("OunceCake server address detected: " + servingUrl);
                if (pendingBackupDirectory is string destination && pendingBackupTitleId == servingTitleId)
                {
                    pendingBackupDirectory = null; pendingBackupTitleId = null;
                    await BackupToFolderAsync(destination);
                }
            }); } catch { }
        }
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
                games.Rows.Add(name, storage);
                if (games.CurrentRow == null && games.Rows.Count > 0) games.Rows[0].Selected = true;
                chooseGame.Enabled = ounceProcess != null && !ounceProcess.HasExited;
            });
        }
        catch { }
    }

    private async Task SelectGameAsync()
    {
        if (games.CurrentRow == null || games.CurrentRow.Index < 0 || games.CurrentRow.Index >= discoveredGames.Count)
        {
            WriteLog("Select a listed game first."); return;
        }
        var p = ounceProcess;
        if (p == null || p.HasExited)
        {
            WriteLog("OunceCake is no longer waiting for selection. Start it again."); return;
        }
        var game = discoveredGames[games.CurrentRow.Index];
        try
        {
            await p.StandardInput.WriteLineAsync(game.Index.ToString());
            await p.StandardInput.FlushAsync();
            servingTitleId = game.Id;
            chooseGame.Enabled = false;
            WriteLog($"Selected {game.Name} [{game.Id}]. Waiting for the WebDAV server ready message.");
        }
        catch (Exception ex) { WriteLog("Game selection failed: " + ex.Message); }
    }

    private Task<Process> StartProcessAsync(string executable, IEnumerable<string> args, string workingDirectory)
    {
        var psi = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            WriteLog(e.Data);
            if (e.Data.StartsWith("4/4 verified.", StringComparison.Ordinal) && !IsDisposed && IsHandleCreated)
                try { BeginInvoke(() =>
                {
                    if (!ReferenceEquals(hostProcess, p) || p.HasExited) return;
                    var resultPath = activeRunDirectory == null ? null : Path.Combine(activeRunDirectory, "result.json");
                    try
                    {
                        if (resultPath == null || !File.Exists(resultPath)) throw new IOException("Verified result file is missing.");
                        using var result = JsonDocument.Parse(File.ReadAllText(resultPath));
                        if (!result.RootElement.TryGetProperty("verified", out var verified) || verified.ValueKind != JsonValueKind.True ||
                            !result.RootElement.TryGetProperty("rpc", out var rpc) ||
                            !rpc.TryGetProperty("state", out var state) || state.GetString() != "listening")
                            throw new IOException("The host has not confirmed a held RPC session.");
                        hostVerified = true;
                        RefreshCakeAvailability();
                        WriteLog("ns2jb fully established; OunceCake game discovery is now enabled.");
                    }
                    catch (Exception ex) { WriteLog("Cannot enable OunceCake yet: " + ex.Message); }
                }); } catch { }
            var match = Regex.Match(e.Data, @"^Run folder:\s*(.+)$");
            if (match.Success && !IsDisposed && IsHandleCreated)
                try { BeginInvoke(() => { activeRunDirectory = Path.GetFullPath(match.Groups[1].Value.Trim()); run.Text = activeRunDirectory; WriteLog("Active run folder captured from ns2jb."); }); } catch { }
        };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) WriteLog("ERR: " + e.Data); };
        p.Exited += (_, _) =>
        {
            try { WriteLog($"Process exited (PID {p.Id}, code {p.ExitCode})."); } catch { }
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(() =>
            {
                if (!ReferenceEquals(hostProcess, p)) return;
                hostVerified = false;
                RefreshCakeAvailability();
                backup.Enabled = false;
                RefreshHostButtons();
                WriteLog("ns2jb host exited; OunceCake startup is disabled.");
            }); } catch { }
        };
        if (!p.Start()) throw new IOException("Unable to start process.");
        processes.Add(p);
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        WriteLog($"Started {Path.GetFileName(executable)} (PID {p.Id}) from {workingDirectory}.");
        return Task.FromResult(p);
    }
    private void FindLatestRun()
    {
        var root = Path.Combine(activeHostDependency?.Root ?? deps[0].Root, "runs");
        if (!Directory.Exists(root)) { WriteLog("No runs directory found."); return; }
        var dirs = Directory.GetDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "switch2", "current-acquisition.json")))
            .OrderByDescending(Directory.GetLastWriteTimeUtc).ToArray();
        if (dirs.Length == 0) { WriteLog("No completed acquisition metadata was found."); return; }
        run.Text = dirs[0];
        WriteLog("Found candidate run: " + dirs[0] + ". OunceCake requires the current manager-started session, not just the newest folder.");
    }
    private void StopOunceCake()
    {
        backupCancellation?.Cancel();
        ounceReady = false;
        backup.Enabled = false;
        chooseGame.Enabled = false;
        servingUrl = null;
        servingTitleId = null;
        pendingBackupDirectory = null;
        pendingBackupTitleId = null;
        var p = ounceProcess;
        ounceProcess = null;
        RefreshOunceCakeStopAvailability();
        if (p == null) { WriteLog("No manager-started OunceCake process is running."); return; }
        try
        {
            if (!p.HasExited)
            {
                // Only stops OunceCake, never the retained ns2jb host.
                p.Kill(entireProcessTree: true);
                p.WaitForExit(1500);
            }
            WriteLog("OunceCake stopped. The verified ns2jb RPC session remains running.");
        }
        catch (Exception ex) { WriteLog("Stop OunceCake warning: " + ex.Message); }
    }

    private void StopProcesses()
    {
        hostVerified = false;
        cake.Enabled = false;
        backupCancellation?.Cancel();
        ounceReady = false;
        backup.Enabled = false;
        chooseGame.Enabled = false;
        activeRunDirectory = null;
        servingUrl = null;
        servingTitleId = null;
        pendingBackupDirectory = null;
        pendingBackupTitleId = null;
        foreach (var p in processes.ToArray())
        {
            try { if (!p.HasExited) { p.Kill(entireProcessTree: true); p.WaitForExit(1500); } }
            catch (Exception ex) { WriteLog("Stop warning: " + ex.Message); }
            finally { p.Dispose(); }
        }
        processes.Clear();
        hostProcess = null;
        activeHostDependency = null;
        hostStarting = false;
        RefreshHostButtons();
        ounceProcess = null;
        RefreshOunceCakeStopAvailability();
        WriteLog("Stopped manager-launched processes. This does not restore console state.");
    }
}
