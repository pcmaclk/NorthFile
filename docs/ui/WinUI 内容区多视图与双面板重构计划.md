# WinUI 内容区多视图与双面板重构计划

## 当前进度（2026-04-12）

- 双面板当前已进入“真实 pane 行为收口”阶段，不再只是壳层骨架：
  - 次 pane 已接入真实 entries surface、真实地址栏/搜索栏状态、独立返回/前进/上级/刷新命令。
  - pane-local 文件命令已统一进入 `PaneFileCommandController` / pane core，不再继续保留“主 pane 本地实现、次 pane 特判补丁”的长期形态。
- pane 壳层状态与导航通知开始收敛到共享 helper：
  - `工具栏 / 内容区 / 输入框 / 前景色 / 边框 / 阴影层级` 的 active/inactive 视觉属性，当前已统一走 `WorkspacePanelId -> shell helper` 查询，不再在主/次 pane 属性里各自拼条件。
  - 次 pane 的 `地址栏 / 面包屑 / 搜索 / 可导航状态 / 占位态` 属性通知名单，当前也开始收敛到共享 raising 入口，后续继续统一主 pane 导航链时不需要再复制一整串 `nameof(...)`。
- 次 pane 的 details 滚动分页链当前已打通：
  - `SecondaryEntriesScrollViewer.ViewChanged` 已接入与主 pane 同一套 viewport 调度入口。
  - 次 pane 已支持滚动到阈值后按 `readRange` 结果集窗口继续追加下一页，而不是每次整页重载。
  - `System32` 级别大目录场景下，`secondary-append.begin/end` 已能稳定触发，追加后继续滚动未再复现之前的 WinUI 崩溃链。
- 双面板 active / 焦点路由已补齐两层保护：
  - 第一层是交互入口显式切 active：次 pane 的工具栏、地址栏、搜索栏、内容区背景、行项、列头、右键背景、双击项等入口都会先切到 `SecondaryPane`。
  - 第二层是命令执行阶段按 `EntriesContextOrigin` 再同步一次 active surface：
    - `SecondaryEntriesList` -> `SecondaryPane`
    - `SidebarPinned / SidebarTree` -> `Sidebar`
    - 其他 entries context -> `PrimaryPane`
  - 这样即使未来某个 pointer 入口样式或结构调整，右键菜单与快捷键链也不会轻易把命令打回主 pane。
- 当前对双面板交互的一条明确约束：
  - “视觉激活”和“命令命中 pane”必须保持同源，不允许只改边框/背景而不改 `SelectionSurface` / `ActivePanel`。
  - 任何新接入的 pane 级交互入口，如果会改变用户接下来快捷键、右键、地址栏编辑的目标 pane，就必须在入口处显式切 `SetActiveSelectionSurface(...)`，或者在命令 origin 层补对应映射。
- 当前阶段结论：
  - 双面板已从“壳层重构”进入“行为一致性收口”阶段。
  - 后续继续重构 `PanelHost / 导航栏 / entries host` 时，必须保留这套 active 路由约束和次 pane 分页模型，不能退回到“主 pane 保状态、次 pane 整页重载”的旧模式。
- 对应回归入口：
  - 见 [双面板行为回归清单](./双面板行为回归清单.md)。

## 当前进度（2026-04-06）

- 壳层 UI 重构阶段已收口：
  - `MainWindow` 已切到明确的 `LeftShellColumn / RightShellColumn` 骨架。
  - sidebar 顶部按钮区、标题栏 tab strip、toolbar、workspace、status bar 都已回到稳定的列内结构。
  - 旧的隐藏 `NavigationView` sidebar 兼容层已移除，不再保留“看不见但还在驱动状态”的旧壳。
- 双面板 UI 骨架已具备最小可用形态：
  - workspace 当前支持 `Single / SplitVertical` 两种壳层布局。
  - 主 pane 与次 pane 都已有独立 toolbar 区和内容区边界。
  - 中间 action rail 已预留，将来可继续放 pane 级命令或拖拽操作。
- 双面板当前已落地的最小 pane-local 状态：
  - `Secondary` panel 已有独立 `CurrentPath / AddressText / QueryText / Sort / Group / ViewMode / SelectedEntryPath` 状态字段。
  - 次 pane 的地址栏与搜索栏当前已接入自己的状态写回，不再是完全写死的占位文本。
  - pane 激活状态当前会驱动边框、背景和内容区阴影。
