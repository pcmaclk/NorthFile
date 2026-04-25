# NorthFile

[English](README.en.md)

NorthFile 是一个基于 WinUI 3 和 Rust 的 Windows 文件管理器项目。

## 项目结构

- `NorthFileUI/`：WinUI 3 前端目录，工程文件为 `NorthFileUI.csproj`
- `rust_engine/`：通过 FFI 暴露能力的 Rust 后端
- `scripts/`：辅助脚本
- `tools/`：仓库内使用的小型工具

## 环境要求

- Windows
- .NET SDK 8 及以上
- Rust nightly toolchain

## 构建

先构建 Rust 后端：

```powershell
cd rust_engine
cargo build --release
cd ..
```

再构建 WinUI 应用：

```powershell
dotnet build NorthFileUI\NorthFileUI.csproj -c Release -p:Platform=x64
```

本地调试构建：

```powershell
dotnet build NorthFileUI\NorthFileUI.csproj -c Debug -p:Platform=x64
```

## 当前范围

- 标签页工作区
- 双面板浏览壳层
- 侧边栏导航
- 面包屑与搜索
- 文件操作：复制、移动、重命名、删除、粘贴
- 面板级选择状态与命令路由
- WinUI 前端配合 Rust 文件系统能力

## 说明

- 远程仓库当前以代码和工程文件为主。
- 本地规划文档、截图、临时文件和工具目录默认不纳入远程仓库快照。
