# NorthFile

[中文](README.md)

NorthFile is a Windows file explorer project built with WinUI 3 and Rust.

## Structure

- `NorthFileUI/`: WinUI 3 frontend directory, using the `NorthFileUI.csproj` project file
- `rust_engine/`: Rust backend exposed through FFI
- `scripts/`: utility scripts
- `tools/`: small internal repository tools

## Requirements

- Windows
- .NET SDK 8+
- Rust nightly toolchain

## Build

Build the Rust backend first:

```powershell
cd rust_engine
cargo build --release
cd ..
```

Then build the WinUI app:

```powershell
dotnet build NorthFileUI\NorthFileUI.csproj -c Release -p:Platform=x64
```

For local debug builds:

```powershell
dotnet build NorthFileUI\NorthFileUI.csproj -c Debug -p:Platform=x64
```

## Current Scope

- tabbed workspace
- dual-pane explorer shell
- sidebar navigation
- breadcrumb and search
- file operations: copy, move, rename, delete, paste
- pane-aware selection and command routing
- WinUI frontend backed by Rust filesystem operations

## Notes

- The remote repository is currently kept focused on source code and project files.
- Local planning notes, screenshots, temporary files, and tooling folders are excluded from the remote snapshot by default.
