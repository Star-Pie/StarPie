"""
「切到英文后整页已翻译」的棘轮式（ratchet）回归。

## 为什么是台账断言，而不是「一条中文都不许出现」

StarPie 的 i18n 是**代码重设型**：XAML 里写的中文只是设计期占位，运行时由
``ApplyLocalization()`` 逐项按当前语言重设。这套机制**没有反射式兜底**，
所以只要某处漏接线，那一处就会在英文界面上原样显示中文。

漏接的量级不是「偶发一两处」，而是「一整批无名控件从没接过线」：截至本用例落地时，
`SettingsWindow.xaml` 里含中文的 ``Text``/``Content`` 有 **441 处没有 ``Name``**，
其中 **376 处词表里压根没建键**。这批控件既进不了静态差集（`scratch/check_i18n.py`
只扫带 ``Name="X"`` 的，`scratch/scan_cjk_logic.py` 只扫代码字面量），
也进不了「按 auto_id 查询」的 UI 断言 —— 静态与动态**双双漏掉**。

因此本用例不去假装「已经干净了」，而是把**已知欠账**写成台账：
``i18n_baseline.json`` 里逐页签记着「此刻英文界面上仍会显示中文的控件及其文案」。
判据是**集合包含**：

    本次观测集合 ⊆ 台账集合  ⇒ 通过（只允许变好，不允许变坏）
    出现台账里没有的中文     ⇒ 失败（新增即红）

这样它今天就能落地并立刻生效（挡住下一次新增漏接），同时不必先还清全部旧账。

## 与已有 `test_v138_i18n_multilanguage_support` 的分工

那条用例管的是**语言切换机制本身**：下拉里有几种语言、切了之后配置落盘写的是哪个代码、
切回中文是否正常 —— 每个语言档位只看 2 个控件（`SaveButton` / `NavTab0Text`）。
它抽查的那 2 个控件一直是好的，所以**前两轮整页漏接它一条都没拦住**。

本用例不碰切换机制（语言是启动前写进 `config.json` 的），只管「切完这一页到底翻干净没有」。
两者互补：切换机制坏了归 v138 报，翻得不干净归这里报。

## 已知盲区（写在这里，是为了不让人误以为「这条绿了 = 全站已翻译」）

1. **无名控件的静态差集查不出**：上面那 376 处，只有真的渲染在你所遍历到的页面、
   且真的含中文时才会被本用例看见。折叠页签里的内容不进自动化树，
   所以「遍历到哪些页」就等于「覆盖到哪些页」。
2. **``tab_4``（关于 / 更新日志）不在范围内**：该页正文是**发行说明散文**，
   项目有意只发中文（与 `CHANGELOG.md` 同源），不随界面语言切换。
   把它纳入台账会让「每次发版新增一条 release note」都变成一次失败 ——
   那会训练所有人去改台账，台账就失去了权威。代价是这一页的**标签**类文案
   （如「关于软件」「版本演进里程碑」）也一并失去覆盖，属已知欠账。
3. **``tab_5`` 的候选路径文本**里有一个表意空格（U+3000）作排版分隔符，
   与语言无关 —— 因此 CJK 判据**刻意不含 U+3000**，见 ``CJK_RE`` 处的注释。

## 怎么收紧台账（修完一批漏接之后）

    STARPIE_I18N_UPDATE_BASELINE=1 \\
        /c/Users/23836/.workbuddy/binaries/python/envs/default/Scripts/python.exe \\
        -m pytest tests/test_i18n.py -v

会用**同一条采集代码路径**重采台账（所以台账永远不会和用例逻辑漂移），
然后该用例以 ``skipped`` 结束 —— 标定不是验收，别把它当成跑过了。
跑完请 ``git diff tests/i18n_baseline.json`` 看一遍：**少了多少条**就是这一轮的真实战果。
"""

import json
import os
import re
import time
import warnings

import pytest

from conftest import launch_app

# ---------------------------------------------------------------------------
# 判据与过滤
# ---------------------------------------------------------------------------

# 含中日韩文字 / 全角标点即为命中。
#
# 刻意**不含** U+3000（表意空格）：它在本项目里被当作排版分隔符用
# （`PluginCandidatesPathText` 的 `　—　`），与界面语言无关。
# 收进来只会让「磁盘路径 + 排版分隔符」这类纯 ASCII 内容变成假阳性 ——
# 而假阳性会逼着人放宽判据，最后把真信号一起放掉。
CJK_RE = re.compile(r"[\u3001-\u303f\u3040-\u30ff\u4e00-\u9fff\uff08\uff09\uff1a\uff1b\uff0c\uff1f\uff01]")

