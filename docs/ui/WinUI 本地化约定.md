# WinUI 本地化约定

## 目标

- 静态界面文本和运行期动态文本分层管理。
- 默认语言跟随系统 UI 语言。
- 调试期允许临时语言覆盖，但不做持久化。
- 资源键命名保持可检索、可扩展。

## 当前实现

- 资源文件：
  - [Strings/en-US/Resources.resw](/D:/Develop/Workspace/Rust/FileExplorer/FileExplorerUI/Strings/en-US/Resources.resw)
  - [Strings/zh-CN/Resources.resw](/D:/Develop/Workspace/Rust/FileExplorer/FileExplorerUI/Strings/zh-CN/Resources.resw)
- 资源读取入口：
  - [LocalizedStrings.cs](/D:/Develop/Workspace/Rust/FileExplorer/FileExplorerUI/LocalizedStrings.cs)
- 调试语言覆盖入口：
  - [App.xaml.cs](/D:/Develop/Workspace/Rust/FileExplorer/FileExplorerUI/App.xaml.cs)

## 当前约束

- `x:Uid` 文本切换依赖窗口创建时的语言上下文。
- 调试语言覆盖必须在 `MainWindow` 创建前应用，否则只会刷新代码侧资源，不能覆盖 `x:Uid`。
- 当前项目不在 `Window` 根节点上挂 `Window.Resources`；如需抽共享资源，优先放到页面内 `Grid.Resources`、应用级资源，或改成独立控件。

## 边界规则

- XAML 静态文本：
  - 默认使用 `x:Uid`
  - 适用于 `Text`、`Content`、`Header`、`PlaceholderText`、`ToolTip` 这类固定文案
- 代码侧动态文本：
  - 使用 `LocalizedStrings.Instance.Get(...)`
  - 适用于状态栏、错误提示、标题拼接、运行期创建的菜单项和列表项
- 不进资源的内容：
  - 协议字符串，如 `shell:mycomputer`
  - 内部命令 id，如 `open-in-terminal`
  - Win32 / DLL / 资源键自身名字
  - 纯技术常量，如 glyph、资源 key、样式部件名

## 命名规则

- 通用命令：`Common...`
- 状态与提示：`Status...`
- 错误：`Error...`
- 对话框：`Dialog...`
- 侧边栏：`Sidebar...`
- 窗口标题：`WindowTitle...`
- 文件类型：`FileType...`
- 驱动器类型：`DriveType...`
- 互操作层异常/错误：`Interop...`

## 语言策略

- 默认语言：
  - 跟随 `CultureInfo.CurrentUICulture`
- 调试覆盖：
  - 仅 `DEBUG` 构建支持 `--lang=...`
  - 不写本地配置，不做持久化
- 非调试版本：
  - 不暴露调试切语言按钮
  - 不读取调试语言启动参数

## 文案要求

- 中文优先使用自然中文，不保留无意义英文占位。
- 英文统一使用自然 UI 文案，避免明显工程直译。
- 同一类文案保持风格一致：
  - 菜单项优先短动词短语
  - 状态提示优先完整短句
  - 错误提示优先 `动作失败：原因`

## 新增文本时的选择标准

1. 如果文本在 XAML 中固定存在，用 `x:Uid`
2. 如果文本需要拼接参数或只在代码运行期出现，用资源键 + `Get(...)`
3. 如果文本只用于内部逻辑判断，不进资源

## 回归检查

- `zh-CN` 默认启动
- `en-US` 调试覆盖启动
- 主窗口标题、toolbar、sidebar、右键菜单、对话框、状态栏是否同步切换
- 长文本是否造成布局挤压或截断异常
