# WinUI 内容区多视图与双面板重构计划

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
  - 引入 `WorkspaceLayoutHost`，支持 `Single/Split` 切换。
  - 将命令路由切到 `ActivePanel`。

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

- 标签页显示会话摘要，不显示全部细节。
- 双面板标签头：优先显示 `活动面板目录名 + split 标识`。
- 完整两侧路径放入 tooltip 与面板自身地址栏。

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
- 引入 `WorkspaceLayoutHost`，支持 `Single/Split` 切换。
- 命令路由切到 `ActivePanel`。

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
- 之后再进入双面板与标签页摘要接线，避免在视图基础未稳前继续扩面。
