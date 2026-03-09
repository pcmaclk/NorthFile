# Rust 文件管理器 MVP 功能矩阵与实现方案

## 1. 功能矩阵（P0/P1/P2）

| 优先级 | 功能 | 目标 | 主要模块 | 验收标准 |
| --- | --- | --- | --- | --- |
| P0 | 双路径路由 | 全场景可用 | `engine` `memory` `traditional` | NTFS/非NTFS/网络卷都可打开目录 |
| P0 | 目录分页枚举 | 大目录不卡顿 | `memory` `traditional` | 基于“当前目录稳定有序快照”的首批返回可用，UI 按页消费 |
| P0 | 基础元数据 | 列表可展示 | `core/types` | 名称/类型/时间/大小可显示 |
| P0 | 批量返回 FFI | 降低跨边界开销 | `ffi` | 单次 500~1000 条稳定，释放无泄漏 |
| P0 | 错误模型 | 可诊断可恢复 | `core/error` | UI 可按错误码分类提示 |
| P0 | 取消机制 | 快速切目录不阻塞 | `memory` `traditional` | 连续切目录无明显卡顿 |
| P1 | NTFS INDEX 枚举 | 提升目录性能 | `ntfs` `memory` | 热目录性能优于传统路径 |
| P1 | Lazy Record 解析 | 详情按需加载 | `ntfs` `memory` | 缓存命中延迟 < 1ms 级 |
| P1 | L1/L2 缓存 | 降低重复 IO | `cache` | 重复打开目录耗时显著下降 |
| P1 | 搜索分层匹配 | 秒搜基础 | `search` | 百万名称查询 P50 < 20ms（热） |
| P1 | 批量文件操作 | 管理器基本能力 | `ops`（新增） | 复制/移动/删除可取消、有进度 |
| P2 | USN 增量同步 | 实时一致性 | `ntfs` `memory` | 长时间运行无需全盘重扫 |
| P2 | 后台预读 | 冷热切换平滑 | `memory` `ntfs` | 不影响前台响应 |
| P2 | L3 Raw Cache | 优化随机 IO | `cache` | HDD 场景抖动明显降低 |
| P2 | 稳定性工程化 | 可上线 | 全模块 | 压测无崩溃、无明显泄漏 |

## 2. 实现方案（按阶段）

### 阶段 A：P0 可用版
1. 打通 `PathRouter`，按卷类型路由并支持失败降级。
2. 先实现 `TraditionalPath.enumerate_dir` 真枚举，确保可用。
3. `MemoryPath` 提供分页协议和空实现兼容，逐步替换为 NTFS 真实现。
4. 目录列表统一采用“后端稳定有序结果集 -> 分页返回 UI”的契约：
   - 排序必须在后端完成，`cursor` 基于排序后的稳定结果集。
   - UI 不得对已分页结果做二次重排。
   - 默认排序为“文件夹优先 + 名称升序”，后续通过 `SortMode` 扩展。
4. 建立 FFI 批量返回契约：
   - `entries: *mut FileEntry`
   - `entries_len`
   - `names_utf16: *mut u16`
   - `names_len`
   - `next_cursor/has_more`
   - `error_code/error_message`
5. 导出成对 `free` 函数，明确 Rust 分配、Rust 释放。

### 阶段 B：P1 性能版
1. 落地 NTFS `MFT runlist + INDEX_ROOT/INDEX_ALLOCATION`。
2. 接入 `Lazy record`（USA 修复 + 最小属性解析）。
3. 接入 L1/L2 缓存，先做容量可配置。
4. 搜索实现分层：`PrefixFilter -> ASCII fast path -> UTF-16 fallback`。
5. 列表详情采用“基础列表先到，当前视口详情异步补齐”的两级加载：
   - 基础字段：`name / is_dir / stable_id`
   - 详情字段：当前视口优先补 `modified / size / icon`
   - 下一页详情可预取，但必须低优先级、可取消、可丢弃过期结果

### 阶段 C：P2 增强版
1. 接入 USN 增量变更，更新 FileTable 与缓存。
2. 启动后台低优先级顺序预读，支持抢占。
3. 引入 L3 原始缓存（按设备与内存预算开关）。
4. 补齐观测：trace、metrics、错误聚合、压力回归。

## 3. 批量返回结构建议（MVP）

### Rust 侧
- `#[repr(C)] struct FfiBatchResult`：包含数组指针、字符串池指针、分页字段、错误字段。
- `#[repr(C)] struct FileEntry`：紧凑结构（24B 对齐目标）。
- 导出：
  - `fe_get_demo_batch(limit)`（MVP 骨架）
  - `fe_free_batch_result(result)`（回收内存）

### C# 侧
- `StructLayout.Sequential` 镜像 `RustFileEntry` 与 `RustBatchResult`。
- `LibraryImport` 对应导入 Rust 导出函数。
- 使用 `ReadOnlySpan<T>` 直读 `entries` 和 `name pool`，仅在绑定 UI 时构造托管字符串。

## 4. 关键约束

1. 不跨边界传 JSON。
2. 不每条记录传独立字符串指针。
3. 限制单批大小（默认 500，最大 2000）。
4. 所有错误路径必须可释放内存。
5. FFI 结构若变更，必须增加版本字段或保持向后兼容。
6. 排序模式必须进入缓存键，至少覆盖 `path + sort_mode`。
7. 当前目录浏览与 Everything 式全局搜索分开设计：
   - 当前目录浏览允许先构建当前目录有序快照再分页。
   - 全局搜索依赖索引和增量更新，不等同于目录浏览链路。
8. 详情异步加载必须可中断：
   - 目录切换、排序切换、搜索变更、快速滚出视口都要取消旧任务。
   - 回写 UI 前必须校验 `path + sort_mode + snapshot/version + index/item key`。

## 5. 下一步落地顺序（建议）

1. 先把 `TraditionalPath` 做成可用真实现。
2. 并行推进 `MemoryPath` 的 NTFS 枚举。
3. 在 UI 先接入批量 FFI 和分页滚动。
4. 统一目录列表排序契约，默认落地“文件夹优先 + 名称升序”，并预留 `SortMode`。
5. 将列表填充拆成“基础页数据 + 视口详情异步补齐 + 下一页详情预取”。
6. 最后再叠加搜索分层和 USN。

## 6. 当前目录列表的两档策略

### 3. 常规大目录模式
- 适用范围：`<= 100,000` 子项。
- NTFS 本地盘：优先走 `INDEX_ROOT / MemoryPath` 目录项。
- 非 NTFS、网络盘或 NTFS 原始访问失败：fallback 到普通目录枚举。
- 后端先形成当前目录的稳定有序结果集，再按 `cursor` 分页返回 UI。
- 详情策略：基础列表先返回；当前页详情优先补齐；下一页详情仅做低优先级预取。

### 4. 超大目录模式
- 适用范围：`> 100,000` 子项。
- 不再把普通 `read_dir + 全量重排` 视为长期默认主路径。
- NTFS 本地盘优先利用目录索引、热目录缓存和增量失效。
- 非 NTFS / 网络盘允许功能性降级，优先保证可用与可取消。
