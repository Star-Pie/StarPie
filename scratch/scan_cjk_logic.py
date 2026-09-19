"""扫描「中文参与控制流判断」的地雷，并核对动作默认名判据的覆盖率。

## 为什么需要这个脚本

把 UI 文案接入 i18n 之前，必须先确认这些文案**没有被代码当作判断依据**。
一旦某个 `.Contains("未启用")` / `case "锁屏"` / `== "动作"` 依赖的是显示文案，
把它换成 `I18n.T(...)` 之后，非中文语言下判断会**静默失效** ——
没有编译错误、没有异常、界面看起来正常，行为悄悄退化。
这是本项目最不能接受的一类缺陷。

## 判读方式

命中的每一处都要人工回答：「这里比的是**身份**还是**文案**？」

- 比身份（数据里的历史字符串、第三方软件名、OCR 结果）→ 可接受，登记进 `ACCEPTED`；
- 比文案（赋值点走 `I18n.T`，判断走硬编码中文）→ 缺陷，收敛到与语言无关的判据。

**关键判据不是「有没有中文」，而是「赋值点与判断点是否同源」**。
同源硬编码（两边都写死中文）不会因语言切换而失效；
真正会炸的是「赋值走词条、判断走字面量」——
本脚本的两条机器护栏正是围绕这一点：
① 默认名一致性（`Name = I18n.T(...)` 的键必须都在 `ActionNameDefaults` 里）；
② 占位名判据不许再手抄（新增的中文比较若落在动作名上，会被上面那条抓出来）。

## 用法

    python scratch/scan_cjk_logic.py               # 全量清单
    python scratch/scan_cjk_logic.py --summary     # 只按文件汇总
    python scratch/scan_cjk_logic.py --pending     # 只看「待判定」的（CI 友好：非零即须处理）
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "WinPieGestures"
CJK = r"[\u4e00-\u9fff]"

PATTERNS = [
    ("Contains", re.compile(rf'\.Contains\(\s*"([^"]*{CJK}[^"]*)"')),
    ("StartsWith", re.compile(rf'\.StartsWith\(\s*"([^"]*{CJK}[^"]*)"')),
    ("EndsWith", re.compile(rf'\.EndsWith\(\s*"([^"]*{CJK}[^"]*)"')),
    ("IndexOf", re.compile(rf'\.IndexOf\(\s*"([^"]*{CJK}[^"]*)"')),
    ("== 字符串", re.compile(rf'==\s*"([^"]*{CJK}[^"]*)"')),
    ("!= 字符串", re.compile(rf'!=\s*"([^"]*{CJK}[^"]*)"')),
    ("case", re.compile(rf'case\s+"([^"]*{CJK}[^"]*)"')),
    ("Equals", re.compile(rf'\.Equals\(\s*"([^"]*{CJK}[^"]*)"')),
    ("switch 表达式", re.compile(rf'^\s*"([^"]*{CJK}[^"]*)"\s*=>')),
]

# ---------------------------------------------------------------------------
# 已确认可接受的项：(文件名, 字面量) -> 理由
#
# 判据是「赋值点与判断点同源」：这些中文要么是老配置里已经存着的字符串，
# 要么是第三方数据（软件名 / Release 标题 / OCR 结果）。
# 界面语言怎么切都不会让它们对不上，所以**不是** i18n 的一部分。
#
# 登记在这里而不是散在源码里，是因为「哪里可以放过」这件事需要能一眼看全 ——
# 挨个文件加注释的做法在第十个文件之后就没人找得到了。
# ---------------------------------------------------------------------------
ACCEPTED = {
    # 系统预设：老配置的 Action.Parameter 里存的就是这些中文名。
    ("ActionExecutor.cs", "starpie控制台"): "系统预设的历史中文名（老配置数据）",
    ("ActionExecutor.cs", "控制台"): "同上",
    ("ActionExecutor.cs", "文件秒搜"): "同上",
    ("ActionExecutor.cs", "快速秒搜"): "同上",
    ("ActionExecutor.cs", "原生秒搜"): "同上",
    ("ActionExecutor.cs", "锁定屏幕"): "同上",
    ("ActionExecutor.cs", "锁屏"): "同上",
    ("ActionExecutor.cs", "睡眠"): "同上",
    ("ActionExecutor.cs", "休眠"): "同上",
    ("ActionExecutor.cs", "重启"): "同上",
    ("ActionExecutor.cs", "关机"): "同上",

    # 程序挑选器：过滤第三方软件，匹配的是别人的软件名。
    ("ProgramPickerWindow.xaml.cs", "卸载"): "第三方软件名里的中文（过滤卸载程序）",
    ("ProgramPickerWindow.xaml.cs", "意见反馈"): "同上",
    ("ProgramPickerWindow.xaml.cs", "修复"): "同上",
    ("ProgramPickerWindow.xaml.cs", "使用说明"): "同上",
    ("ProgramPickerWindow.xaml.cs", "用户手册"): "同上",
    ("ProgramPickerWindow.xaml.cs", "帮助"): "同上",
    ("ProgramPickerWindow.xaml.cs", "官方网站"): "同上",
    ("ProgramPickerWindow.xaml.cs", "访问官网"): "同上",
    ("NativeSearchEngine.cs", "卸载"): "同上",

    # 更新检查：匹配 GitHub Release 的标题。
    ("UpdateManager.cs", "内测"): "GitHub Release 标题里的中文（发布者写的）",
    ("UpdateManager.cs", "尝鲜"): "同上",

    # OCR：匹配识别到的屏幕文字，而且只在 #if DEBUG 的调试打印里。
    ("OcrManager.cs", "功能"): "OCR 识别结果里的文字，且仅 DEBUG 调试打印",
    ("OcrManager.cs", "快捷"): "同上",
    ("OcrManager.cs", "[OCR 识别异常]"): "自己生成的标记串，用作数据前缀而非 UI 文案",

    # 配置迁移与轮盘配置名：都是「已存在于配置里的字符串」。
    ("ConfigManager.cs", "平铺"): "老配置里的动作名（迁移判断）",
    ("SettingsWindow.xaml.cs", "平铺"): "同上",
    ("SettingsWindow.xaml.cs", "自定义配置_"): "轮盘配置名的历史占位串",
    ("SettingsWindow.xaml.cs", " - 副本"): "轮盘配置名的复制后缀（赋值点也是硬编码）",
}

# 「选类型就自动填名」的赋值点：这些键名必须出现在 ActionNameDefaults 里，
# 否则那个默认名会被当成用户自定义，换类型时不再更新。
# 注意要同时认 `Name = ...` 与 `item.Name = ...` 两种写法，
# 但必须排除 `DisplayName = ...`（那是插件/预设的展示名，不是动作名）。
NAME_ASSIGN_PATTERN = re.compile(r'(\w*)\s*\.?\s*Name\s*=\s*I18n\.T\(\s*"([^"]+)"')
FILLED_KEYS_BLOCK = re.compile(r"FilledNameKeys\s*=\s*\{(.*?)\};", re.S)


def is_comment(line: str) -> bool:
    s = line.lstrip()
    return s.startswith("//") or s.startswith("///") or s.startswith("*")


def collect_hits():
    hits_by_file: dict[str, list[tuple[int, str, str, str]]] = {}

    for path in sorted(SRC.rglob("*.cs")):
        if "obj" in path.parts or "bin" in path.parts:
            continue

        text = path.read_text(encoding="utf-8", errors="ignore")

        for lineno, line in enumerate(text.splitlines(), 1):
            if is_comment(line):
                continue

            for tag, pattern in PATTERNS:
                for match in pattern.finditer(line):
                    hits_by_file.setdefault(path.name, []).append(
                        (lineno, tag, match.group(1), line.strip())
                    )

    return hits_by_file


def report_cjk_logic(hits_by_file, summary_only: bool, pending_only: bool) -> int:
    total = sum(len(v) for v in hits_by_file.values())
    accepted = sum(
        1
        for name, hits in hits_by_file.items()
        for _, _, literal, _ in hits
        if (name, literal) in ACCEPTED
    )
    pending = total - accepted

    print(f"命中「中文参与控制流判断」合计：{total} 处"
          f"（已确认可接受 {accepted} / 待判定 {pending}）\n")

    if pending_only:
        for name in sorted(hits_by_file, key=lambda n: -len(hits_by_file[n])):
            rest = [h for h in hits_by_file[name] if (name, h[2]) not in ACCEPTED]
            if not rest:
                continue
            print(f"### {name}  ({len(rest)} 处待判定)")
            for lineno, tag, literal, source in rest:
                print(f"  {lineno:5d} [{tag}] \"{literal}\"")
                print(f"        {source[:150]}")
            print()
        return pending

    for name in sorted(hits_by_file, key=lambda n: -len(hits_by_file[n])):
        hits = hits_by_file[name]
        print(f"### {name}  ({len(hits)} 处)")
        if summary_only:
            continue
        for lineno, tag, literal, source in hits:
            reason = ACCEPTED.get((name, literal))
            if reason:
                print(f"  {lineno:5d} [{tag}] \"{literal}\"  ← 可接受：{reason}")
            else:
                print(f"  {lineno:5d} [{tag}] \"{literal}\"  ← 待判定")
                print(f"        {source[:150]}")
        print()

    return pending


def report_name_defaults() -> int:
    """核对「默认名判据」的覆盖率。不一致的条数即返回值。"""
    config_path = SRC / "ActionNameDefaults.cs"
    if not config_path.exists():
        print("找不到 ActionNameDefaults.cs —— 默认名判据收敛的成果被删了？")
        return 1

    block = FILLED_KEYS_BLOCK.search(config_path.read_text(encoding="utf-8"))
    declared = set(re.findall(r'"([^"]+)"', block.group(1))) if block else set()

    assigned: dict[str, set[str]] = {}
    for path in sorted(SRC.rglob("*.cs")):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        if path.name == "ActionNameDefaults.cs":
            continue  # 它自己只声明键名，不是赋值点

        for lineno, line in enumerate(path.read_text(encoding="utf-8", errors="ignore").splitlines(), 1):
            if is_comment(line):
                continue
            for match in NAME_ASSIGN_PATTERN.finditer(line):
                if match.group(1).endswith("Display"):
                    continue  # DisplayName：展示名，不是动作名
                assigned.setdefault(match.group(2), set()).add(path.name)

    missing = sorted(set(assigned) - declared)
    stale = sorted(declared - set(assigned))

    print(f"\n默认名判据：代码里用 I18n 填名的键 {len(assigned)} 个，"
          f"ActionNameDefaults 声明 {len(declared)} 个")

    for key in sorted(assigned):
        where = ", ".join(sorted(assigned[key]))
        if key in declared:
            print(f"  ✓ {key:32s} <- {where}")
        else:
            print(f"  ✗ {key:32s} <- {where}  漏加：该默认名会被当成用户自定义名")

    for key in stale:
        print(f"  · {key:32s} 声明了但没有赋值点（历史键？删掉或补注释说明）")

    return len(missing)


def main() -> int:
    summary_only = "--summary" in sys.argv
    pending_only = "--pending" in sys.argv

    pending = report_cjk_logic(collect_hits(), summary_only, pending_only)
    missing = report_name_defaults()

    print()
    if missing:
        print(f"[X] 有 {missing} 个默认名键漏加进 ActionNameDefaults —— 会让「换类型时自动更新名字」失效")
    if pending:
        print(f"[X] 有 {pending} 处中文判断待判定 —— 要么收敛到语言无关判据，要么登记进 ACCEPTED 并写明理由")
    if not pending and not missing:
        print("[OK] 中文判断全部已分类，默认名判据覆盖完整")

    return 1 if (pending or missing) else 0


if __name__ == "__main__":
    raise SystemExit(main())
