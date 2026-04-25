# NorthFile

NorthFile is a Windows file explorer project built with WinUI 3 and Rust.

## Structure

- `FileExplorerUI/`: WinUI 3 frontend, C#, .NET 8, x64
- `rust_engine/`: Rust backend exposed through FFI
- `scripts/`: utility scripts
- `tools/`: small internal code tools

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
dotnet build FileExplorerUI\FileExplorerUI.csproj -c Release -p:Platform=x64
```

For local debug builds:

```powershell
dotnet build FileExplorerUI\FileExplorerUI.csproj -c Debug -p:Platform=x64
```

## Current Scope

- tabbed workspace
- dual-pane explorer shell
- sidebar navigation
- breadcrumb and search
- file operations: copy, move, rename, delete, paste
- pane-aware selection and command routing
- WinUI frontend with Rust-backed directory/file operations

## Notes

- The repository is intentionally kept code-focused.
- Local planning notes, detailed docs, screenshots, and temporary files are not part of the remote repository snapshot.