- 双面板当前仍然刻意没有做的部分：
  - 次 pane 还没有接第二套真实 entries surface。
  - pane-local 返回/前进/上级/刷新/搜索命令路由还没有真正切到 `ActivePanel`。
  - 主 pane 的真实数据与视图宿主还没有进一步抽成独立 `PanelHost + entries host` 组合。
- 当前阶段结论：
  - UI 壳层重构已经可以视为完成，可以停止继续打磨骨架样式。
  - 下一阶段应切到新分支，开始做 pane-local 命令路由和第二面板真实功能，而不是继续扩散 UI 结构改动。

## 当前进度（2026-03-28）

- 列表模式已从多层 `ItemsControl` 分列实现迁移到 `ItemsRepeater + 自定义 VirtualizingLayout`：
  - `GroupedEntriesColumnsView` 已移除，列表模式当前直接绑定 `_entries`。
  - 新增 `GroupedListRepeaterLayoutProfile`、`GroupedListVirtualizingLayout`、`GroupedRepeaterEntriesViewHost`。
  - 分组头与分组项都作为同一条 repeater 数据流中的真实 item 渲染，不再先投影成“列集合 + 列内 items”。
- 列表模式 resize 与首帧排布已做第一轮收口：
  - 切换到列表模式后会立即请求一次布局刷新。
  - `GroupedEntriesScrollViewer` 尺寸变化会直接触发 repeater 重新测量。
  - 列表整体 extent 改为按所有可见元素的最大 `bottom/right` 计算，避免最后一列不满时底部留空。
- 键盘列导航已切到基于当前 `_entries` 和 `rowsPerColumn` 的即时列投影，不再依赖旧的 `_groupedEntryColumns`。
- 当前分支结论：
  - 列表模式的主问题已经从“多层 `ItemsControl` 的排布和重建抖动”转到后续可继续优化的阈值/刷新策略问题。
  - 窗口 resize 时边缘透明条已通过空白项目复现，确认不是本项目自定义内容树或列表迁移引入的问题，本分支不继续处理这条系统层现象。

## 当前进度（2026-03-20）

- Phase 1 已完成：
  - `Workspace` 基础状态模型已落地（布局模式、面板标识、面板状态、壳层状态）。
  - 视图宿主接口 `IEntriesViewHost` 已落地。
  - `MainWindow` 已接入最小 `WorkspaceShellState` 初始化（行为未改动）。
- Phase 2 已完成当前目标：
  - `EntriesPresentationBuilder` 已落地，并接管条目插入排序与展示构建的首批逻辑。
  - 现有列表内容区已包装为 `ExistingListEntriesViewHost`。
  - 双击、右键命中、背景命中、预览按下命中已通过 host 解析，`MainWindow` 删除了对应的 `ListViewItem` 命中细节代码。
- Phase 3 待开始：
  - 引入 `WorkspaceLayoutHost`，先完成单窗口单面板下的壳层抽象。
  - 将命令路由切到 `ActivePanel`。
  - `Split/Tab` 当前阶段只保留状态模型与接口边界，不进入实际 UI 接线。

## 当前进度（2026-03-23）

- 详情模式大目录滚动链已切到 `ItemsRepeater + VirtualizingLayout + readRange(startIndex, count)`。
- 当前目录浏览结果开始按“结果集窗口”而不是“目录分页 UI 特判”收口。
- 详情模式首屏和后续区间读取的 `size / modified` 已改为优先随 Rust 批量结果返回；这条主路径不再依赖 C# 对当前块逐项 `FileInfo/DirectoryInfo` 回填。
- 后续 `List / GroupedColumns / Search / GlobalIndex` 需要继续沿同一结果集契约接入，而不是再各自补一套 metadata 延迟链。

## 目标

- 将内容区从 `MainWindow` 中解耦，形成可复用的工作区组件。
- 支持未来的多视图（详情、列表、分组列、后续 Tiles/Gallery 等）。
- 支持标签页与双面板共存：`Tab = 会话边界`，`Split = 会话内部布局`。
- 保持现有命令体系（右键、快捷键、toolbar、can-execute）一致，不重写一套。

