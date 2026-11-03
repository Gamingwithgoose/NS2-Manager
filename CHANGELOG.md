# Changelog

## Unreleased — initial foundation
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
- Title ID remains optional for direct launch.
- Load nvm-managed Node.js in WSL for dependency preflight and host launch; improve missing Node diagnostics.
- Added WSL (recommended) runner mode; checks Linux Node.js 22+ and launches the retained RPC host through `wsl.exe` without requiring the user to type commands.
- Kept Windows-native runner as explicitly experimental after reports of FS acquisition failures.
- Added root-level `build.bat` to publish a self-contained Windows x64 EXE into `dist/win-x64/`.
- Added Windows .NET 8 desktop GUI for host-side ns2jb and OunceCake workflows.
- Added user-approved GitHub dependency checks, staged updates, version tracking, and rollback copies.
- Added process output capture, independent backend paths, and explicit session lifetime handling.
- Added experimental integrated HTTP/WebDAV title-file backup, destination picker, path traversal checks, size verification and SHA-256 manifest.
- Installed-title discovery, resume/cancel, and live device testing remain pending.
