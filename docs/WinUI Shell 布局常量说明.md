# WinUI Shell 布局常量说明

## 目的

把标题栏、工具栏、sidebar、settings shell 里反复出现的布局数值集中到少数几个入口，避免后续继续靠全局搜索 `32 / 40 / 42 / 48 / 8 / 12` 来微调。

## 当前入口

主窗口 shell 常量：
- `FileExplorerUI/MainWindow/MainWindow.Fields.cs`
- `FileExplorerUI/MainWindow/MainWindow.LayoutProperties.cs`
- `FileExplorerUI/MainWindow.xaml`

sidebar 常量：
- `FileExplorerUI/SidebarView.xaml`
- `FileExplorerUI/SidebarView.xaml.cs`

## MainWindow 侧已收口的值

这些值现在定义在 `MainWindow.Fields.cs`，并通过 `MainWindow.LayoutProperties.cs` 暴露给 `MainWindow.xaml` 的 `x:Bind`：

- `ShellWindowHorizontalPadding`
- `ShellTitleBarHeightValue`
- `ShellControlSizeValue`
- `ShellGlyphSizeValue`
- `ShellTitleBarLeftInsetWidthValue`
- `ShellToolbarBottomSpacing`
- `ShellStatusBarHeightValue`
- `ShellSplitterWidthValue`
- `SettingsNavigationCompactPaneLengthValue`

对应的 XAML 绑定点包括：
- 根窗口 `Padding`
- 标题栏 `TabView` 和 settings drag region 高度
- 标题栏左侧设置按钮容器宽度
- 标题栏和工具栏按钮宽高
- 标题栏和工具栏图标字号
- 工具栏外边距和内边距
- 地址栏、搜索栏高度
- 底部状态栏高度和状态文本边距
- sidebar splitter 宽度
- settings `NavigationView` 的 `CompactPaneLength / OpenPaneLength`

## Sidebar 侧已收口的值

sidebar 目前分两层：

XAML 资源：
- `SidebarExpandedScrollPadding`
- `SidebarFooterMarginExpanded`
- `SidebarItemContentInsetLeftMargin`
- `SidebarItemHeight`
- `SidebarItemGlyphSize`
- `SidebarItemContentInsetLeft`
- `SidebarDragOverlaySize`
- `SidebarDragOverlayGlyphSize`

代码常量：
- `SidebarItemHeight`
- `SidebarItemGlyphSize`
- `SidebarItemContentInsetLeft`
- `SidebarExpandedScrollRightPadding`
- `SidebarBottomPadding`
- `SidebarFooterBottomMargin`
- `SidebarCompactButtonSize`

这些值已经统一到：
- 分组项模板
- 分组头图标尺寸
- compact 按钮布局
- 动态创建的 sidebar 项
- footer 设置按钮
- pinned 拖拽预览
- tree host 的右侧 padding

## 当前确认的规则

标题栏和工具栏：
- 标题栏高度与 settings drag region 高度保持一致
- 标题栏按钮、工具栏按钮、地址栏、搜索栏都共享 `32px` 控件高度基准
- 标题栏左侧设置按钮占位宽度独立保留，避免再和 tab 宽度逻辑耦合

sidebar：
- 展开态右侧滚动区留白和底部留白固定
- compact 按钮尺寸与展开态项高保持同一基准
- 自定义项的左侧选中标志和 `TreeViewItemSelectionIndicatorForeground` 保持一致
- 分组项内容缩进单独集中，避免模板项和代码动态项偏移不一致

settings shell：
- `NavigationView` 的 `CompactPaneLength` 与主 shell compact 宽度概念分开维护
- `OpenPaneLength` 目前仍直接跟 sidebar 默认展开宽度一致

## 已确认的边界

下面这个点目前不要强行收口：

- `TeachingTip.PlacementMargin`

实测把它改成 `x:Bind` 会触发 WinUI XAML 编译器失败。现阶段保留硬编码 `8` 更稳。

## 最小回归清单

这套 shell 布局后续每次调整后，至少检查下面这些点：

主题切换：
- 浅色和深色下，标题栏、工具栏、内容区层级关系正常
- sidebar tree、自定义 sidebar 项、右侧列表的左侧选中标志保持一致

shell 切换：
- explorer 模式下显示标题栏 tab、工具栏、sidebar
- settings 模式下保留标题栏拖拽区，只隐藏 tab 内容
- settings 左侧按钮、explorer 左侧按钮显示逻辑正常

sidebar 状态：
- expanded 模式下分组头、分组项、tree 缩进关系正常
- compact 模式下按钮与内容区之间的间距正常，且与标题栏/工具栏基准不冲突
- compact 和 expanded 切换后，footer 设置按钮位置不跳

窗口状态：
- 窗口激活和失活时，选中态仍可区分 active/inactive
- 主题切换后，自定义 indicator 不会卡在旧 brush

构建与运行：
- `dotnet build FileExplorerUI/FileExplorerUI.csproj -c Debug -p:Platform=x64`
- 应用能正常启动到主窗口
- 如果构建失败先查是否有运行中的 `NorthFile.exe` 锁住输出

## 本轮结论

本轮已经人工检查过：
- 浅色/深色
- explorer/settings 切换
- sidebar compact/expanded
- 主要选中标志一致性

## 范围说明

这份文档只覆盖主窗口 shell 的布局常量收口：
- 标题栏
- 工具栏
- explorer sidebar
- settings shell

下面这些内容不属于这轮收口范围：
- `UITest/`
- `FileExplorerUI.slnx` 中与 `UITest` 相关的本地引用调整
- 独立 UI 自动化或截图对比脚本

当前 `UITest` 只是独立测试 UI 项目，后续如果要补自动化验证，应单独演进，不要和 shell 布局常量提交混在一起。

## 后续建议

如果后面继续整理，可以优先按这个顺序：

1. 把 shell 常量按“标题栏 / 工具栏 / 状态栏 / settings shell / sidebar”分段注释。
2. 评估 `SidebarView.xaml` 和 `SidebarView.xaml.cs` 是否还要再合并成单一来源，减少同名常量双份维护。
3. 如果后面继续补自动化，再考虑把这份最小回归清单映射到独立的 UI 验证项目，而不是先把 shell 改动和 `UITest` 绑定在一起。