## 核心原则

1. 数据语义与布局语义分离
- 禁止使用“占位项假数据”驱动布局。
- 分组列由真实列容器实现，不通过扁平列表补齐模拟。

2. 壳层与内容区分离
- `MainWindow` 只负责壳层（标题栏、侧栏、导航、全局状态、命令路由）。
- 内容区由 `WorkspaceHost` 承担。

3. 命令统一路由
- 所有命令入口统一路由到 `ActivePanel`。
- 右键/快捷键/工具栏不直接依赖具体控件类型。

4. 可扩展视图契约
- 通过 `IEntriesViewHost` 适配不同视图实现。
- 新视图接入只实现契约，不改壳层逻辑。

## 状态模型

- Window
  - TabHost
    - TabSession
      - LayoutMode: `Single` / `SplitVertical` / `SplitHorizontal`
      - ActivePanel: `Primary` / `Secondary`
      - PanelState(Primary)
      - PanelState(Secondary, 可空)

`PanelState` 建议最小字段：
- `CurrentPath`
- `QueryText`
- `SortField`
- `SortDirection`
- `GroupField`
- `ViewMode`
- `SelectedEntryPath`

## UI 结构

- `MainWindow`
  - Sidebar（先全局一份）
  - `WorkspaceLayoutHost`
    - Single: `PanelHost`
    - Split: `PanelHost | Splitter | PanelHost`

`PanelHost` 内部：
- 面板级导航按钮（后退/前进/上级/刷新）
- 地址+搜索合并栏（每面板独立）
- 内容视图区（`IEntriesViewHost`）
- 可选面板状态摘要

## 标签页与双面板关系

- 标签页与双面板相关设计先保留为后续规划，不属于当前单窗口稳定阶段的实现范围。
- 当前阶段只保留会话摘要与 split 相关的状态模型，不接实际 UI。

## 菜单与命令入口

- 全局菜单栏降级为低频入口（右上角 `...`）。
- 高频操作以右键 + 快捷键为主。
- 边缘浮出菜单可作为可选增强能力，不作为唯一入口。

## 迁移计划

### Phase 1（当前）
- 落地契约与状态骨架（不改变现有行为）。
- 新增 `Workspace` 相关模型和接口。
- 在 `MainWindow` 中接入最小骨架字段，作为后续迁移落点。

### Phase 2
- 抽离内容展示构建器（排序/分组/视图结构输出）。
- 将现有列表视图包装为第一个 `IEntriesViewHost` 实现。

### Phase 3
- 引入 `WorkspaceLayoutHost`，先完成单窗口单面板场景下的内容区宿主抽象。
- 命令路由切到 `ActivePanel`。
- 双面板与标签页当前只保留框架、状态模型和接口，不进入实际 UI 实现。

### Phase 4
- 增加第二视图实现（分组列或新列表实现）。
- 标签页会话摘要接入。

### Phase 5
- 回归与性能收口（大目录、双面板、分组、搜索并发场景）。

## 风险与控制

- 风险：迁移期间事件命中与选择行为回归。
- 控制：
  - 每阶段只做单一职责改动。
  - 维持 build + 启动验收。
  - 保持旧路径可回退，避免一次性替换。

## 当前进度（2026-03-21）

- Phase 2 已完成：
  - `EntriesPresentationBuilder` 已落地，内容区展示构建开始从 `MainWindow` 抽离。
  - 现有列表已通过 `ExistingListEntriesViewHost` 接入 `IEntriesViewHost`，命中解析不再直接依赖 `MainWindow` 内联的 `ListViewItem` 查找代码。

- Phase 3 已完成当前单面板迁移：
  - 新增 `WorkspaceLayoutHost`，`MainWindow` 当前通过它访问 `ActivePanel` 与 panel state。
  - 现有内容区展示状态同步已从硬编码 `Primary` 改为经由 `ActivePanel` 读写，为后续 split 布局接线留出稳定边界。

