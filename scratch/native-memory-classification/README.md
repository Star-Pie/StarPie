# Windows 内存元数据分类

`memory_map.py` 仅请求 `PROCESS_QUERY_INFORMATION`，使用 `VirtualQueryEx`、`QueryWorkingSetEx`、进程路径/创建时间和 `GetProcessMemoryInfo`。不读取目标页内容、触碰/锁定目标页、暂停、注入、GC或裁剪。

先运行 `python -X utf8 memory_map.py --self-test`，仅在自身拥有的16 MiB保留/4 MiB提交分配上验证结构、驻留位和释放。再以 `--identity <明确身份JSON> --output <全新绝对JSON路径>` 观察经确认的实例；身份含pid、createdUtc、exePath和fileSha256，采样前后均核验。结果仅记录路径已核验及候选文件哈希，不重复输出路径。64位Windows专用；2GiB已提交页、20万区域查询上限，以及20秒协作式截止（每批查询前后检查，不能中断正在执行的Win32调用）；超限拒绝不完整结果。

结果区分保留、提交、驻留及shareable/private；MEM_IMAGE/MAPPED可含私有COW页，Shared位表示可共享，不等于当前多进程共享。非原子扫描不能解释分配调用栈或证明泄漏。绝对地址仅在本地报告中作为同实例分配比较标识；不得将原始元数据发给外部模型，材料评议仅给聚合数值。
