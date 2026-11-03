# Changelog

## Unreleased
- Disable **Stop OunceCake** whenever OunceCake is not running. Enable it on process start and disable it after exit or manual shutdown.
- Update the application title and startup message for normal operation. Simplify the runner labels to **Windows** and **WSL** while retaining compatibility with saved settings.
- Rewrite the main README and add a complete build and setup guide under `docs/SETUP.md`.

## Initial development
- Keep **List games (OunceCake)** disabled until the current ns2jb host prints `4/4 verified` and its result file confirms a listening held RPC session; disable it again when the host exits or stops.
- Removed the standalone Title ID field, its saved setting, and the displayed Title ID column; per-game selection retains IDs internally.
- Renamed the independent control to exactly **Stop OunceCake** while preserving the live ns2jb host.
- Fix Windows WebDAV backup file lock: dispose SHA-256 read stream before renaming `.partial` to the completed file.
- Make List games always request OunceCake discovery, ignoring previously stored title ID; allow rediscovery while OunceCake is mounted by restarting only OunceCake.
- Add a dedicated Stop OunceCake control that preserves the verified ns2jb RPC host.
- Fix WebDAV backup destination: use OunceCake's actual `Serving on http://IP:PORT/` endpoint instead of the separately configured Switch IP.
- Replace cramped game list with an installed-title table (name, title ID, storage, Backup action per row).
- A game's Backup action mounts/selects that title, starts or switches only OunceCake when needed, and then downloads read-only WebDAV files to the chosen empty directory.
- Keep ns2jb host alive while switching OunceCake mounts.
- Fixed Stop host processes being clipped by moving actions into two visible rows.
- Bind OunceCake to the current captured ns2jb run, require verified result and live RPC status instead of treating a previous run folder as active.
- Keep Backup disabled until OunceCake confirms its HTTP/WebDAV server is serving; disable it when the server exits.
- Cancel active backup when stopping host processes.
- Fixed WSL runner argument forwarding that could cause `Usage: node run.cjs [computer-IP]` before console connection.
- Removed mandatory Title ID validation for normal OunceCake launch; added a game list captured from OunceCake discovery and an Open selected game button.
- Load nvm-managed Node.js in WSL for dependency preflight and host launch; improve missing Node diagnostics.
- Added WSL (recommended) runner mode; checks Linux Node.js 22+ and launches the retained RPC host through `wsl.exe` without requiring the user to type commands.
- Kept Windows-native runner as available after reports of FS acquisition failures.
- Added root-level `build.bat` to publish a self-contained Windows x64 EXE into `dist/win-x64/`.
- Added Windows .NET 8 desktop GUI for host-side ns2jb and OunceCake workflows.
- Added confirmation-based GitHub dependency checks, staged updates, version tracking, and rollback copies.
- Added process output capture, independent backend paths, and explicit session lifetime handling.
- Added integrated HTTP/WebDAV title-file backup, destination picker, path traversal checks, size verification and SHA-256 manifest.
