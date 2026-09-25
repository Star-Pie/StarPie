"""
StarPie 国际化词条完整性静态护栏。
用于检查：
1. 词条定义语法（兼容历史遗留的 legacy 字典语法与扁平 Add 语法）；
2. 简中文案非空、语言分支齐全度（ZhCn, ZhTw, En, Ja）；
3. 代码实际引用键是否全部已定义（undef 必须为 0）；
4. 相对基线的新增键及其占位符；
5. 跨语言占位符集合一致性（少一个 {0} 或编号不一律报错）；
6. 检查非字面量 TF 实参调用；
7. SettingsWindow.xaml 具名控件泄漏复查；
8. 官方插件首次引导（Onboarding）词条专项目录完整性及 4 语系覆盖与占位符一致性。
"""

import os
import pathlib
import re
import subprocess
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

root = pathlib.Path(__file__).resolve().parent.parent
src = root / "WinPieGestures"
i18n_path = src / "I18n.cs"
if not i18n_path.exists():
    print(f"Error: {i18n_path} not found")
    sys.exit(1)

i18n = i18n_path.read_text(encoding="utf-8")

# 1) 解析词条定义
#
# 两种写法都要认，因为它们在历史上都真实存在过：
#   旧：dictionary["Key"] = new Dictionary<LanguageCode, string> { [LanguageCode.ZhCn] = "…" … };
#   新：Add("Key", "…", "…", "…", "…");   —— 自 `perf(core): I18n 扁平值类型重构` 起
# 只认其中一种的代价是**静默的全绿**：整份词表解析成 0 条，于是「缺语言分支」「空值」
# 「占位符不一致」全部无从可判，而「引用但未定义」会一次性报出上千条 —— 那反而显眼。
# 真正致命的是反过来：某次重构只改写了**一部分**定义写法时，脚本仍然出数，
# 只是把没认出来的那些键悄悄算成「没人引用」。
LEGACY_PATTERN = re.compile(
    r'dictionary\["(?P<key>[^"]+)"\]\s*=\s*new Dictionary<LanguageCode,\s*string>\s*\{(?P<body>.*?)\n\t\t\};',
    re.S,
)
# 行首刻意写成「任意空白」而不是「恰好两个制表符」：那 1600 多条 `Add(` 的缩进并不齐，
# 按缩进过滤会把几十条真词条当成没定义，报出一串查不出来源的「引用但未定义」假阳性。
FLAT_PATTERN = re.compile(r'^[ \t]*Add\(\s*"(?P<key>[^"]+)",(?P<rest>.*?)\);[ \t]*$', re.M)
# C# 字符串字面量：含转义（\" 与 \\ 都要吃掉，否则引号计数会错位）
CS_STRING = re.compile(r'"((?:[^"\\]|\\.)*)"')
LANG_ORDER = ["ZhCn", "ZhTw", "En", "Ja"]


def parse_defs(text):
    """返回 (定义列表, 键→各语言字面量)。列表按文件中出现的顺序，用于数重复定义。"""
    items = []
    for m in LEGACY_PATTERN.finditer(text):
        langs = dict(re.findall(r"\[LanguageCode\.(\w+)\]\s*=\s*(.*?)(?=\n\s*\[LanguageCode\.|\Z)", m.group("body"), re.S))
        items.append((m.group("key"), {k: v.strip().rstrip(",").strip() for k, v in langs.items()}))
    for m in FLAT_PATTERN.finditer(text):
        literals = CS_STRING.findall(m.group("rest"))
        items.append((m.group("key"), dict(zip(LANG_ORDER, literals))))
    return items


defs = {}
order = []
dup = []
for key, langs in parse_defs(i18n):
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
    return {k for k, _ in parse_defs(blob)}

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
#    ② 占位符「实参本身有没有接 i18n」这件事机器查不了，本脚本刻意不假装能查：
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

