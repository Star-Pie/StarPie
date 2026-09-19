import re, sys, subprocess, pathlib

root = pathlib.Path(r"D:\IO\dotnet\StarPie")
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
refs = set()
dynamic = []
mentions = set()   # 出现在代码里的任意字符串字面量（含三元表达式里拼的键，refs 抓不到）
refpat = re.compile(r'I18n\.(?:T|TF)\(\s*"([^"]+)"')
for f in src.rglob("*.cs"):
    if "obj" in f.parts or "bin" in f.parts or f.name == "I18n.cs":
        continue
    txt = f.read_text(encoding="utf-8", errors="ignore")
    for m in refpat.finditer(txt):
        refs.add(m.group(1))
    for m in re.finditer(r'I18n\.(?:T|TF)\(\s*[^"\s]', txt):
        dynamic.append(f"{f.name}:{txt[:m.start()].count(chr(10))+1}")
    mentions.update(re.findall(r'"([A-Za-z][A-Za-z0-9_]{6,})"', txt))

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

# 5) 插件页静态控件漏接复查（有 Name + 硬编码中文，但代码从未重设）
xaml = (src / "SettingsWindow.xaml").read_text(encoding="utf-8")
code = "\n".join((f.read_text(encoding="utf-8", errors="ignore") for f in [src / "SettingsWindow.xaml.cs"]))
named_cjk = re.findall(r'Name="(\w+)"[^>]*?(?:Text|Content)="([^"]*[\u4e00-\u9fff][^"]*)"', xaml)
leaks = []
for name, text in named_cjk:
    # 只看插件页（官方目录 + 候选 + 列表）
    if not any(t in text for t in ["官方", "插件", "外掛"]):
        continue
    if f"{name}.Text" not in code and f"{name}.Content" not in code:
        leaks.append((name, text[:40]))
print(f"\n插件页仍漏接的具名控件: {len(leaks)}")
for n, t in leaks:
    print(f"  {n}: {t}")
