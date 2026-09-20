import re, sys, subprocess, pathlib

# Windows 默认 GBK 控制台会把中文检查结果显示成乱码；统一为 UTF-8，便于本机和 CI 诊断。
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

root = pathlib.Path(__file__).resolve().parent.parent
src = root / "WinPieGestures"
i18n = (src / "I18n.cs").read_text(encoding="utf-8")

# 1) 解析词条定义
defs = {}
order = []
dup = []
pattern = re.compile(
    r'dictionary\["(?P<key>[^"]+)"\]\s*=\s*new Dictionary<LanguageCode,\s*string>\s*\{(?P<body>.*?)\n\t\t\};',
    re.S,
)
for m in pattern.finditer(i18n):
    key, body = m.group("key"), m.group("body")
    langs = dict(re.findall(r"\[LanguageCode\.(\w+)\]\s*=\s*(.*?)(?=\n\s*\[LanguageCode\.|\Z)", body, re.S))
    langs = {k: v.strip().rstrip(",").strip() for k, v in langs.items()}
    if key in defs:
        dup.append(key)
    defs[key] = langs
    order.append(key)

print(f"词条定义总数: {len(order)}  唯一键: {len(defs)}  重复定义: {len(dup)}")
if dup:
    print("  重复键:", dup)

LANGS = ["ZhCn", "ZhTw", "En", "Ja"]
missing = [(k, [l for l in LANGS if l not in v]) for k, v in defs.items() if any(l not in v for l in LANGS)]
print(f"缺语言分支的键: {len(missing)}")
for k, ms in missing[:10]:
    print(f"  {k}: 缺 {ms}")

empty = [k for k, v in defs.items() if not v.get("ZhCn") or v.get("ZhCn") in ('""', '')]
print(f"简中文案为空的键: {len(empty)}  {empty[:10]}")

# 2) 收集代码里的引用
#
# **必须跳过注释行**：文档注释里出现 `I18n.T("…")` 这样的示例是常态
# （说明「新增默认名赋值点时要同步哪里」时就会写），把它当成真引用会报出
# 「引用但未定义」的假阳性 —— 而假阳性会让人开始忽略这个脚本的输出。
refs = set()
dynamic = []
mentions = set()   # 出现在代码里的任意字符串字面量（含三元表达式里拼的键，refs 抓不到）
refpat = re.compile(r'I18n\.(?:T|TF)\(\s*"([^"]+)"')
dynpat = re.compile(r'I18n\.(?:T|TF)\(\s*[^"\s]')
for f in src.rglob("*.cs"):
    if "obj" in f.parts or "bin" in f.parts or f.name == "I18n.cs":
        continue
    for lineno, line in enumerate(f.read_text(encoding="utf-8", errors="ignore").splitlines(), 1):
        stripped = line.lstrip()
        if stripped.startswith("//") or stripped.startswith("*"):
            continue
        for m in refpat.finditer(line):
            refs.add(m.group(1))
        for m in dynpat.finditer(line):
            dynamic.append(f"{f.name}:{lineno}")
        mentions.update(re.findall(r'"([A-Za-z][A-Za-z0-9_]{6,})"', line))

undef = sorted(refs - set(defs))
print(f"代码引用的唯一键: {len(refs)}")
print(f"引用但未定义（应为 0）: {len(undef)}  {undef}")
orphan = sorted(set(defs) - refs)
print(f"已定义但无人引用: {len(orphan)}")

# 3) 相对某个基线新增 / 改动的键。
#    基线默认 HEAD（即看未提交的工作区改动）；已提交后想回看某一轮就传 --since HEAD~1。
#    刻意不写死键名列表：写死的那种在下一轮就变成过期信息，还会让人误以为「本次新增=0」是好事。
since = "HEAD"
if "--since" in sys.argv:
    since = sys.argv[sys.argv.index("--since") + 1]

