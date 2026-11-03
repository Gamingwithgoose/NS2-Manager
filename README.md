# NS2 Manager

Windows desktop manager for a **user-controlled** Switch 2 research session. This is an early host-side prototype, not a jailbreak, CFW, or universal game-image dumper.

## Build
**Easy Windows build:** install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then double-click `build.bat` at the repository root. The standalone EXE will be at `dist/win-x64/NS2Manager.exe`. This is a host-side build and does not launch an exploit on its own.

Windows 10/11, .NET 8 SDK. Alternatively, run `dotnet run --project src/NS2Manager/NS2Manager.csproj` or publish with `dotnet publish src/NS2Manager/NS2Manager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`.

## First-run workflow
1. Open **Dependencies**, click **Check for Updates**, then **Install selected**. Review the sources and the exact commit IDs before installing. Nothing updates automatically.
2. Install Node.js 22+ separately. The manager checks for Node and does not silently install it.
3. In **Session**, choose **WSL (recommended)** and your host LAN IP address. You must have WSL installed, with Node.js 22+ available inside the *default Linux distribution* (not just Windows). Click **Start ns2jb (RPC)**. The GUI launches `wsl.exe --cd <checkout> -- node run.cjs --rpc <PC-IP>` using the installed source tree. **Windows (experimental)** remains selectable, but may fail at FS acquisition. Use your existing SmileBASIC input setup on the console; only the PC terminal is replaced.
4. A successful retained RPC session produces a run directory inside `ns2jb/runs`. Click **Find latest run**; the manager never reuses a previous run automatically.
5. Use **Start OunceCake** with a 16-hex-character title ID and the verified run directory. This runs OunceCake's HTTP/WebDAV server for the specific title. A full installed-title browser is planned, not implemented.
6. After OunceCake reports its server ready, click **Backup title files…**, choose a new empty folder and allow the manager to download the WebDAV index recursively. A SHA-256 manifest is written when all files have transferred. This is an experimental host implementation; verify output against your known-good method before relying on it. OunceCake builds without WebDAV PROPFIND support will report an explicit error instead of creating a false backup.

## Safety
- All console actions and their risks remain those of upstream ns2jb and OunceCake. No console exploit or save writes are performed by the manager itself.
- The manager launches official upstream source scripts, not arbitrary shell commands. Repository ZIPs are staged, path-validated, and installed with a backup of the previous version.
- Do not update dependencies while a session is active. Update checks use GitHub's default-branch commit API, so a new commit is not equivalent to a tested release; pin or roll back as needed.
- The installer uses HTTPS GitHub archives and checks archive integrity/path traversal but does not cryptographically authenticate the authors. Inspect any new upstream code before launching it.
- OunceCake's read-only title content is not an XCI/NSP and is not a game save backup.
- Logs may contain LAN addresses, run paths, title IDs, and identifiers. Review before sharing.
- Licenses remain with upstream authors; no third-party sources are copied into this repository.

## Upstreams
- Windows-oriented ns2jb host: https://github.com/Cellenseres/ns2jb
- OunceCake HTTP/WebDAV: https://github.com/ariankordi/OunceCake
- Alternative: https://github.com/Cellenseres/OunceCake
- Original exploit: https://gitlab.com/kurumuz/ns2jb

## Development status
- [x] Native Windows GUI, persistent preferences, Node check, output logs
- [x] Launch/stop native Windows ns2jb and OunceCake scripts
- [x] Dependency update check, manual installation, rollback staging
- [ ] Native installed-title discovery and read-only game browser
- [x] Experimental read-only WebDAV recursive download with per-file SHA-256 manifest
- [ ] Resumable transfers, cancel control, high-level aggregate progress, transfer integration tests
- [ ] Automated host and device integration tests

See CHANGELOG.md for changes.


### FS acquisition failure on Windows
If you see `FS acquisition did not complete`, do **not** immediately retry in the same console session. Inspect `switch2/acquisition.log` under the indicated run folder, close the stale host runner, restart the console as instructed upstream, then choose WSL mode. This GUI changes the host process, not the firmware-dependent exploit behavior. WSL shares the same Windows checkout through `/mnt/c`; verify compatibility with your known-good WSL setup before console testing.
