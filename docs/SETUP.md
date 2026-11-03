# Setup and usage

This guide covers building NS2 Manager, connecting a Switch 2 through ns2jb, browsing games in OunceCake, and downloading the files OunceCake exposes. The console-side ns2jb bootstrap must already be set up; this application does not install SmileBASIC or prepare the bootstrap.

## 1. Build NS2 Manager on Windows

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The SDK is required for building; installing only the runtime is not enough.

Get the NS2 Manager source and run `build.bat` in the repository folder. The script publishes the Windows x64 executable to:

```text
dist\win-x64\NS2Manager.exe
```

To build manually from PowerShell or Command Prompt:

```powershell
dotnet publish src/NS2Manager/NS2Manager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/win-x64
```

Run `NS2Manager.exe` after the build completes. The application stores its settings and downloaded dependencies under `%LOCALAPPDATA%\NS2Manager`.

## 2. Install the host dependencies

NS2 Manager downloads two separate projects: **ns2jb** runs the host connection and retained RPC session, while **OunceCake** mounts a game and serves its files.

Install Node.js 22 or newer. For the Windows runner, Node must be available in Windows PATH. For the WSL runner, Node must be available inside the default Linux distribution; a Windows-only Node installation does not satisfy this requirement.

In the application's dependency list:

1. Click **Check for updates**.
2. Select **ns2jb** and click **Install selected update**. Review the confirmation before proceeding.
3. Select **OunceCake** and install its update the same way.

Dependencies are stored separately under `%LOCALAPPDATA%\NS2Manager\tools`. An update does not run unless you approve it. Do not update either dependency during an active connection.

## 3. Set the network address and start ns2jb

Enter your PC's LAN IPv4 address in **PC LAN IP**. This must be the address reachable from the Switch 2, not an unrelated WSL virtual-interface address.

Select **Windows** to run ns2jb with Windows Node.js or **WSL** to run it through the default Linux distribution. The WSL option uses the dependency folder under your Windows profile, not an independent checkout under your Linux home directory.

Click **Start ns2jb (RPC)**. Wait until the log reports:

```text
Waiting for Switch 2 at YOUR_PC_IP:39380.
```

Do not enter the console-side connection command before the host starts listening.

## 4. Connect from SmileBASIC

On the Switch 2, open the saved ns2jb bootstrap in a fresh SmileBASIC session. Use the commands printed by ns2jb in the manager's log.

The sequence is:

```text
RUN
REMOVE NSR,&H60000000,&H3FFFFFFF
```

After the expected Out of memory message, enter `V=START` using the IP shown in the host log. For example, if the PC address is `192.168.1.228`:

```text
V=START(39380,192,168,1,228)
```

If SmileBASIC reports the wrong number of arguments, use the nine-argument form:

```text
V=START(39380,192,168,1,228,0,0,0,0)
```

The example IP is not a default setting. Use the address shown in your own host output.

ns2jb works through four stages: checking the console, acquiring FS memory, starting the native helper, and running its FS check. Keep the host process and SmileBASIC open until the work is finished. **List games (OunceCake)** becomes available only after `4/4 verified` and a held RPC session are confirmed.

## 5. Find a game and start OunceCake

Click **List games (OunceCake)**. The manager starts OunceCake without asking for a title ID and displays the discovered games in a table.

Select a game and click **Open selected game**. OunceCake opens the game's available content, which may include the base game and installed updates. Some update mounts can take longer than the others.

When the log prints `Serving on http://IP:8080/`, OunceCake is ready. The IP address can differ from the console address entered in the manager. The backup feature uses the address reported by OunceCake.

To change games, use the game table or start discovery again. OunceCake serves one game at a time.

## 6. Back up exposed files

Click **Backup** in the row for the game you want. Choose an empty destination folder. The manager mounts that game if necessary and downloads the available files over HTTP/WebDAV. The separate **Backup title files** button downloads whichever game is currently mounted.

A completed backup contains the files exposed by OunceCake and `ns2manager-backup-manifest.json`, which records the downloaded paths, sizes, and SHA-256 hashes. If a transfer is interrupted, partial files may remain. Use **Cancel backup** to interrupt an active transfer.

These files are extracted RomFS/ExeFS content. They are not an NSP, XCI, or savedata backup.

## 7. Stop a session

**Stop OunceCake** becomes clickable only when OunceCake is running. It stops the OunceCake process and leaves ns2jb running.

**Stop host processes** stops both ns2jb and OunceCake. Closing the manager also stops the processes the application started.

Keep SmileBASIC open while a mounted game or file transfer needs the live RPC session. Stop OunceCake before ending the host session.

## Troubleshooting

### ns2jb fails during FS memory acquisition

A failure at `2/4` means the host connected but acquisition did not finish. The log prints a run folder. Inside it, open `switch2/acquisition.log` for the detailed failure. Stop the failed host process and restart the Switch 2 before another attempt, as the upstream tool instructs. Trying Windows versus WSL may be useful when diagnosing the host environment, but neither mode guarantees acquisition.

### OunceCake reports ENOENT on a named pipe

That generally means the held endpoint for the selected ns2jb run is unavailable. Check that the session reached `4/4 verified`, that ns2jb remains running, and that the run folder belongs to the current connection. Run folders from previous sessions cannot restore an expired endpoint.

### OunceCake starts but there is no backup

Wait for the `Serving on` line before downloading. Make sure the selected destination is empty and writable, and that the console is still connected. The transfer uses OunceCake's announced server address.

### WSL cannot find Node.js

Check Node inside the default WSL Linux distribution:

```powershell
wsl -- node --version
```

If Node is managed with `nvm`, make sure it is installed in the default Linux distribution. The manager initializes the standard `~/.nvm/nvm.sh` script when available.

### The wrong network interface is selected

Use the PC address that the Switch can actually reach. On systems with WSL, VPN software, or multiple adapters, several IPv4 addresses may appear. The `172.x.x.x` WSL virtual address is not necessarily reachable from the console.

## About console access

The ns2jb bootstrap performs operations on the console, including temporary save-file activity. A reboot clears volatile state but does not reverse persistent storage writes. Check the [ns2jb documentation](https://github.com/Cellenseres/ns2jb) before running a new version or changing the bootstrap.
