好，我给你画一个 **完整双路径架构一体化大图**，把 **MemoryPath + TraditionalPath + UI + 缓存 + USN + 搜索路径** 全部整合，便于工程落地和团队沟通。下面是 Mermaid 代码版本：

```mermaid
flowchart LR
    %% UI Layer
    UI[UI / Query Layer<br>虚拟列表 + 分批渲染]

    %% Memory Path
    MemoryPath[Memory Path<br>全局文件名表]
    FileTable[FileTable<br>FileEntry ~24B/文件 + StringPool ~56B/文件]
    FileEntry[FileEntry<br>u32 parent_id<br>u32 name_off<br>u16 name_len<br>u16 flags<br>u64 mft_ref]
    StringPool[StringPool<br>UTF-16 连续字符池]

    %% Memory Path 缓存
    DirCache[L1 Directory Cache<br>热目录快速访问]
    FileCache[L2 FileRecord Cache<br>Lazy 属性解析]
    RawCache[L3 Raw MFT / IO Cache<br>顺序预读]

    %% Memory Path 搜索
    PrefixFilter[前缀过滤 / first_char hash]
    SIMDMatcher[SIMD / memchr 匹配器<br>ASCII 优先, UTF-16 fallback]

    %% Memory Path 更新
    USN[USN Journal 增量更新<br>文件系统变化]

    %% Traditional Path
    TraditionalPath[Traditional Path<br>按需目录枚举]
    SysAPI[系统 API / NtQueryDirectoryFile]
    LazyAttr[延迟属性读取<br>size / timestamps / ACL]
    NetworkVolume[网络卷 / 非 NTFS 文件系统]

    %% 用户操作流
    UI -->|搜索 / 打开目录| PrefixFilter
    PrefixFilter --> SIMDMatcher
    SIMDMatcher --> FileTable
    FileTable --> FileEntry
    FileTable --> StringPool

    %% Memory Path 缓存访问
    SIMDMatcher --> DirCache
    SIMDMatcher --> FileCache
    SIMDMatcher --> RawCache

    %% USN 增量更新流
    USN --> FileTable
    USN --> DirCache
    USN --> FileCache
    FileTable -->|刷新 UI| UI

    %% UI → Traditional Path
    UI -->|搜索 / 打开目录<br>卷不支持 NTFS 或网络卷| TraditionalPath
    TraditionalPath --> SysAPI
    SysAPI --> LazyAttr
    LazyAttr -->|返回结果| UI
    TraditionalPath --> NetworkVolume

    %% Fallback / Mixing
    MemoryPath -->|卷异常或不支持| TraditionalPath
```

---

### 🔹 关键点说明

1. **MemoryPath（内存路径）**

   * FileTable + StringPool 存储全局文件信息
   * 多级缓存（DirCache / FileCache / RawCache）
   * 搜索通过 PrefixFilter + SIMDMatcher 高速完成
   * USN 增量更新保持表实时性

2. **TraditionalPath（传统路径）**

   * 系统 API 枚举目录 / 文件
   * 延迟读取属性
   * 支持网络卷或非 NTFS 卷
   * MemoryPath 异常或不支持卷时 fallback

3. **UI 层**

   * 虚拟列表 + 分批渲染
   * 搜索 / 目录打开统一接口
   * 自动选择 MemoryPath 或 TraditionalPath

4. **搜索优化**

   * PrefixFilter 快速跳过不匹配项
   * SIMDMatcher 快速匹配 ASCII / UTF-16
   * 顺序扫描内存数组，提高 CPU cache 命中率

5. **数据流与更新**

   * 用户操作 → 内存过滤 → 缓存命中 → UI 渲染
   * 背景预读 + USN 增量更新保证冷启动和实时性
   * 缓存分层减少磁盘 IO，提高响应速度

---

💡 这个图把 **双路径架构、MemoryPath 内存表 + 缓存、多级搜索优化、USN 实时更新、UI 渲染** 全部整合，是商业级文件管理器架构蓝图，可直接作为工程参考。

如果你愿意，我可以帮你在这个图的基础上 **加上 MemoryPath 内存占用估算 + 文件数量分级性能预期**，让团队更直观判断资源消耗和优化点。

你希望我画吗？