# 与 `scratch/dump_i18n_en_cjk.py` 标定时一致：多扫几种 control_type，
# 少扫一种就可能漏掉一整类控件（例如只扫 Text 会漏掉 Button 上的中文）。
_TEXT_TYPES = (
    "Text", "Button", "CheckBox", "RadioButton", "TabItem", "ListItem",
    "Group", "Pane", "Custom", "Edit", "ComboBox", "Hyperlink", "Menu",
)

# 窗口框架按钮由 **操作系统** 提供（UIA 挂在窗口 peer 上，不是应用画的），
# 跟随系统语言而不跟随应用语言。在中文 Windows 上切英文界面，它们依然是中文 ——
# 这是假阳性，收进台账只会让人以为「界面没翻干净」。
_OS_WINDOW_CHROME_TEXTS = {"关闭", "最大化", "最小化"}

# 在范围内的页签。`tab_4` 见模块 docstring 的盲区说明 2。
_IN_SCOPE_TABS = (0, 1, 2, 3, 5)

# 侧边栏页签按钮（NavTab0..NavTab5）是**已接好 i18n** 的控件：英文界面下必须无中文。
# 用它当「语言到底切没切成英文」的前置判据，比事后数漏接条数清楚得多 ——
# 语言没切过去时，整页都是中文，得到的会是一份几百条的大 diff，看不出根因。
_LANGUAGE_PROBE_IDS = tuple(f"NavTab{i}" for i in _IN_SCOPE_TABS)

_BASELINE_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "i18n_baseline.json")
_UPDATE_ENV = "STARPIE_I18N_UPDATE_BASELINE"

_DIGITS_RE = re.compile(r"\d+")
_WS_RE = re.compile(r"\s+")


def _normalize(text):
    """
    把**每次运行都会变的东西**抹平，只留下「这句文案是什么语言」。

    - 数字（页码、扇区号、版本号、贡献者人数、`上次检查` 的时间戳）→ ``#``；
    - 连续空白 → 单个空格。

    不做这一步的话，``UpdateStatusDescText`` 里那个「上次检查: 2026-09-19 13:32」
    会让这条用例**每次运行都红**，而它红的原因与被测功能毫无关系。
    """
    text = _WS_RE.sub(" ", (text or "").strip())
    return _DIGITS_RE.sub("#", text)


def _goto_tab(win, index):
    """
    切到第 ``index`` 个页签。

    必须逐页签走一遍：WPF 折叠页签的内容**不进自动化树**（不产生 peer），
    不许诺「一次遍历拿到全窗口文案」。踩过：语言下拉在 NavTab3 里，
    站在 NavTab0 上按 auto_id 找它，得到的是「控件不存在」这种误判。
    """
    tab = win.child_window(auto_id=f"NavTab{index}", control_type="RadioButton")
    if not tab.exists(timeout=5):
        return False
    tab.select()
    # 切页会触发布局 + 若干延迟刷新，给它时间把元素取齐
    time.sleep(1.0)
    return True


def _collect_tab(win, index):
    """采集当前页签上「仍含中日韩文字」的可见控件 → ``{控件键: [文案]}``。"""
    hits = {}
    for control_type in _TEXT_TYPES:
        try:
            elements = win.descendants(control_type=control_type)
        except Exception:
            continue
        for element in elements:
            try:
                raw = element.window_text() or ""
            except Exception:
                continue
            text = _normalize(raw)
            if not text or text in _OS_WINDOW_CHROME_TEXTS or not CJK_RE.search(text):
                continue
            # 具名控件用 auto_id 作键（可精确定位到人）；无名控件退化成
            # 「按 control_type 分桶」——粒度粗一档，但这是无名控件唯一可用的身份，
            # 而漏接的重灾区恰好就在无名控件里，放弃它等于放弃这张网的主要面积。
            auto_id = (element.element_info.automation_id or "").strip()
            key = auto_id or f"<no-name:{control_type}>"
            hits.setdefault(key, set()).add(text)
    return {k: sorted(v) for k, v in hits.items()}