def keys_at(ref):
    """取某个 ref 下 I18n.cs 里的键集合。"""
    try:
        blob = subprocess.run(
            ["git", "show", f"{ref}:WinPieGestures/I18n.cs"],
            cwd=root, capture_output=True, text=True, encoding="utf-8",
        ).stdout
    except Exception:
        return set()
    return set(re.findall(r'dictionary\["([^"]+)"\]\s*=\s*new Dictionary', blob))

base = keys_at(since)
new_keys = sorted(set(defs) - base) if base else []
print(f"\n相对 {since} 新增键: {len(new_keys)}" + ("" if base else "  （基线读不到，已跳过）"))
for k in new_keys:
    ph = set(re.findall(r"\{(\d+)\}", "".join(defs[k].values())))
    ref = "是" if k in refs else ("是(三元拼接)" if k in mentions else "否 ✗")
    print(f"  {k:40s} 引用={ref}  语言={len([l for l in LANGS if l in defs[k]])}  占位符={sorted(ph)}")

# 4) 占位符一致性（对应 ui-i18n-bulk-wiring 坑 19 / 坑 20）
#    ① 同一个键的 4 种语言，占位符集合必须完全一致：
#       少一个 {0} 在 C# 里不报错也不抛异常，只是那一段信息静默消失。
#    ② 占位符「实参本身有没有接 i18n」这件事**机器查不了**，本脚本刻意不假装能查：
#       真正会出问题的形态是实参来自一个返回硬编码中文的方法（如 DescribeFailure()），
#       而字面量实参含中文只是个几乎不成立的边角。这里只列「实参不是字面量」的调用点
#       供人工顺着来源追，不做任何自动判定。
ph_mismatch = []
for k, v in defs.items():
    sets = {l: tuple(sorted(set(re.findall(r"\{(\d+)\}", v.get(l, ""))))) for l in LANGS if l in v}
    if len(set(sets.values())) > 1:
        ph_mismatch.append((k, sets))
print(f"\n占位符集合跨语言不一致（应为 0）: {len(ph_mismatch)}")
for k, sets in ph_mismatch[:10]:
    print(f"  {k}: {sets}")

nonliteral = []
argpat = re.compile(r'I18n\.TF\(\s*"([^"]+)"\s*,([^)]*)\)', re.S)
for f in src.rglob("*.cs"):
    if "obj" in f.parts or "bin" in f.parts or f.name == "I18n.cs":
        continue
    txt = f.read_text(encoding="utf-8", errors="ignore")
    for m in argpat.finditer(txt):
        args = [a.strip() for a in m.group(2).split(",") if a.strip()]
        if any(not (a.startswith('"') and a.endswith('"')) for a in args):
            nonliteral.append(f"{f.name}: {m.group(1)}")

print(f"\n实参非字面量的 TF 调用点（需人工顺来源追是否已本地化）: {len(nonliteral)}")
for c in nonliteral[:20]:
    print(f"  {c}")

# 5) XAML 具名控件的属性漏接复查（有 Name + 硬编码中文，但代码从未重设）
#
# **必须按「开标签 → 标签内属性」两层解析，不能用 `Name="…"[^>]*?(Text|Content|ToolTip)="…"`
# 那种一条正则横扫的写法**：非贪婪的 `[^>]*?` 只会命中**标签里的第一个**含中文属性，
# 命中之后正则从匹配结束处继续扫，同一元素里剩下的属性就整体落在消费区间里再也不会被看到 ——
# `PluginInstallDialog` 的 `CopyButton` 就是这样把 `Content`（已接）报掉、`ToolTip`（没接）漏掉的。
# 一个元素上挂三四个中文属性是本项目的常态，所以这个盲区一直在放真漏翻过去。
elementpat = re.compile(r"<(\w+)\b([^>]*?)/?>", re.S)
attributepat = re.compile(r'\b(Text|Content|ToolTip)="([^"]*)"')
namepat = re.compile(r'\bName="(\w+)"')
cjkpat = re.compile(r"[\u4e00-\u9fff]")