# 6) 官方插件首次引导功能词条专项检查
ONBOARDING_REQUIRED_KEYS = [
    "OfficialPluginsOnboardingTitle",
    "OfficialPluginsOnboardingIntro",
    "OfficialPluginsOnboardingSecurityHeader",
    "OfficialPluginsOnboardingSecuritySource",
    "OfficialPluginsOnboardingSecurityPerms",
    "OfficialPluginsOnboardingSecurityDesc",
    "OfficialPluginsOnboardingInstallBtn",
    "OfficialPluginsOnboardingLaterBtn",
    "OfficialPluginsOnboardingRetryBtn",
    "OfficialPluginsOnboardingDoneBtn",
    "OfficialPluginsOnboardingHint",
    "OfficialPluginsOnboardingStatusReady",
    "OfficialPluginsOnboardingStatusFetchingCatalog",
    "OfficialPluginsOnboardingStatusInstallingItem",
    "OfficialPluginsOnboardingStatusEnablingItem",
    "OfficialPluginsOnboardingStatusCancelling",
    "OfficialPluginsOnboardingStatusCancelled",
    "OfficialPluginsOnboardingStatusItemEnableSuccess",
    "OfficialPluginsOnboardingStatusItemEnableFailed",
    "OfficialPluginsOnboardingStatusItemInstallSuccess",
    "OfficialPluginsOnboardingStatusItemDetail",
    "OfficialPluginsOnboardingUnknownReason",
    "OfficialPluginsOnboardingUnknownError",
    "OfficialPluginsOnboardingCatalogItemNotFound",
    "OfficialPluginsOnboardingAlreadyRunning",
    "OfficialPluginsOnboardingCatalogEmpty",
    "OfficialPluginsOnboardingAllSucceeded",
    "OfficialPluginsOnboardingPartialFailed",
    "OfficialPluginsOnboardingSummaryWithDisabled",
    "OfficialPluginsOnboardingEnableFailedGuidance",
    "OfficialPluginsOnboardingEnableFailedGuidanceWithReason",
    "OfficialPluginsOnboardingInstallFailedGuidance",
    "OfficialPluginsOnboardingOriginallyDisabledNotice",
    "OfficialPluginsOnboardingPermissionRejectedCatalog",
    "OfficialPluginsOnboardingPermissionRejectedPackage",
    "OfficialPluginsOnboardingExtraCapPackagePrompt",
    "OfficialPluginsOnboardingExtraCapPromptTitle",
    "OfficialPluginsOnboardingExtraCapPrompt",
    "OfficialPluginsStateToInstall",
    "OfficialPluginsStateInstalled",
    "OfficialPluginsStateInstalledDisabled",
    "PluginsOnboardingBannerTitle",
    "PluginsOnboardingBannerText",
    "PluginsOnboardingBannerButton",
    "OfficialPluginNameFolder",
    "OfficialPluginDescFolder",
    "OfficialPluginNameWebUrl",
    "OfficialPluginDescWebUrl",
    "OfficialPluginNameLaunch",
    "OfficialPluginDescLaunch",
    "OfficialPluginNameSystem",
    "OfficialPluginDescSystem",
    "OfficialPluginNameShellTool",
    "OfficialPluginDescShellTool",
]

print(f"\n6) 官方插件首次引导词条专项检查 ({len(ONBOARDING_REQUIRED_KEYS)} 个键)...")
onboarding_errors = []
for req_key in ONBOARDING_REQUIRED_KEYS:
    if req_key not in defs:
        onboarding_errors.append(f"缺失键定义: {req_key}")
        continue
    key_langs = defs[req_key]
    for lang in LANGS:
        if lang not in key_langs or not key_langs[lang] or key_langs[lang] in ('""', "''"):
            onboarding_errors.append(f"键 {req_key} 缺少语言 {lang} 的翻译或为空")
    ph_sets = {l: tuple(sorted(set(re.findall(r"\{(\d+)\}", key_langs.get(l, ""))))) for l in LANGS if l in key_langs}
    if len(set(ph_sets.values())) > 1:
        onboarding_errors.append(f"键 {req_key} 占位符跨语言不一致: {ph_sets}")

if onboarding_errors:
    print(f"  ❌ 引导功能专项检查失败 ({len(onboarding_errors)} 个错误):")
    for err in onboarding_errors:
        print(f"    - {err}")
else:
    print(f"  ✅ 引导功能专项检查全部通过！所有 {len(ONBOARDING_REQUIRED_KEYS)} 个词条完整定义且 4 语系占位符一致。")

# 7) 退出判定
critical_errors = len(undef) + len(ph_mismatch) + len(missing) + len(onboarding_errors)
if critical_errors > 0:
    print(f"\n[FAIL] 存在 {critical_errors} 项全局 i18n 严重缺陷。")
    sys.exit(1)
else:
    print("\n[PASS] 全局 i18n 检查与官方插件引导专项检查全部通过。")
