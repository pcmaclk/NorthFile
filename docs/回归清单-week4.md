# 回归清单 Week4

## 1. 构建
1. `cargo build`（`rust_engine`）通过
2. `dotnet build`（`FileExplorerUI`）通过

## 2. 浏览与导航
1. 路径输入跳转（回车/Load）可用
2. 面包屑、Back、Up 可用
3. 大目录滚动分页与预取无明显卡顿

## 3. 文件操作
1. 重命名成功后列表一致
2. 删除（递归/非递归）提示正确
3. 操作失败提示可读（非裸错误码）

## 4. 搜索
1. ASCII 查询（如 `ic`）返回正确
2. Unicode 查询（如中文关键字）返回正确
3. 状态栏显示 `Match` 命中率、`Fetch` 耗时

## 5. USN/刷新策略
1. 状态栏显示 `USN: Available/Denied/NotNTFS`
2. USN 不可用时，目录刷新自动走缓存失效分支
3. `ProbeUsnCapability` 调用不崩溃

## 6. Fallback
1. NTFS 卷无权限打开原始设备时，不影响搜索与浏览（自动 fallback）
2. 非 NTFS 或网络盘稳定走 Traditional 路径
