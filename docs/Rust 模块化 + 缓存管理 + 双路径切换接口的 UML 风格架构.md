好，那我帮你设计一个 **Rust 模块化 + 缓存管理 + 双路径切换接口的 UML 风格架构**，直接面向工程落地。下面是用 Mermaid 写的代码，方便团队直接参考或在文档里展示：

```mermaid
classDiagram
    %% UI Layer
    class UI {
        <<module>>
        +search(query: &str)
        +open_directory(dir_id: u32)
        +subscribe_updates()
    }

    %% Memory Path
    class MemoryPath {
        <<module>>
        +get_dir_entries(parent_id: u32) : Vec<FileEntry>
        +search(query: &str) : Vec<FileEntry>
        +update_from_usn()
    }

    class FileTable {
        <<struct>>
        +entries: Vec<FileEntry>
        +names: StringPool
    }

    class FileEntry {
        <<struct>>
        +parent_id: u32
        +name_off: u32
        +name_len: u16
        +flags: u16
        +mft_ref: u64
    }

    class StringPool {
        <<struct>>
        +data: Vec<u16>
        +intern(name: &[u16]) -> (u32, u16)
    }

    class DirCache {
        <<cache>>
        +get(parent_id: u32) : Option<Vec<FileEntry>>
        +insert(parent_id: u32, entries: Vec<FileEntry>)
    }

    class FileCache {
        <<cache>>
        +get(mft_ref: u64) : Option<FileRecord>
        +insert(mft_ref: u64, record: FileRecord)
    }

    class RawCache {
        <<cache>>
        +read(run: Run) -> Bytes
    }

    class USN {
        <<module>>
        +poll_changes() : Vec<ChangeEvent>
    }

    class FileRecord {
        <<struct>>
        +attributes: Vec<Attribute>
    }

    %% Traditional Path
    class TraditionalPath {
        <<module>>
        +open_directory(path: &Path) : Vec<DirEntry>
        +search(query: &str) : Vec<DirEntry>
    }

    class SysAPI {
        <<module>>
        +enumerate_dir(path: &Path) : Vec<DirEntry>
    }

    class LazyAttr {
        <<module>>
        +read_size(path: &Path) -> u64
        +read_timestamps(path: &Path) -> Timestamps
    }

    %% UI → Paths
    UI --> MemoryPath : search / open_directory
    UI --> TraditionalPath : fallback / network / non-NTFS

    %% Memory Path内部
    MemoryPath --> FileTable
    MemoryPath --> DirCache
    MemoryPath --> FileCache
    MemoryPath --> RawCache
    MemoryPath --> USN

    FileTable --> FileEntry
    FileTable --> StringPool

    FileCache --> FileRecord

    %% Traditional Path内部
    TraditionalPath --> SysAPI
    SysAPI --> LazyAttr
```

---

### 🔹 模块说明

1. **UI 模块**

   * 统一接口搜索 / 打开目录
   * 订阅内存表或 Traditional Path 更新

2. **MemoryPath 模块**

   * FileTable + StringPool 主存储
   * DirCache / FileCache / RawCache 多级缓存
   * USN 增量更新保持 MFT 与表同步
   * 支持 parent_id 过滤、搜索

3. **FileEntry & FileRecord**

   * FileEntry：24B 紧凑结构体
   * FileRecord：按需解析属性（lazy）

4. **TraditionalPath 模块**

   * 系统 API 枚举目录
   * Lazy 属性读取
   * 处理非 NTFS / 网络卷

5. **缓存层设计**

   * L1 DirCache：热目录快速访问
   * L2 FileCache：MFT / FileRecord lazy
   * L3 RawCache：顺序预读减少随机 IO

---

这张 UML 风格图把 **模块划分 + 缓存层次 + 路径切换接口** 全覆盖，Rust 实现几乎可以直接映射类和结构体，非常利于团队开发和文档落地。

如果你愿意，我可以进一步给你画一张 **MemoryPath 内存布局 + SIMD 搜索 + 缓存层次联合数据流图**，把内存访问和搜索流程直观展示出来，便于优化和调试。

你希望我画这个吗？