def _collect(win):
    """逐页签采集，返回 ``{"tab_0": {...}, ...}``。"""
    observed = {}
    for index in _IN_SCOPE_TABS:
        if not _goto_tab(win, index):
            continue
        observed[f"tab_{index}"] = _collect_tab(win, index)
    return observed


def _assert_english_mode(win):
    """
    前置判据：先把「语言切成了英文」这件事本身断言掉，再谈漏接。

    不这样做的话，`config.json` 预置失败（或日后 `Language` 字段改名）会让整页保持中文，
    得到的是一份几百条的大 diff —— 症状看起来像「i18n 全面崩了」，
    而真相是「用例自己的现场没摆对」。
    """
    for auto_id in _LANGUAGE_PROBE_IDS:
        tab = win.child_window(auto_id=auto_id, control_type="RadioButton")
        assert tab.exists(timeout=5), f"{auto_id} 未渲染，无法确认界面语言"
        text = tab.window_text() or ""
        assert not CJK_RE.search(text), (
            f"前置条件不成立：{auto_id} 的文案仍有中文（{text!r}）——\n"
            f"界面语言没切成英文，本用例的结论无效。"
            f"通常是 conftest 的沙箱没生效，或 AppConfig.Language 的字段名/取值变了。"
        )


def _load_baseline():
    if not os.path.exists(_BASELINE_PATH):
        pytest.fail(
            f"台账不存在：{_BASELINE_PATH}\n"
            f"先跑一次 STARPIE_I18N_UPDATE_BASELINE=1 pytest tests/test_i18n.py 采一份。"
        )
    with open(_BASELINE_PATH, encoding="utf-8") as fh:
        return json.load(fh)


def _write_baseline(observed):
    payload = {
        "_comment": (
            "英文界面上仍会显示中文的控件台账（棘轮基线）。"
            "由 STARPIE_I18N_UPDATE_BASELINE=1 pytest tests/test_i18n.py 采集；"
            "判据见 tests/test_i18n.py：观测集合必须是它的子集，只允许变少。"
            "数字已归一化为 #（版本号/时间戳/扇区号每次运行都会变）。"
        ),
        "_scope": {
            "language": "en",
            "tabs": [f"tab_{i}" for i in _IN_SCOPE_TABS],
            "excluded_tabs": {"tab_4": "关于/更新日志：发行说明散文，项目有意只发中文"},
        },
        "tabs": observed,
    }
    with open(_BASELINE_PATH, "w", encoding="utf-8") as fh:
        json.dump(payload, fh, ensure_ascii=False, indent=2, sort_keys=True)
        fh.write("\n")


def _format_entries(entries, limit=40):
    lines = [
        f"    [{tab}] {key}\n        {text[:110]!r}"
        for tab, key, text in entries[:limit]
    ]
    if len(entries) > limit:
        lines.append(f"    …（另有 {len(entries) - limit} 条；跑 pytest -v --tb=no -rA 看完整清单）")
    return "\n".join(lines)


def _set_config_language(env, local_app_data, language="en"):
    """
    让**程序自己**生成默认配置，只把其中的语言改成 ``language``。

    为什么不图省事直接写一个 ``{"Language": "en"}`` 的最小配置：那样文件里只有我们写的
    那一个字段，其余全靠 `AppConfig` 的字段初始化值 —— 而 `EnsureConfigHealth` 只做校验、
    **不会**补出程序自己那份默认轮盘方案。实测结果是「一个所有扇区都空着的轮盘」：
    动作名显示占位符「动作 1」、子动作数 0、级联区显示空态提示。
    于是用例会标定到一个**降级现场**，而真实用户全新安装时看到的那批 UI 一条都没覆盖到。

    多起一次进程（约 3 秒）换来的，是「标定的就是用户真正会走到的那一步」。
    第一次启动写完配置即被结束，第二次启动读到的是一份完整的默认配置，
    与首装现场逐字节一致，只差语言这一个字段。
    """
    # 第一次启动：让程序自己写出默认配置（此刻语言还是默认值，无所谓 —— 我们要的是那份默认数据）
    proc, _win = launch_app(env)
    try:
        # 配置在 LoadConfig 里同步写盘，早于主窗口可见；这里再宽裕一点即可
        time.sleep(1.0)
    finally:
        try:
            proc.kill()
            proc.wait(timeout=3)
        except Exception:
            pass

    config_path = os.path.join(str(local_app_data), "StarPie", "config.json")
    assert os.path.exists(config_path), (
        f"首次启动后没有生成 {config_path} —— 无法标定真实首装现场。"
    )
    with open(config_path, encoding="utf-8") as fh:
        config = json.load(fh)

    # 大小写不敏感地找现成的键名，而不是硬写 "Language"：
    # 日后若给 AppConfig 加了 JsonNamingPolicy，这里不会出现「改了但没生效」——
    # 那是最难查的一类失败：用例照跑、语言没换、结论全错。
    key = next((k for k in config if k.lower() == "language"), "Language")
    config[key] = language
    with open(config_path, "w", encoding="utf-8") as fh:
        json.dump(config, fh, ensure_ascii=False, indent=2)


