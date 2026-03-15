# Session Resume Prompt

新开会话时，直接使用下面这段：

```text
继续做 FileExplorer。先看 planning/file-management-task-board.md 和 CHANGELOG.md，基于 main 上的 373d636，继续微调左侧树重命名 overlay 的样式和位置。

重点看 FileExplorerUI/MainWindow.xaml.cs 里的 BeginSidebarTreeRenameAsync 和 EnsureSidebarTreeRenameOverlay。
```

如果后续提交点变化，只需要把上面的提交号替换成最新提交号。
