# R4 设置内存观察

只用于固定候选的数值诊断，不参与产品包。身份文件必须明确 PID、精确创建时间、EXE 路径和候选文件 SHA-256；每次采样前后重新核验。

- `sample_memory.py`：仅请求 `PROCESS_QUERY_LIMITED_INFORMATION`，优先读取 96 字节 `PROCESS_MEMORY_COUNTERS_EX2`。支持不足时只退回普通计数器，私有工作集明确记为不可用。不会把总工作集当私有工作集。
- `allocation_charge.py`：无参数时仅在自身创建的匿名分配上验证 reserve/commit/decommit 和 COW 收费。带明确身份及全新绝对输出路径时，尝试区域/驻留/逐分配收费元数据扫描；共用 20 秒协作式接受截止，系统调用本身无法硬中断。

分类器默认limited权限；`--query-information`显式使用初始契约允许的0x0400元数据查询权限，不自动回退，不加入VM_READ，不申请提权。用户批准追加一次观察后，普通权限1145用此明确参数取得完整分类快照。标量采样仍仅使用limited权限。

不请求 VM_READ/WRITE 或调试权限，不读取目标页内容、不注入、不暂停、不触发目标 GC 或裁剪、不运行 GUI/真实插件动作。扫描非原子，也没有分配栈或存活根，不能用于单独判定泄漏。

自有测试中的 `selfTest: PASS` 不是目标访问资格。必须逐项检查 limited 控制字段，并以目标完整调用的实际结果为准。R4 的管理员实例在 `QueryWorkingSetEx` 返回访问拒绝，完整扫描结果未落盘；没有使用权限升级或空结果冒充成功。普通权限新实例的运行时计数器另行绑定身份，不与管理员实例的周期数据混算。

R4 原始证据及预算：`G:\Users\2 Better\Documents\Codex\2026-10-30\starpie-issue-review\settings-memory-r4\`。此前各轮预算保留。