- 内容布局、排序和分组第一版已可用：
  - 背景右键中的“查看 / 排序方式 / 分组方式”已接入真实命令。
  - “查看”支持 `详细信息 / 列表` 两种模式切换。
  - “排序方式”支持 `名称 / 修改日期 / 类型 / 大小`，并支持 `升序 / 降序`。
  - “分组方式”支持 `无分组 / 名称 / 类型 / 修改日期`。
  - 分组头已支持展开/折叠，且与普通文件项命中、选择、右键路径隔离。
  - 三套右键菜单中这批高频项已接入真实逻辑并统一到共享分发链：`Open / Paste / New File / New Folder / Copy Path / Open in Terminal / Properties`。
  - 底部 command bar 与顶部菜单项现在都复用同一套 target 解析和 command catalog 可用性判断，`详细信息 / 列表 / 分组列表` 下行为保持一致。
  - 当前已新增最小 `InlineEditCoordinator`，并先接入三类已有编辑会话：`右侧内容区重命名 / 左侧树重命名 / 地址栏编辑`。
  - 外部点击、失焦、窗口失活、切换到另一类编辑时，都会先通过统一会话边界处理，而不是继续在各编辑点分散维护一套焦点逻辑。
  - 文件操作快捷键也已开始统一到窗口级分发入口：当前已收口 `F2 / Delete / F5 / Ctrl+C / Ctrl+X / Ctrl+V / Ctrl+L`，并对地址栏、搜索框、重命名输入框等文本输入场景做了抢键抑制。

- 内容区公共组件与自定义宿主已完成第一轮收口：
  - 新增 `EntryNameCell`，统一 `图标 + 名称 + 基础间距`。
  - 新增 `EntryGroupHeader`，统一分组头标题、计数和展开图标。
  - 新增 `EntryItemMetrics` / `EntryViewDensityMode`，统一项高度、图标尺寸、组头高度等尺寸令牌。
  - 新增 `EntryItemHost`，承接最小 `ListViewItemPresenter` 语义：`selected / pointer over / pressed / selection indicator / content presenter`。
  - 详细模式和列表模式都已切到自定义内容宿主，不再由原生 `ListView` 承担实际显示。

- 当前可用状态（单面板）：
  - 详细模式基础体验已基本稳定：
    - 列宽调整可用，最右列 splitter 可拖动。
    - 选中态、悬停态、左侧选中指示已统一到 `EntryItemHost`。
    - 详细分组已完成样式收口：组头无下边框、组间距生效、分组项对齐已调整。
  - 列表模式和分组列模式已可用，但后续仍需要继续收口交互和样式一致性。
  - 重命名浮层当前已完成一轮交互收口：
    - 锚点改为名称文本区域，不再锚整行。
    - 滚动、点击内容区其他位置、窗口失活时会自动消失。
    - 方向键、向上按钮等外部操作会先关闭重命名，再继续执行主交互。
    - 输入框宽度已放宽到不再受名称列宽约束，而是受内容区可用宽度约束。
  - 排序当前已补回“文件夹优先”规则：
    - 在名称、类型、大小、修改日期等排序下，文件夹都优先于文件。

## 当前补充计划（2026-03-21）

- 为后续“改变大小 / 紧凑模式 / 统一样式调整”做准备，内容区需要继续去重：
  - 先抽公共项内容组件 `EntryNameCell`，统一 `图标 + 名称 + 基础间距`。
  - 先抽尺寸令牌 `EntryItemMetrics`，把行高、图标列宽、图标字号、图标文本间距、名称字号等从散落常量收口。
  - 第一阶段先替换 `详细信息`、`列表`、`列表分组` 三处文件项内容，不改组头模板。
  - 第二阶段再抽公共组头组件 `EntryGroupHeader`。
  - 第三阶段再统一重命名定位、键盘导航、拖放命中等与视图宿主耦合较深的路径。

## 公共项组件方案

### 目标

- 样式改动不再需要在 `详细信息 / 列表 / 分组列表` 三套模板里各改一次。
- 为以后增加 `小 / 中 / 大 / 紧凑` 等尺寸模式保留统一入口。
- 保持现有命令、选择、右键与排序/分组逻辑不变。

### 第一阶段范围

- 新增 `EntryItemMetrics`
  - 作为内容区项的统一尺寸令牌对象。
  - 第一批字段至少包括：
    - `RowHeight`
    - `IconColumnWidth`
    - `IconFontSize`
    - `NameFontSize`
    - `IconTextSpacing`
    - `NameTrailingSpacing`

