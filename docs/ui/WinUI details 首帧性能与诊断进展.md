# WinUI details 首帧性能与诊断进展

Last updated: 2026-03-24

## 背景

这一轮工作不再解决目录数据获取本身，而是聚焦 WinUI details 视图在首帧阶段的可视呈现成本与回归风险。

当前整体状态已经可以拆成两条：

- 数据路径：
  - 小目录和已缓存目录的 `fetch` 已经足够快
  - 目录级持久结果集缓存生效后，二次进入常见目录通常已经不再受 Rust 侧冷加载主导
- UI 路径：
  - details 首帧 `first-frame` 仍明显高于 `fetch-completed`
  - 剩余主要成本在 details 行模板整体的首次 layout / measure

## 已确认结论

### 1. 滚动正确性问题已恢复

- 之前出现过：
  - 中间空洞
  - 底部空白行
  - 行复用后名称列空白、类型列已更新
- 其中最关键的已确认根因是：
  - `EntryNameCell` 为了首帧优化去掉 `Bindings.Update()` 后，只在 `Entry` 引用变化时刷新
  - details 行复用时，`EntryViewModel` 会原地更新 `Name/Icon...`
  - 结果名称列不跟着刷新
- 当前修复：
  - `EntryNameCell` 监听 `EntryViewModel.PropertyChanged`
  - 仅对 `Name / IconGlyph / IconForeground` 做轻量更新
- 这一修复已经提交，滚动正确性不再作为本轮主问题。

### 2. 单个 cell / 单个 host 不是当前首帧主热点

通过 item 级 perf 日志确认：

- `EntryNameCell` 单项测量基本在 `0ms`
- `EntryItemHost` 单项测量也基本在 `0-1ms`

所以当前瓶颈不是某一个 cell 的单次更新，而是：

- details 首帧这一批行一起进入 layout / measure / text / template 管线时的总成本

### 3. 当前首帧瓶颈主要在 details 行模板整体

最近一轮日志的稳定结论：

- `fetch-completed` 通常已经显著早于 `first-frame`
- `layout-perf` 中的首帧 measure 才是最接近实际热点的阶段

典型现象：

- `C:\\`、`C:\\Windows` 首帧 measure 成本高
- `D:\\`、`D:\\Develop` 相对更轻
- `D:\\Downloads` 仍有一定波动

这说明剩余成本与目录内容和 details 首屏布局共同相关，而不是纯粹的条目数问题。

## 已尝试的优化与结论

### 有明确收益

- 去掉 `EntryNameCell` 内部的 `Bindings.Update()`
- 将非关键的 UI finalize 延后到首帧之后
- 收紧 details 首帧的 layout realization 范围

这些优化对部分目录首帧已经有实测收益，尤其是中等规模目录。

### 收益有限或不稳定

- 扁平化 `EntryItemHost` 模板
- 对所有目录统一做“首帧少绑、帧后补齐”

结论：

- “首帧少绑”只适合较大目录
- 对中小目录会演变成两次布局成本，不一定更快

### 已验证不能保留的尝试

- 过度收紧 `FixedExtentVirtualizingLayout` 的可 realize 范围
- 在稳定滚动路径上继续叠加首帧实验逻辑

这类实验会直接引入：

- 空洞
- 空白名称行
- 中间未刷新的 recycled row

所以后续优化必须把“滚动正确性”当硬约束，不能再在稳定 sparse/recycle 主链上冒险叠实验。

## 当前推荐做法

后续如果继续优化 details 首帧，建议遵守下面几条：

1. 不再改动已恢复稳定的 sparse / recycle / scrolling correctness 主链。
2. 首帧优化优先落在：
   - details 行模板整体布局成本
   - 首帧列内容的轻量化
   - 非关键列/视觉收尾的延后
3. 所有尝试都应保留回退路径，并配合：
   - `navigation-perf.log`
   - `layout-perf.log`
   - 外部 `WinUI_MCP` 截图 / UIA 快照

## 外部诊断能力

本轮还额外完成了 `WinUI_MCP` 的外部验证：

- 已在 OpenCode 中成功挂载 `WinUI_MCP`
- 已验证：
  - attach 到运行中的 `NorthFile`
  - 获取窗口信息
  - 获取 UIA 快照

这意味着后续 WinUI 诊断不必只靠内部 perf 日志，还可以直接结合：

- 窗口截图
- accessibility tree
- element ref

来确认“代码认为正常”和“UI 实际正常”是否一致。

## 当前结论

截至 2026-03-24：

- details 滚动正确性已恢复
- 小目录/已缓存目录的数据获取已不是主瓶颈
- details 首帧性能仍需继续优化
- 后续优化重点应放在 details 行模板整体首帧布局成本，而不是再修改滚动模型或条目复用语义
