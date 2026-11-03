# NS2 Manager

NS2 Manager is a Windows application for running ns2jb, opening installed Switch 2 titles through OunceCake, and copying their exposed files to a folder on your PC.

The application brings the host, game list, and file transfers into one window. You still use SmileBASIC on the console to connect to ns2jb. OunceCake provides read-only access to the mounted title's RomFS and ExeFS, including update content when available. These backups are extracted files, **not** installable NSP/XCI images or save data.

## Requirements

- Windows 10 or 11 (64-bit)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build the application
- Node.js 22 or newer to run the host tools
- A Switch 2 with the matching SmileBASIC/ns2jb bootstrap already prepared
- PC and console on the same reachable network

You can use either the Windows Node.js runner or WSL. For WSL, install Node.js 22+ inside the default Linux distribution as well. The manager checks the default distribution and loads a standard `nvm` installation when present.

## Build

Clone or download this repository, open its folder, and double-click `build.bat`.

The script checks for the .NET 8 SDK and builds a self-contained Windows executable:

```text
dist\win-x64\NS2Manager.exe
```

You do not need the .NET runtime installed separately to run the published executable. For build errors or command-line options, see the [setup guide](docs/SETUP.md).

## Set up ns2jb and OunceCake

On the first launch, select each dependency in the list, click **Check for updates**, then **Install selected update**. Both downloads are installed under:

```text
%LOCALAPPDATA%\NS2Manager\tools
```

The manager does not update these tools without your approval. Check the upstream changes before installing a new revision.

Set **PC LAN IP** to the IPv4 address the Switch 2 can reach. Select **Windows** or **WSL** for the runner. The **Switch 2 IP** field can be used to record the console's address; OunceCake backups use the address reported by the OunceCake server, not that field.

## Connect the console

1. Click **Start ns2jb (RPC)**.
2. Wait for the message `Waiting for Switch 2 at ...:39380`.
3. Open the saved ns2jb bootstrap in SmileBASIC on the Switch 2.
4. Enter `RUN`, then `REMOVE NSR,&H60000000,&H3FFFFFFF`. After the expected Out of memory message, enter the `V=START(...)` command shown in the manager log.
5. Keep SmileBASIC open while the host works through its four stages.

The **List games (OunceCake)** button remains unavailable until ns2jb reports `4/4 verified` and its held RPC session is confirmed. It is disabled again if ns2jb stops.

For the full walkthrough, including the nine-argument SmileBASIC command and recovery notes, see [Setup and usage](docs/SETUP.md).

## Browse games and back up files

After ns2jb is verified, click **List games (OunceCake)**. OunceCake reads the installed-title list and the manager displays the games and their storage locations.

Select a row and click **Open selected game** to mount it. Once OunceCake reports `Serving on http://...:8080/`, its file server is ready.

Each game also has a **Backup** button. Choose an empty destination folder and the manager will mount that game if necessary, then copy the available files through WebDAV. The **Backup title files** button copies the game that is currently mounted. File transfers include size checks and a SHA-256 manifest. Incomplete transfers may leave `.partial` files; keep the console and both host processes running until the transfer finishes.

**Stop OunceCake** is available only while OunceCake is running. It stops the game server without stopping ns2jb. **Stop host processes** ends both. Closing NS2 Manager also stops the processes it started.

## Common problems

**ns2jb stops at 2/4:** Check the `switch2/acquisition.log` file inside the run folder printed in the log. An acquisition failure is not automatically caused by the Windows or WSL runner. Follow the ns2jb recovery instructions and restart the console before another attempt.

**OunceCake cannot find a named pipe:** Make sure ns2jb reached `4/4 verified` and is still running. Do not reuse a run folder from an earlier session.

**The game list is empty:** Leave the ns2jb process open, click **List games (OunceCake)**, and check the output for title-discovery errors. OunceCake supports one active game mount at a time.

**A backup fails:** Make sure OunceCake has printed `Serving on`. Select an empty destination folder, and check that Windows allows writing to it. The download URL is taken from OunceCake's own output.

## Notes

ns2jb performs console-side operations through its bootstrap, including temporary save-file activity. A restart clears volatile changes but cannot undo writes to persistent storage. Review the upstream documentation before using the tool.

The dependency source repositories are maintained separately. Their respective licenses and attribution remain with their authors.

- [ns2jb (Cellenseres)](https://github.com/Cellenseres/ns2jb)
- [OunceCake (ariankordi)](https://github.com/ariankordi/OunceCake)
- [Original ns2jb project](https://gitlab.com/kurumuz/ns2jb)

See [CHANGELOG.md](CHANGELOG.md) for application changes.
