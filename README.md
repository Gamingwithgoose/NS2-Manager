# NS2 Manager

Windows desktop manager for a **user-controlled** Switch 2 research session. This is an early host-side prototype, not a jailbreak, CFW, or universal game-image dumper.

## Build
Windows 10/11, .NET 8 SDK. Run `dotnet run --project src/NS2Manager/NS2Manager.csproj` or publish with `dotnet publish src/NS2Manager/NS2Manager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`.

## First-run workflow
1. Open **Dependencies**, click **Check for Updates**, then **Install selected**. Review the sources and the exact commit IDs before installing. Nothing updates automatically.
2. Install Node.js 22+ separately. The manager checks for Node and does not silently install it.
3. In **Session**, choose the host LAN IP address. Click **Start ns2jb (RPC)**. Use your existing SmileBASIC input setup on the console; only the PC terminal is replaced.
4. A successful retained RPC session produces a run directory inside `ns2jb/runs`. Click **Find latest run**; the manager never reuses a previous run automatically.
5. Use **Start OunceCake** with a 16-hex-character title ID and the verified run directory. This runs OunceCake's HTTP/WebDAV server for the specific title. A full installed-title browser is planned, not implemented.
6. Open the read-only content URL in a browser for now. FileZilla is not required. The **Backup selected title** button will be enabled only after a safe, tested directory-index transfer adapter has been implemented.

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
- [ ] Integrated recursive download, progress, resumable verification
- [ ] Automated host and device integration tests

See CHANGELOG.md for changes.