- 新增 `EntryNameCell`
  - 只承载文件项公共内容：
    - 图标
    - 名称文本
    - 基础间距与字号
  - 外层布局差异仍由宿主模板控制：
    - 详细信息模式决定列定义与类型/大小/修改日期位置
    - 列表模式决定单列/多列排列

### 暂不处理

- 组头模板统一
- 重命名浮层在自定义列表宿主中的重新锚定
- 列表宿主虚拟化优化

## 下一步建议（提交点之后）

- 继续收口列表模式与详细模式之间的交互一致性：
  - 键盘导航
  - 重命名锚定与滚动到可见
  - 拖放与命中路径
- 再接真正可切换的 `Compact / Normal / Large` 密度模式。
- 在单窗口单面板稳定前，不进入双面板与标签页 UI 实现；当前只保留接口与状态边界。

## 当前进度（2026-03-22）

- 内容区重构当前已完成的部分：
  - `MainWindow` 与内容展示之间已经通过 `IEntriesViewHost` 解耦，命中、滚动到可见、重命名锚点查找不再直接依赖单一 `ListView`。
  - 详细模式与列表模式都已切到自定义内容宿主；原生 `ListView` 不再承担主要显示职责。
  - 公共组件已落地并开始稳定复用：
    - `EntryItemHost`
    - `EntryNameCell`
    - `EntryGroupHeader`
    - `EntryItemMetrics`
  - 详细模式当前已基本收稳，已完成：
    - 列头与项对齐
    - 分组头与分组项样式收口
    - 最右列 splitter 可拖动
    - 选中态、悬停态、左侧指示统一
    - 键盘上下/Home/End/Enter/F2 基础可用
  - 列表模式与分组列表模式当前已完成：
    - 分组列专用宿主
    - 基础命中、右键、选择、双击打开
    - 样式与详情模式开始共用公共组件
  - 旧 `ListView` 依赖已在这一轮基本移除：
    - 旧 `ExistingListEntriesViewHost` 已删除
    - 代码中对 `EntriesListView.SelectedItem`、隐藏 `ListView` 视口、旧 `ListView` 事件的行为依赖已清空
    - 分页/元数据预取、滚动到可见、菜单透传已经迁回真实可见宿主
    - 三套文件区右键 flyout 已从隐藏 `ListView` 挪到独立资源宿主，不再依赖废弃控件壳
  - 交互一致性这轮已进一步收口：
    - 列表分组模式已真正接入 `GroupedColumnsEntriesViewHost`
    - 列表/分组列表/详情三条宿主线的双击、右键、重命名入口语义继续对齐
    - 组头命中规则已统一，分组头不再误落为背景或文件项目标
    - 列表模式 `Home / End / PageUp / PageDown` 已按列投影规则稳定工作
    - 类型分组下已恢复“文件夹组优先”

- 这轮导航/切换性能优化已完成的结论：
  - 已新增导航阶段埋点，日志位置：
    - `FileExplorerUI/bin/Debug/net8.0-windows10.0.19041.0/navigation-perf.log`
  - 已确认目录进入慢点主要不在目录读取，而在首帧布局/渲染。
  - 已完成并保留的优化：
    - 去掉进入目录前的重复可读性预读
    - 引入展示切换快路径，纯视图切换尽量复用现有展示源
    - 引入列表/分组列投影缓存，减少详情/列表切换时的重复建模
    - 首屏分页改为“当前视口数量 + 首帧后补剩余已读项”
    - 首帧元数据请求改为延后，不再与首帧竞争
  - 已验证无效并明确回退的尝试：
    - “首屏完全不放 placeholder，只直接绑定真实项”会明显拉高 `first-frame`，已回退。
  - 当前较优策略已确认：
    - 首屏保留轻量 placeholder 骨架
    - 先回填当前视口真实项

## 当前进度（2026-03-23）

- 详情模式的大目录滚动链路已继续收口：
  - 详情区当前已经切到 `ItemsRepeater + VirtualizingLayout`，不再是 `ScrollViewer + ItemsControl` 的整树渲染路径。
  - 导航进入新目录时会显式重置内容区视口，不再继承上一层滚动位置。
  - 详情区远距离拖动滚动条时，数据请求已改为“最终视口优先”，不再在拖动过程中顺序补齐前序页。
  - 详情元数据请求已延后且可合并，当前视口优先，降低了 `System32` 一类大目录的滚动卡死风险。

