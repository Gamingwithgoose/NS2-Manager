# Changelog

## Unreleased — initial foundation
- Load nvm-managed Node.js in WSL for dependency preflight and host launch; improve missing Node diagnostics.
- Added WSL (recommended) runner mode; checks Linux Node.js 22+ and launches the retained RPC host through `wsl.exe` without requiring the user to type commands.
- Kept Windows-native runner as explicitly experimental after reports of FS acquisition failures.
- Added root-level `build.bat` to publish a self-contained Windows x64 EXE into `dist/win-x64/`.
- Added Windows .NET 8 desktop GUI for host-side ns2jb and OunceCake workflows.
- Added user-approved GitHub dependency checks, staged updates, version tracking, and rollback copies.
- Added process output capture, independent backend paths, and explicit session lifetime handling.
- Added experimental integrated HTTP/WebDAV title-file backup, destination picker, path traversal checks, size verification and SHA-256 manifest.
- Installed-title discovery, resume/cancel, and live device testing remain pending.
