Rust 级 MFT 解析方案

---

# 🧭 总体设计目标（先定边界）

这个方案假设你的产品是：

* ✅ 高性能文件管理器 / 索引器
* ✅ 直接读 NTFS
* ✅ 按需解析为主
* ✅ 支持 USN 增量
* ✅ 支持超大目录
* ✅ Rust 实现

设计哲学：

```text
轻索引（INDEX）驱动 UI
完整解析（MFT）按需触发
后台顺序预读补热缓存
USN 保持增量一致
```

---

# 🏗️ 一、模块总览（工程分层）

```text
┌────────────────────────────┐
│ UI / Query Layer           │
├────────────────────────────┤
│ Directory Engine           │
│ Lazy File Resolver         │
├────────────────────────────┤
│ NTFS Parser Core           │
│   • MFT Reader             │
│   • Attribute Parser       │
│   • Index Parser           │
│   • Runlist Engine         │
├────────────────────────────┤
│ IO Scheduler (priority)    │
├────────────────────────────┤
│ Multi-level Cache          │
└────────────────────────────┘
```

---

# 🚀 二、启动阶段（冷启动路径）

## Step 1️⃣ 打开卷

```rust
NtfsVolume::open(r"\\.\C:")
```

关键点：

* FILE_FLAG_NO_BUFFERING（可选高级优化）
* 读 BPB
* 定位 `$MFT`

缓存：

```rust
struct NtfsMeta {
    bytes_per_cluster: u32,
    bytes_per_record: u32,
    mft_lcn: u64,
}
```

---

## Step 2️⃣ 只做一件事：定位 MFT Runlist

⚠️ 启动时**不要扫描全盘**

只需要：

```text
读取 $MFT 的 DATA attribute
解析 runlist
```

得到：

```rust
struct MftLayout {
    runs: SmallVec<[Run; 8]>,
}
```

👉 这是后续所有随机访问的基础。

---

# 📂 三、目录枚举路径（最高优先级热路径）

这是用户最常触发的路径。

---

## Step 3️⃣ 读取目录 INDEX（不是 MFT）

当用户进入目录：

```rust
fn enumerate_dir(mft_ref) -> DirCursor
```

流程：

```text
读取该目录 FILE Record
   ↓
解析：
   • INDEX_ROOT
   • INDEX_ALLOCATION（如存在）
   ↓
返回 DirEntry 列表
```

⚠️ **关键原则**

```text
只用 INDEX 信息填 UI
不要解析子文件 MFT
```

DirEntry 最小结构：

```rust
struct DirEntryLite {
    file_ref: u64,
    name_range: StrRange, // 指向字符串池
    flags: u16,
}
```

目标：

> 🔥 首屏 < 5ms
> 🔥 无额外 IO

---

# 🧊 四、延迟 FILE Record 解析（Lazy Resolver）

只有在需要详细信息时触发：

触发条件：

* 用户选中
* 需要 size
* 需要时间
* 需要权限
* 需要数据流

---

## Step 4️⃣ Lazy Resolver

```rust
fn get_file_record(mft_ref) -> Arc<FileRecord>
```

内部流程：

```text
L2 cache 命中？ → 返回
        ↓
计算 MFT 偏移
        ↓
读取 record（可能跨 run）
        ↓
修复 USA
        ↓
解析 attribute
        ↓
写入缓存
```

---

## MFT 偏移计算（必须正确）

```rust
fn mft_offset(meta: &NtfsMeta, layout: &MftLayout, index: u64) -> u64
```

注意支持：

* 非连续 run
* 大盘
* 稀疏

很多实现死在这里。

---

# 🌊 五、后台顺序预读（性能上限关键）

单独线程：

```rust
struct MftPrefetcher
```

策略：

```text
顺序扫描 MFT record
   ↓
只做：
   • USA 修复
   • 基本校验
   ↓
写入 L3 Raw Cache
```

⚠️ 不要完整解析 attribute（浪费 CPU）

---

## 调度策略（非常关键）

优先级：

```text
High   → 目录读取
Medium → Lazy record
Low    → 预读
```

并且：

> ❗ Low 必须可被打断

---

# 🧠 六、多级缓存设计（决定上限）

这是性能分水岭。

---

## 🥇 L1 Directory Cache

```rust
LruCache<MftRef, Arc<DirPage>>
```

容量建议：

```text
32–128 目录
```

---

## 🥈 L2 File Record Cache

```rust
DashMap<u64, Arc<FileRecord>>
```

容量：

```text
50k–200k records
```

---

## 🥉 L3 Raw MFT Cache（高手优化）

```rust
moka::Cache<u64, Bytes>
```

key：

```text
mft_index
```

作用：

* 减少随机 IO
* 加速 Lazy
* 帮助 HDD

---

# 🔄 七、USN Journal 增量同步（必须预留）

即使第一版不做，也要预留。

---

## 接口设计（推荐）

```rust
trait ChangeFeed {
    fn poll_changes(&self, since: Usn) -> Vec<ChangeEvent>;
}
```

---

## 更新流程

```text
读取 USN
   ↓
定位 MFT record
   ↓
更新：
   • Dir cache
   • File cache
   • name index（如果有）
```

复杂度：

```text
O(变化文件数)
```

不是全盘。

---

# 🧨 八、必须提前避开的坑（非常重要）

这是实战踩坑总结。

---

## ❌ 坑 1：启动扫描整个 MFT

会导致：

* 冷启动爆炸
* 内存暴涨
* UI 延迟

正确：

> 按需 + 后台

---

## ❌ 坑 2：每个文件一个 String

必须：

> 字符串池

---

## ❌ 坑 3：假设 MFT 连续

必须支持：

* runlist
* 跨段
* 非驻留

---

## ❌ 坑 4：没有取消机制

必须：

```rust
CancellationToken
```

否则用户快速切目录会炸。

---

## ❌ 坑 5：目录一次性吐给 UI

必须：

> 分批 + cursor

---

# 📊 九、性能预期（按本方案）

在现代 SSD + Rust 优化下：

| 场景          | 预期             |
| ----------- | -------------- |
| 打开普通目录      | < 5 ms         |
| 打开 10 万文件目录 | 5–20 ms 首屏     |
| Lazy 单文件    | < 0.2 ms（缓存命中） |
| 冷 Lazy      | 0.5–2 ms       |
| 顺序预读        | 接近磁盘带宽         |

---

# 🧠 最终一句话架构

> **INDEX 驱动目录，MFT 按需解析，后台顺序补热，USN 保持一致，缓存分层承压。**

这就是目前 Windows 世界高性能 NTFS 工程的主流最优解。

---

---

如果你下一步想进入真正“高手区”，我可以给你：

🔥 **Rust MFT Record 字节级解析模板（含 USA 修复 + attribute walker）**

这是很多人写错的地方。