- 当前实现与原规划的差异已经明确：
  - 详情模式已经有真正的 `VirtualizingLayout`；列表模式与分组列模式当前仍是自定义宿主，但还没有统一到同一套布局引擎。
  - 当前滚动条长度已经可以基于“逻辑总条目数”计算，但远距离跳转的数据访问仍依赖后端目录快照分页，不是完全独立于数据加载的纯布局问题。
  - 当前代码已不再使用“可视树级 placeholder 假项”撑满整棵列表，但数据层仍保留轻量 placeholder/稀疏页回填策略，用于保证远跳时宿主可以立即 realize 当前视口。
  - 内容区读取链当前已开始从“目录分页接口”向“结果集接口”收口：
    - `MainWindow` 不应长期直接围绕 `cursor + limit + read directory` 建模。
    - 后续应统一为 `ResultSet + ReadRange(startIndex, count)`，这样目录浏览、目录搜索、未来全局索引命中结果都能接进同一条滚动/虚拟化宿主链。

- 对原规划需要补充的点：
  - Phase 4 之后需要单独加一个“统一虚拟布局层”阶段，而不是只写“增加第二视图实现”：
    - 目标是让 `Details / List / GroupedColumns / 后续新布局` 都复用同一套 `ItemsRepeater host + layout profile + density token`。
    - `Compact / Normal / Spacious` 切换应由统一 layout profile 驱动，而不是每个视图各自改模板常量。
  - 需要把“真实大目录滚动”写成显式验收项：
    - `1000+ / 5000+ / 10000+` 项目录进入、滚轮滚动、拖动滚动条、视图切换都要做回归。
    - 验收标准应包含“不卡死”“不整屏空白”“滚动位置正确”“进入新目录回到顶部”。
  - 需要把“前台视口优先、详情异步补齐、过时请求可取消”补进内容区契约，避免后续新布局回退到先补全数据再出图的旧路径。
    - 首帧后再补剩余首批真实项
  - 需要补一个“结果集抽象层”小阶段，位置应在“统一虚拟布局层”之前：
    - 目标：把当前目录浏览先包装为 `DirectoryResultSet`，对上层只暴露 `totalCount + readRange(startIndex, count)`。
    - 目录搜索随后接成 `SearchResultSet`。
    - 未来 Everything 风格的本地索引/全局索引命中结果接为 `GlobalIndexResultSet`。
    - 这样 `IEntriesViewHost` 和滚动宿主只依赖“有序、可随机访问的结果集”，不依赖具体后端是目录快照、MFT 索引还是本地数据库。

- 当前阶段判断：
  - 内容区重构主线已经从“搭骨架”进入“收口与稳态”阶段。
  - 如果按目标拆分，当前大致进度可认为是：
    - 内容区宿主与公共项组件重构：约 `70%~80%`
    - 详细模式：约 `85%~90%`
    - 列表/分组列表模式：约 `60%~70%`
    - 双面板/标签页：当前阶段仅保留框架与接口，尚未进入 UI 实现
  - 当前未完成但已降级为收尾项的主要内容：
    - 列表模式与详情模式的更细交互一致性（滚动到可见、重命名前后视口细节）
    - 排序/分组规则和系统行为的进一步对齐
    - 文档与阶段边界的持续更新

### 2026-03-23 晚间补充

- 详情模式滚动链路这轮已经进一步收口为“视口直读”模型：
  - `ViewChanged` 不再先依赖旧的顺序分页语义判断“要不要读”。
  - 当前实现已经统一走 `EnsureDataForViewportAsync(start, end)`。
  - 只要当前视口范围未加载，就直接读取当前位置对应块。
- 详情模式的小跳 / 大跳策略已明确分流：
  - 小跳、滚轮、相邻块移动：
    - 若当前块或相邻块已命中，直接同步读当前块或只预读下一块。
  - 大跳、拖动滚动条：
    - 直接按当前位置块读取，不再先顺序补齐前序页。
    - 同步读取已收口为“同块或相邻块”，避免拖动 thumb 时 UI 线程迟滞。