@pytest.fixture
def english_app(sandbox_env):
    """
    以**英文界面**启动控制台，现场与「全新安装」一致。

    为什么不在启动后去点语言下拉：条目名带 emoji（``🌐 [EN] English``），
    而 emoji 在 UIA Name 上可能被剥掉，``combo.select("整串")`` 会抛
    ``IndexError: item not found``；改用 ``texts()`` + 下标又要额外开合下拉、额外等待。
    **改配置再启动**更短也更稳，而且它顺带覆盖了「配置里的语言在启动时就生效」这条真实路径。
    """
    env, local_app_data = sandbox_env
    _set_config_language(env, local_app_data, "en")

    proc, win = launch_app(env)
    try:
        # 启动即读配置定语言，但首帧布局与延迟刷新还要一点时间
        time.sleep(1.0)
        yield win, local_app_data
    finally:
        try:
            proc.kill()
            proc.wait(timeout=3)
        except Exception:
            pass


def test_english_ui_has_no_new_untranslated_text(english_app):
    """
    切英文后，逐页签采集「仍含中文的可见文案」，断言它是台账的子集。

    红了怎么办（按这个顺序想）：
    1. 这批新增文案**真的漏接了** ⇒ 去接 i18n（首选，台账不动）；
    2. 它们**有意保留中文**（品牌名、用户数据、发行说明）⇒ 在 `_OS_WINDOW_CHROME_TEXTS`
       或采集范围里说明理由后收紧判据，**不要**直接往台账里塞；
    3. 只是文案被改写了 ⇒ 跑一次 `STARPIE_I18N_UPDATE_BASELINE=1` 重采台账，
       并在提交信息里写清「为什么这次的不一样」。
    """
    win, _ = english_app
    _assert_english_mode(win)

    observed = _collect(win)

    if os.environ.get(_UPDATE_ENV):
        _write_baseline(observed)
        total = sum(len(v) for buckets in observed.values() for v in buckets.values())
        pytest.skip(f"已重采台账（{total} 条）→ {_BASELINE_PATH}；标定不是验收，请再跑一次验证。")

    baseline = _load_baseline().get("tabs", {})

    new_leaks = []
    reductions = []
    for tab, buckets in observed.items():
        base_buckets = baseline.get(tab, {})
        for key, texts in buckets.items():
            known = set(base_buckets.get(key, ()))
            new_leaks.extend((tab, key, t) for t in texts if t not in known)
        # 顺手统计「比台账少了多少」——这才是这张网存在的意义：
        # 它每次跑都在告诉你还欠多少，而不是只说一句「通过」。
        for key, texts in base_buckets.items():
            current = set(buckets.get(key, ()))
            reductions.extend((tab, key, t) for t in texts if t not in current)

    if reductions:
        warnings.warn(
            f"英文界面上的中文比台账少了 {len(reductions)} 条 —— 这是好消息，"
            f"请用 STARPIE_I18N_UPDATE_BASELINE=1 pytest tests/test_i18n.py 收紧台账，"
            f"别让这张网继续挂着一笔已经还清的账。",
            stacklevel=1,
        )

    if new_leaks:
        hint = ""
        if len(new_leaks) > 50:
            hint = (
                "\n新增条数异常多 —— 先确认界面确实切成了英文（前置断言已过，"
                "但仍可能是配置读取路径的问题），再怀疑 i18n 全面崩了。\n"
            )
        pytest.fail(
            f"英文界面上出现了 {len(new_leaks)} 条台账里没有的中文文案：\n"
            f"{_format_entries(new_leaks)}\n"
            f"{hint}\n"
            f"台账位置：{_BASELINE_PATH}\n"
            f"（台账的含义、已知盲区与收紧方法，见 tests/test_i18n.py 的模块文档字符串）"
        )
