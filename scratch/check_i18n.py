import re, sys, pathlib

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
refpat = re.compile(r'I18n\.(?:T|TF)\(\s*"([^"]+)"')
for f in src.rglob("*.cs"):
    if "obj" in f.parts or "bin" in f.parts or f.name == "I18n.cs":
        continue
    txt = f.read_text(encoding="utf-8", errors="ignore")
    for m in refpat.finditer(txt):
        refs.add(m.group(1))
    for m in re.finditer(r'I18n\.(?:T|TF)\(\s*[^"\s]', txt):
        dynamic.append(f"{f.name}:{txt[:m.start()].count(chr(10))+1}")

undef = sorted(refs - set(defs))
print(f"代码引用的唯一键: {len(refs)}")
print(f"引用但未定义（应为 0）: {len(undef)}  {undef}")
orphan = sorted(set(defs) - refs)
print(f"已定义但无人引用: {len(orphan)}")

# 3) 重点：这次新增的 19 个键状态
new_keys = [k for k in defs if k.startswith("PluginsOfficial") or k in ("PluginsActionNotSelected", "PluginsActionPluginNotFound")]
print(f"\n本次新增键: {len(new_keys)}")
for k in sorted(new_keys):
    print(f"  {k:38s} 引用={'是' if k in refs else '否 ✗'}  语言={len([l for l in LANGS if l in defs[k]])}")

# 4) 插件页静态控件漏接复查（有 Name + 硬编码中文，但代码从未重设）
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