- 当前 `ItemsRepeater + VirtualizingLayout` 已经满足：
  - `System32` 一类 4000+ 项目录可以稳定进入并滚动，不再出现整树构建导致的失去响应。
  - 第一次远拖时会按视口范围直接触发 sparse 读取，不再等松手后才开始出内容。
  - 预读已收紧为“接近已加载块尾部”时才触发，避免无效预读干扰。
- 当前仍保留的后续方向：
  - 列表/分组列模式继续收口到同一套 layout profile / result set 语义。
  - 首屏冷加载继续向“首屏优先，后台补全整目录快照”演进。

## 后续保留优化点（性能）

- 以后若继续收导航首帧性能，优先看这两点：
  - 压低 `placeholders-synced` 的偶发抖动，减少首屏 placeholder 骨架同步带来的波动。
  - 将首帧后补入的剩余真实项继续拆成更小批次，降低第二次可感知顿挫。

- 这两点暂不在当前阶段继续推进，避免偏离“内容区重构与交互收口”的主任务。

## 当前进度（2026-04-20）

- 双面板与标签页主线已经进入收尾验证阶段：
  - 每个标签页持有自己的主/次 pane 状态，包括路径、双面板开关、active pane、列宽、视口与数据会话。
  - 主/次 pane 共用 pane 数据加载协调器、命令路由、刷新/粘贴/删除/移动后的局部收尾逻辑。
  - 次 pane 已补齐分页/视口模型，不再依赖“清空后整页恢复”的旧刷新模型。
  - 主/次 pane 都有独立地址栏、搜索栏、toolbar、内容区、状态栏和关闭按钮。
  - 中间操作区已支持方向明确的复制选中、复制全部、移动选中、移动全部、路径同步和面板互换。

- 当前 UI 行为边界：
  - 地址栏保持“展示态 breadcrumb + 编辑态 TextBox”两段式。
  - 地址栏只有 `Enter` 提交导航；点击外部或 `Escape` 取消编辑。
  - 点击 breadcrumb 项本身只执行 breadcrumb 导航；点击地址栏右侧预留空白才进入编辑态。
  - 双面板窄宽度下，主/次地址栏右侧固定预留透明命中区，避免空白点击区过小。
  - active / inactive 视觉状态由 XAML theme resource 承担，代码只切 pane 状态和数据。

- 当前数据一致性规则：
  - pane 的状态栏数量必须和当前过滤后的可见项一致。
  - 当后端分页结果仍有更多页时，pane 可以暂用后端总数维持虚拟滚动。
  - 当分页结果已经没有更多页时，pane 总数必须回落到已过滤后的可见项数，避免隐藏/系统项导致主/次 pane 状态栏不一致。
  - 文件操作产生的局部增删优先就地更新目标 pane；只有局部收尾失败时才触发 pane 级重载。

- 2026-04-20 MCP 回归结论：
  - Debug x64 构建通过。
  - 双面板启动、标签切换、主/次刷新、双状态栏和中间操作按钮渲染通过。
  - `D:\New Folder\asdfjj123` 临时文件刷新回归通过，测试文件已清理。
  - `D:\ | D:\` 双面板标签下主/次 pane 状态栏已保持一致。

## 当前进度（2026-04-23）

- 多选与 pane 命令路由继续收口：
  - 主/次 pane 的选择状态现在按 pane 保存，点击另一 pane 时会清空前一 pane 的选中并切换 active pane。
  - 复制、剪切、删除和中间操作区的复制选中 / 移动选中都应读取 pane-local 多选集合。
  - 中间操作区的复制全部 / 移动全部只改变文件操作来源和目标，不主动切换 active pane。

- sidebar 与收藏导航已改为 active pane 语义：
  - tree 和收藏项导航不再固定作用于主 pane，而是作用于当前 active pane。
  - active pane 切换或 pane 路径变化时，sidebar tree 选中项需要同步到当前 pane 路径。
  - 设置页新增 `路径变化时展开侧边栏树` 开关；开启后会展开到当前路径并滚动到对应项，关闭时只同步选中状态。

- 设置页与标签栏视觉边界：
  - 进入设置页时隐藏 explorer 标签栏，避免设置页仍显示文件浏览标签。
  - active 标签补充与 toolbar 一致的阴影层级，继续保持 tab 视觉由 XAML/template 资源驱动。
