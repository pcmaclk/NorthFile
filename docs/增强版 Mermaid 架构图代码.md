明白，我给你做一个**增强版 Mermaid 架构图代码**，把 **缓存层大小、FileEntry 内存布局、字符串池** 都标注出来，这样在 Rust 实现中可以直接参考。

```mermaid
flowchart LR
    %% UI Layer
    UI[UI / Query Layer<br>虚拟列表 + 分批渲染]

    %% Memory Path
    MemoryPath[Memory Path<br>全局文件名表]
    FileTable[FileTable<br>FileEntry ~24B/文件 + StringPool ~56B/文件]
    DirCache[L1 Directory Cache<br>32–128 热目录]
    FileCache[L2 File Record Cache<br>50k–200k records]
    RawCache[L3 Raw MFT / IO Cache<br>顺序预读，低优先级]
    USN[USN Incremental Updates<br>保持 MFT 与 FileTable 同步]

    %% Traditional Path
    TraditionalPath[Traditional Path<br>按需目录枚举]
    SysAPI[系统 API / NtQueryDirectoryFile]
    LazyAttr[延迟属性读取<br>size/timestamps/ACL]
    NetworkVolume[网络卷 / 非 NTFS 文件系统]

    %% UI → Memory Path
    UI -->|搜索/目录打开| MemoryPath
    MemoryPath --> FileTable
    MemoryPath --> DirCache
    MemoryPath --> FileCache
    MemoryPath --> RawCache
    MemoryPath --> USN
    USN --> FileTable
    FileTable -->|返回结果| UI

    %% UI → Traditional Path
    UI -->|搜索/目录打开| TraditionalPath
    TraditionalPath --> SysAPI
    SysAPI --> LazyAttr
    LazyAttr -->|返回结果| UI
    TraditionalPath --> NetworkVolume

    %% Fallback / Mixing
    MemoryPath -->|卷不支持或异常| TraditionalPath

    %% 内存布局标注
    subgraph MemoryPathDetails[Memory Path 内存布局]
        FE[FileEntry<br>u32 parent_id<br>u32 name_off<br>u16 name_len<br>u16 flags<br>u64 mft_ref<br>≈24B]
        SP[StringPool<br>UTF-16连续字符池<br>平均 ~56B/文件]
    end
    FileTable --> FE
    FileTable --> SP
```

---

### 🔹 设计要点

1. **FileEntry + StringPool**

   * 每文件 ≈ 80 B
   * 连续数组，cache-friendly
   * 支持 parent_id 快速过滤
   * mft_ref 用于 lazy MFT record 解析

2. **缓存分层**

   * L1：目录缓存，快速返回目录内容
   * L2：FileRecord 缓存，lazy 属性
   * L3：Raw MFT / IO Cache，用于顺序预读

3. **USN 增量更新**

   * NTFS 卷实时同步
   * 内存表保持最新
   * UI 自动刷新

4. **Traditional Path**

   * 兼容非 NTFS 卷 / 网络盘
   * 延迟属性读取减少 IO
   * fallback 或混合模式

5. **UI 层**

   * 虚拟列表 + 分批渲染
   * 搜索、排序、分页全靠内存过滤
   * 秒级响应

---

这个图把**FileEntry 内存布局 + 字符串池 + 缓存层级 + Memory/Traditional 双路径**全覆盖，是工程级参考蓝图。

我可以下一步帮你做一个 **Rust 模块划分 + 缓存管理 + 路径切换接口的 UML 风格设计**，让你落地实现几乎零歧义。

你希望我画这个吗？