# 所有 *.xaml.cs 拼在一起找「重设点」：某个控件可能在别的窗口 / 公共辅助类里被统一重设。
all_code = "\n".join(
    f.read_text(encoding="utf-8", errors="ignore")
    for f in src.rglob("*.xaml.cs")
    if "obj" not in f.parts and "bin" not in f.parts
)


def scan_named_leaks(path, keep=None):
    """列出 `path` 里「有 Name + 含中文的属性 + 全仓代码从未重设过」的属性。"""
    found = []
    for m in elementpat.finditer(path.read_text(encoding="utf-8", errors="ignore")):
        tag = m.group(0)
        nm = namepat.search(tag)
        if not nm:
            continue
        for am in attributepat.finditer(tag):
            attr, val = am.group(1), am.group(2)
            if not cjkpat.search(val):
                continue
            if keep is not None and not keep(val):
                continue
            if f"{nm.group(1)}.{attr}" in all_code:
                continue
            found.append((nm.group(1), attr, val))
    return found


xaml_files = sorted(
    f for f in src.rglob("*.xaml") if "obj" not in f.parts and "bin" not in f.parts
)

# 5a) 插件页专项（历史语义：只看插件相关文案，避免被存量欠账冲掉视线）
leaks = []
for name, attr, text in scan_named_leaks(
    src / "SettingsWindow.xaml", keep=lambda v: any(t in v for t in ["官方", "插件", "外掛"])
):
    leaks.append((name, text[:40]))
print(f"\n插件页仍漏接的具名控件: {len(leaks)}")
for n, t in leaks:
    print(f"  {n}: {t}")

# 5b) 全仓棘轮：存量登记在基线里，**只对新增报红**。
#
# 存在的意义：5a 是一条「插件页」专用网，网眼之外（OCR / 快速搜索 / 各类选择器窗口）
# 长期积着几十处有 Name 却从未本地化的控件，在英文界面上一直显示中文。
# 一次性修完会做出一个跨十几个窗口的巨型 diff（与本项目的「一阶段一提交」相冲突），
# 所以这里先把它们**登记下来**，把「不许变多」变成可执行的门禁 —— 存量是这个脚本
# 唯一允许存在的负债，且它不会自己变少，只会被显式收紧。
baseline_path = root / "scratch" / "i18n_xaml_leak_baseline.json"
observed = set()
for xf in xaml_files:
    for name, attr, _ in scan_named_leaks(xf):
        observed.add(f"{xf.name}::{name}.{attr}")

import json

if "--write-baseline" in sys.argv:
    # 收紧/重建基线。**只在确实修掉了漏翻之后跑**：它把「当前观测」整体记为已知，
    # 所以它既能收紧也能悄悄放宽 —— 跑完必须 `git diff` 逐条看，确认没有把新漏翻一起写进去。
    baseline_path.write_text(
        json.dumps(
            {
                "_comment": "check_i18n.py 第 5b 段的存量基线：有 Name + 含中文的属性，但全仓 *.xaml.cs 从未重设过。只允许变少；新增即红（EXIT=1）。",
                "known": sorted(observed),
            },
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    print(f"已写入基线: {baseline_path}（{len(observed)} 条）")
    sys.exit(0)

if baseline_path.exists():
    known = set(json.loads(baseline_path.read_text(encoding="utf-8"))["known"])
else:
    known = set(observed)   # 首次运行：就地登记，不让缺基线变成假红

added = sorted(observed - known)
stale = sorted(known - observed)
print(f"\nXAML 具名控件漏接（全仓棘轮）: 存量 {len(known)}  当前 {len(observed)}  新增 {len(added)}")
for a in added:
    print(f"  [新增] {a}")
if stale:
    print(f"  以下 {len(stale)} 条已不再是漏翻，可从基线收紧:")
    for s in stale[:10]:
        print(f"    {s}")

# 有新增即非零退出 —— 门禁要能被脚本调用方直接判，而不是靠人读输出。
if added:
    sys.exit(1)

