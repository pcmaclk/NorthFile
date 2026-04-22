# Session Resume Prompt

新开会话时，直接使用下面这段：

```text
继续做 FileExplorer。先看 docs/planning/file-management-command-architecture.md、docs/planning/file-management-task-board.md、CHANGELOG.md，基于 main 上的最新提交，继续推进文件管理命令层重构和 FM-03。

重点看 FileExplorerUI/MainWindow.xaml.cs、FileExplorerUI/Services/FileManagementCoordinator.cs、FileExplorerUI/Services/ExplorerService.cs。

当前状态：
- create / rename / delete / copy / cut / paste 已开始统一到 Execute... 命令层
- toolbar 和列表右键菜单已接单选 copy / cut / paste
- CanCreate / CanRename / CanDelete / CanCopy / CanCut / CanPaste 已开始统一驱动入口状态

下一步优先：
1. 继续收上下文目标模型（列表项 / 背景 / 当前目录）
2. 再补快捷键
3. 然后继续 FM-03 的冲突处理和 paste 后同步
```

如果后续提交点变化，只需要把上面的“最新提交”换成新的提交号，或补一句“先看 git log -1”。
