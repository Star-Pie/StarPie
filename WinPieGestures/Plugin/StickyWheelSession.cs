using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinPieGestures.Plugins;

/// <summary>
/// 粘滞轮盘会话 —— <see cref="StarPie.Plugin.IHostWheelService"/> 的宿主侧实现。
/// <para>
/// <b>它解决的问题</b>：悬浮球这类常驻小窗要「点一下就出轮盘」。轮盘本体
/// （<see cref="RadialWindow"/>）零输入 —— 它的一切高亮与收放都由
/// <see cref="GestureController"/> 从全局鼠标钩子里喂出来；而「伪造一串钩子事件去骗过
/// 手势状态机」是被明令禁止的路径（钩子热路径零污染，AGENTS.md §3.7），于是轮盘过去
/// 只能被手势唤出。
/// </para>
/// <para>
/// <b>做法</b>：复用轮盘的<b>呈现</b>（<c>Present/Dismiss/HighlightSector</c> 本来就只吃
/// 坐标与版本号、不校验物理按键 —— 长按唤盘路径是先例），换掉<b>输入</b>：一层全屏
/// 遮罩窗（<see cref="StickyWheelBackdrop"/>，先例：ScreenSnipWindow）用普通 WPF 鼠标事件
/// 驱动悬停高亮与按下执行。全程不进钩子、不碰手势状态机，光标也完全自由
/// （不像手势那样把光标钉在圆心）。
/// </para>
/// <para>
/// <b>命中几何与 <c>GestureController.ProcessMove</c> 同源但刻意不抽公共函数</b>：
/// ProcessMove 与实例态强耦合（蜂窝扇迟滞锁定、音量拖距接管、外甩 flick），那些是
/// 「按住不放拖着选」这套语义的组成部分；粘滞会话是「移动—按下」的另一套交互，
/// 照搬迟滞与外甩反而是错的。这里保留的是两边必须一致的<b>几何公式</b>
/// （死区、极角扇区、二级触发距离、环带命中），每处都在注释里锚定 ProcessMove 的行号；
/// 宿主改那几段公式时若不同步这里，症状是「悬浮球唤出的轮盘点不准」——
/// 好过抽一个带六个旁路开关的公共函数把两套交互焊在一起。
/// </para>
/// <para>
/// <b>单会话</b>：同一时刻全局最多一个粘滞轮盘；新的 <c>Show</c> 整体替换旧的
/// （与「同一屏幕只允许一个轮盘」的直觉一致）。真实手势与粘滞会话互斥，方向不同：
/// 粘滞期手势被 <see cref="GestureController"/> 的两处激活点用 <see cref="IsActive"/> 拦下；
/// 反之手势进行中 <c>Show</c> 直接拒绝（此刻用户按住的是物理触发键，插一个轮盘进去
/// 只会和即将成形的真轮盘打架）。
/// </para>
/// <para>
/// <b>会话不随属主插件的停用而收掉</b>，这是刻意的：选盘执行的每个动作都来自用户配置、
/// 走 <c>ActionExecutor</c> 正常队列，插件此刻不在调用栈上 —— 插件注销不该把用户
/// 正指着的那个扇区吞掉。插件想主动收盘用 <see cref="Dismiss"/>。
/// </para>
/// </summary>
internal static class StickyWheelSession
{
    /// <summary>保护 <see cref="_current"/> 与 <see cref="_versionCounter"/> 的短锁。锁内不做任何 UI 操作。</summary>
    private static readonly object Gate = new object();

    private static Session? _current;
    private static long _versionCounter;

    // 两扇窗都跨会话复用（RadialWindow 的 Present/Dismiss 契约本来就为复用设计，
    // 反复建销透明 HWND 反而引入 DWM 旧帧闪烁 —— 宿主手势路径为此专门注释过）。
    private static RadialWindow? _cachedWheel;
    private static StickyWheelBackdrop? _cachedBackdrop;

    /// <summary>
    /// 是否已有粘滞会话（含「已受理、还在排队上屏」的窗口期）。
    /// <para>
    /// <see cref="GestureController"/> 的两处手势激活点用它做互斥。判据在<b>受理时</b>置位
    /// 而不是等上屏 —— 从 BeginInvoke 到真正 Present 之间有几毫秒，真手势挤进这个窗口期
    /// 就会出现两个轮盘同屏。代价反过来也成立且可接受：受理后又因异常收摊的几毫秒里，
    /// 真手势会被误拦一次，用户下一次按下就是正常。
    /// </para>
    /// </summary>
    internal static bool IsActive
    {
        get
        {
            lock (Gate)
            {
                return _current != null;
            }
        }
    }

    /// <summary>
    /// 受理一次呼出：同步完成全部校验与会话占位，真正的建窗/呈现排到 UI 线程。
    /// 返回 <c>true</c> 只表示「已受理并排队」，语义与 <c>ActivateTaskbarSlot</c> 的
    /// 「宿主已受理」同一条纪律。
    /// </summary>
    internal static bool Show(string pluginId, double physicalX, double physicalY)
    {
        // 无界面自检模式一触即回：这里排在一切副作用之前，保证 [3j] 的
        // 「声明后放行」探针调得动、又绝不弹窗。
        if (PluginHost.HeadlessMode) return false;
        if (!double.IsFinite(physicalX) || !double.IsFinite(physicalY)) return false;

        Application? app = Application.Current;
        if (app == null) return false;

        GestureController? controller = App.MainGestureController;
        if (controller == null || controller.IsGestureActive) return false;

        if (!IsWithinVirtualScreen(physicalX, physicalY)) return false;

        Session session;
        lock (Gate)
        {
            session = new Session(pluginId, new Point(physicalX, physicalY), ++_versionCounter, _current);
            _current = session;
        }

        try
        {
            app.Dispatcher.BeginInvoke(new Action(() => Present(session)));
        }
        catch (Exception ex)
        {
            // 排队失败（进程在退出边缘）：把刚占的位还回去，别让 IsActive 永远挂着。
            lock (Gate)
            {
                if (ReferenceEquals(_current, session)) _current = null;
            }
            AppLogger.LogWarn($"[plugin] 粘滞轮盘排队失败：{ex.Message}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// 收掉属于 <paramref name="pluginId"/> 的当前会话。不是属主、或没有会话时为无害空操作。
    /// </summary>
    internal static bool Dismiss(string pluginId)
    {
        Session? session;
        lock (Gate)
        {
            session = _current;
            if (session == null || !string.Equals(session.PluginId, pluginId, StringComparison.Ordinal))
            {
                return false;
            }
            _current = null;
        }

        try
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => CloseUi(session)));
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 粘滞轮盘收摊排队失败：{ex.Message}");
        }
        return true;
    }

    // ---- 以下全部在 UI 线程执行（Present 经 BeginInvoke、遮罩事件天然在 UI 线程） ----

    private static void Present(Session session)
    {
        lock (Gate)
        {
            // 受理后被更新的会话替换、或被 Dismiss 撤回 —— 直接放弃，别把没收的摊铺开。
            if (!ReferenceEquals(_current, session)) return;
        }

        try
        {
            if (session.Replaces != null)
            {
                CloseUi(session.Replaces);
            }

            WheelProfile profile = ConfigManager.GetProfileForProcess(ActiveWindowHelper.GetActiveWindowProcessName());
            profile.EnsureLayers();
            profile.ActiveLayerIndex = 0;
            profile.SyncRootPropertiesFromActiveLayer();
            session.Profile = profile;

            // 与 ShowRadialUI 相同的顺序：遮罩先上屏，轮盘在其后进入最上层带，
            // 视觉上轮盘压住遮罩；轮盘 HWND 显式穿透鼠标，命中由遮罩收。
            StickyWheelBackdrop backdrop = _cachedBackdrop ??= new StickyWheelBackdrop();
            backdrop.Bind(session);
            backdrop.ShowSession();

            RadialWindow wheel = _cachedWheel ??= new RadialWindow(session.Center, profile);
            session.Wheel = wheel;
            wheel.Present(session.Center, profile, ConfigManager.ConfigurationRevision, session.Version);
            // Present 可能把靠边的轮盘钳进工作区；命中必须以真正画出来的中心为准。
            session.HitCenter = wheel.ActualPhysicalCenter;
            (session.DpiX, session.DpiY) = RadialWindow.GetMonitorDpiScale(session.HitCenter);
            if (session.DpiX <= 0.0) session.DpiX = 1.0;
            if (session.DpiY <= 0.0) session.DpiY = 1.0;
            // 轮盘只负责绘制；整个透明 HWND 穿透鼠标，输入由下层全屏遮罩接收。
            wheel.SetMousePassThrough(true);

            session.Live = true;
            SoundEffectManager.Play(SoundType.WheelPopup);

            // 光标可能已经停在某个扇区上（比如球就压在轮盘边缘位置），立刻补一次高亮。
            HandlePointer();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 粘滞轮盘呈现失败", ex);
            lock (Gate)
            {
                if (ReferenceEquals(_current, session)) _current = null;
            }
            CloseUi(session);
        }
    }

    /// <summary>遮罩收到鼠标移动：重算命中并在变化时推给轮盘。</summary>
    internal static void HandlePointer()
    {
        Session? session = CurrentSession();
        if (session is not { Live: true } || session.Wheel == null) return;

        HitResult hit = HitTest(session, ScreenHelper.GetCursorPhysicalPosition());
        if (hit.Sector == session.LastSector &&
            hit.Sub == session.LastSub &&
            hit.ShowSub == session.LastShowSub &&
            hit.Escaped == session.LastEscaped)
        {
            return;
        }

        int prevSector = session.LastSector;
        int prevSub = session.LastSub;
        bool prevShowSub = session.LastShowSub;
        bool prevEscaped = session.LastEscaped;
        session.LastSector = hit.Sector;
        session.LastSub = hit.Sub;
        session.LastShowSub = hit.ShowSub;
        session.LastEscaped = hit.Escaped;

        // 音效与 QueueHighlightUpdate 同一组转移规则，反馈语汇保持一致。
        if (!prevEscaped && hit.Escaped)
        {
            SoundEffectManager.Play(SoundType.GestureCancel);
        }
        else if (!prevShowSub && hit.ShowSub)
        {
            SoundEffectManager.Play(SoundType.SubmenuExpand);
        }
        else if (hit.ShowSub && prevSub != hit.Sub && hit.Sub >= 0)
        {
            SoundEffectManager.Play(SoundType.SectorHover);
        }
        else if (prevSector != hit.Sector && hit.Sector >= 0)
        {
            SoundEffectManager.Play(SoundType.SectorHover);
        }

        QueueHighlight(session, hit);
    }

    /// <summary>只排一个待执行的 Render 更新，连续鼠标事件只覆盖最新命中。</summary>
    private static void QueueHighlight(Session session, HitResult hit)
    {
        session.PendingSector = hit.Sector;
        session.PendingSub = hit.Sub;
        session.PendingShowSub = hit.ShowSub;
        session.PendingEscaped = hit.Escaped;
        if (session.HighlightScheduled) return;

        session.HighlightScheduled = true;
        try
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => ApplyPendingHighlight(session)),
                System.Windows.Threading.DispatcherPriority.Render);
        }
        catch (Exception ex)
        {
            session.HighlightScheduled = false;
            AppLogger.LogWarn($"[plugin] 粘滞轮盘高亮排队失败：{ex.Message}");
        }
    }

    private static void ApplyPendingHighlight(Session session)
    {
        session.HighlightScheduled = false;
        if (!session.Live || !ReferenceEquals(CurrentSession(), session) || session.Wheel == null) return;

        try
        {
            session.Wheel.SetOuterEscapeState(session.PendingEscaped);
            session.Wheel.HighlightSector(session.PendingSector, session.PendingSub, session.PendingShowSub);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 粘滞轮盘高亮更新失败：{ex.Message}");
        }
    }

    /// <summary>遮罩收到按下。<paramref name="leftButton"/> 为 false 时一律按取消处理。</summary>
    internal static void HandlePress(bool leftButton)
    {
        Session? session = CurrentSession();
        if (session is not { Live: true } || session.Wheel == null) return;

        HitResult hit = HitTest(session, ScreenHelper.GetCursorPhysicalPosition());

        // 先收摊再派发：与 CompleteMouseTriggerRelease 的顺序一致（CloseGestureWindow 在前），
        // 动作执行可能耗时，轮盘不该干等着。
        lock (Gate)
        {
            if (ReferenceEquals(_current, session)) _current = null;
        }
        CloseUi(session);

        if (!leftButton || hit.Escaped)
        {
            if (hit.Escaped)
            {
                DispatchCancelAction();
            }
            return;
        }

        try
        {
            ActionItem? target = hit.Sector == -1
                ? session.Profile?.GetEffectiveCenterAction()
                : session.Profile?.GetEffectiveAction(hit.Sector, hit.Sub);
            if (target != null)
            {
                ActionExecutor.EnqueueAction(target);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 粘滞轮盘派发扇区动作失败", ex);
        }
    }

    private static void CloseUi(Session session)
    {
        session.Live = false;
        try
        {
            session.Wheel?.Dismiss(session.Version);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 粘滞轮盘收盘异常：{ex.Message}");
        }
        finally
        {
            // 即使 Dismiss 失败也要恢复输入样式；否则缓存窗口会一直穿透点击。
            try { session.Wheel?.SetMousePassThrough(false); }
            catch (Exception ex) { AppLogger.LogWarn($"[plugin] 轮盘鼠标样式恢复失败：{ex.Message}"); }
        }

        if (!session.BackdropBound) return;
        try { _cachedBackdrop?.HideSession(); }
        catch (Exception ex) { AppLogger.LogWarn($"[plugin] 粘滞轮盘遮罩收摊异常：{ex.Message}"); }
    }

    private static Session? CurrentSession()
    {
        lock (Gate)
        {
            return _current;
        }
    }

    /// <summary>
    /// 环外取消的动作派发 —— 与 <c>GestureController</c> 外甩收尾同一组条件
    /// （EnableCancelAction 开着且 CancelAction 配了真东西才执行）。
    /// </summary>
    private static void DispatchCancelAction()
    {
        try
        {
            ActionItem? cancelAction = ConfigManager.CurrentConfig?.CancelAction;
            if (ConfigManager.CurrentConfig?.EnableCancelAction == true &&
                cancelAction != null && !string.IsNullOrEmpty(cancelAction.Type))
            {
                ActionExecutor.EnqueueAction(cancelAction);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 粘滞轮盘派发取消动作失败", ex);
        }
    }

    // ---- 命中测试：几何锚定 GestureController.ProcessMove（行号以 64f04ed 为准） ----

    private readonly struct HitResult
    {
        public HitResult(int sector, int sub, bool showSub, bool escaped)
        {
            Sector = sector;
            Sub = sub;
            ShowSub = showSub;
            Escaped = escaped;
        }

        public int Sector { get; }
        public int Sub { get; }
        public bool ShowSub { get; }
        public bool Escaped { get; }
    }

    private static HitResult HitTest(Session session, Point cursorPhysical)
    {
        AppConfig cfg = ConfigManager.CurrentConfig;
        WheelProfile? profile = session.Profile;
        if (cfg == null || profile == null)
        {
            return new HitResult(-1, -1, false, false);
        }

        double dx = (cursorPhysical.X - session.HitCenter.X) / session.DpiX;
        double dy = (cursorPhysical.Y - session.HitCenter.Y) / session.DpiY;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        // 死区：照抄 ProcessMove 1981~1987 的取值式，含 CoreRadius/DragThreshold 兜底链。
        double deadzone = cfg.CoreDeadzoneRadius > 0.0
            ? cfg.CoreDeadzoneRadius
            : Math.Min(cfg.CoreRadius, cfg.DragThreshold * 0.6);
        if (deadzone <= 0.0) deadzone = 15.0;
        if (dist < deadzone)
        {
            // 中心区：语义等同手势在死区内松手 → 中心动作（sector == -1 且未外甩）。
            return new HitResult(-1, -1, false, false);
        }

        double wheelRadius = cfg.WheelRadius;
        double subOuter = cfg.SubWheelOuterRadius > 0.0 ? cfg.SubWheelOuterRadius : wheelRadius * 1.55;
        bool multiTier = cfg.EnableMultiTier;
        // 环带上界取<b>可见的</b>盘缘：手势里这条线要乘 1.5 才有「外甩」可言（那是拖距语义），
        // 粘滞交互的取消就是「点到盘外」，越过视觉边缘即算 —— 锚定 ProcessMove 1994~1995 的 num9，
        // 但刻意不搬 2013 的 OuterEscapeDistance。
        double bound = multiTier ? subOuter + 20.0 : wheelRadius;
        if (dist > bound)
        {
            return new HitResult(-1, -1, false, true);
        }

        double angle = Math.Atan2(dy, dx) * (180.0 / Math.PI);
        if (angle < 0.0) angle += 360.0;
        int sectorCount = profile.SectorCount;
        if (sectorCount <= 0) sectorCount = 8;
        double slice = 360.0 / sectorCount;
        // 极角定扇区：照抄 ProcessMove 2008~2009。
        int sector = (int)Math.Floor((angle + slice / 2.0) / slice) % sectorCount;

        int sub = -1;
        bool showSub = false;
        double trigger = cfg.SubWheelTriggerDistance > 20.0 ? cfg.SubWheelTriggerDistance : 95.0;

        if (multiTier && sector < profile.Actions.Count)
        {
            ActionItem? action = profile.GetEffectiveAction(sector);
            var subActions = action?.SubActions;
            if (subActions != null && subActions.Count > 0)
            {
                bool isFan = cfg.SubmenuStyle == "Fan";
                if (isFan)
                {
                    if (dist >= trigger)
                    {
                        showSub = true;
                        sub = HitTestFanSub(session, cursorPhysical, cfg, sector, subActions.Count);
                    }
                }
                else
                {
                    // 环带二级：锚定 ProcessMove 2059~2080（含 AutoExpandSubRingsOnPopup 常显）。
                    // 不搬 2026~2038 的蜂窝扇迟滞锁定：那是拖拽中途防抖，点击式选盘没有「中途」。
                    showSub = dist >= trigger || cfg.AutoExpandSubRingsOnPopup;
                    double gap = cfg.SubWheelInnerGap >= 0.0 ? cfg.SubWheelInnerGap : 4.0;
                    if (dist >= wheelRadius + gap + 2.0)
                    {
                        double start = sector * slice - slice / 2.0;
                        double local = angle - start;
                        while (local < 0.0) local += 360.0;
                        while (local >= 360.0) local -= 360.0;
                        if (local <= slice)
                        {
                            sub = Math.Clamp((int)(local / (slice / subActions.Count)), 0, subActions.Count - 1);
                        }
                    }
                }
            }
        }

        return new HitResult(sector, sub, showSub, false);
    }

    /// <summary>
    /// 蜂窝扇二级命中 —— 锚定 <c>GestureController.HitTestFanSubs</c> 2220~2271，
    /// 去掉了迟滞偏置（同理：点击式没有拖拽中途的抖动问题），几何本身逐式对应。
    /// </summary>
    private static int HitTestFanSub(Session session, Point cursorPhysical, AppConfig cfg, int parentIndex, int subCount)
    {
        if (parentIndex < 0 || subCount <= 0) return -1;

        double dx = (cursorPhysical.X - session.HitCenter.X) / session.DpiX;
        double dy = (cursorPhysical.Y - session.HitCenter.Y) / session.DpiY;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        double outer = cfg.WheelRadius;
        double inner = cfg.InnerRadius;
        if (dist < inner + (outer - inner) * 0.40)
        {
            return -1;
        }

        int n = session.Profile?.SectorCount ?? 8;
        if (n <= 0) n = 8;
        double sectorSize = 360.0 / n;
        double midRad = parentIndex * sectorSize * (Math.PI / 180.0);

        int activeCount = Math.Min(RadialWindow.FanSubmenuSlotCount, subCount);
        if (activeCount == 1) return 0;

        double mouseAngle = Math.Atan2(dy, dx);
        int bestSub = 0;
        double bestAngleDiff = double.MaxValue;

        double ux = Math.Cos(midRad), uy = Math.Sin(midRad);
        double vx = -Math.Sin(midRad), vy = Math.Cos(midRad);
        double r = (inner + outer) / 2.0;

        string shape = cfg.Shape ?? "";
        for (int j = 0; j < activeCount; j++)
        {
            int slot = RadialWindow.GetFanSlotIndex(j, activeCount);
            (double du, double dv) = RadialWindow.GetFanSubOffsetForShape(shape, slot);

            double px = ux * (du * r) + vx * (dv * r);
            double py = uy * (du * r) + vy * (dv * r);
            double diff = Math.Abs(NormalizeAngleRad(mouseAngle - Math.Atan2(py, px)));

            if (diff < bestAngleDiff)
            {
                bestAngleDiff = diff;
                bestSub = j;
            }
        }

        return bestSub;
    }

    private static double NormalizeAngleRad(double angle)
    {
        angle %= (Math.PI * 2.0);
        if (angle > Math.PI) angle -= Math.PI * 2.0;
        else if (angle < -Math.PI) angle += Math.PI * 2.0;
        return angle;
    }

    private static bool IsWithinVirtualScreen(double physicalX, double physicalY)
    {
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0) return false;

        return physicalX >= left && physicalX < left + width &&
               physicalY >= top && physicalY < top + height;
    }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>一次会话的全部状态。除构造参数外只在 UI 线程读写。internal 是给遮罩窗绑回调用的。</summary>
    internal sealed class Session
    {
        public Session(string pluginId, Point center, long version, Session? replaces)
        {
            PluginId = pluginId;
            Center = center;
            HitCenter = center;
            Version = version;
            Replaces = replaces;
        }

        public string PluginId { get; }
        public Point Center { get; }
        public Point HitCenter { get; set; }
        public long Version { get; }
        public Session? Replaces { get; }

        public WheelProfile? Profile { get; set; }
        public RadialWindow? Wheel { get; set; }
        public double DpiX { get; set; } = 1.0;
        public double DpiY { get; set; } = 1.0;
        public bool Live { get; set; }
        public bool BackdropBound { get; set; }

        public bool HighlightScheduled { get; set; }
        public int PendingSector { get; set; }
        public int PendingSub { get; set; }
        public bool PendingShowSub { get; set; }
        public bool PendingEscaped { get; set; }

        public int LastSector { get; set; } = -2;
        public int LastSub { get; set; } = -2;
        public bool LastShowSub { get; set; }
        public bool LastEscaped { get; set; }
    }
}

/// <summary>
/// 粘滞轮盘的全屏遮罩窗。它承担会话期间的<b>全部</b>鼠标输入 ——
/// 这同时就是它的互斥语义：轮盘显示期间用户的每次点击都属于选盘，点盘外即取消。
/// <para>
/// 三个不显眼但要紧的配置：<c>WS_EX_NOACTIVATE</c>（收点击但绝不抢前台 ——
/// 抢了前台，用户配在轮盘上的「向当前窗口发快捷键」类动作就跑进了错误的窗口）、
/// 背景 <c>#01000000</c>（alpha=1：肉眼不可见，但 WPF 命中测试要求非 <c>null</c>/非全零，
/// 纯 <c>Transparent</c> 画刷配全透明背景会把整窗变成空气）、
/// 窗体边界用 <c>SetWindowPos</c> 以<b>物理像素</b>直接铺满虚拟屏
/// （混合 DPI 多屏下 WPF 的 DIU 换算反而不可靠 —— RadialWindow 的定位走的是同一条路）。
/// </para>
/// </summary>
internal sealed class StickyWheelBackdrop : Window
{
    private Action? _pointerMoved;
    private Action<bool>? _pressed;
    private bool _boundsApplied;

    internal StickyWheelBackdrop()
    {
        Title = "StarPieStickyWheelBackdrop";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = 200;
        Height = 120;

        // 用事件而不是 OnXxxButtonDown 覆写：宿主工程里 WPF 与 WinForms 同名消息类并存，
        // 覆写的签名一旦被解析到另一边就是「没有可重写的成员」，事件订阅没有这个坑。
        MouseMove += (_, _) => _pointerMoved?.Invoke();
        MouseLeftButtonDown += (_, _) => _pressed?.Invoke(true);
        MouseRightButtonDown += (_, _) => _pressed?.Invoke(false);
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                _pressed?.Invoke(false);
            }
        };
    }

    internal void Bind(StickyWheelSession.Session session)
    {
        // 遮罩不认识会话对象本身，只拿两个回调；顺带把「铺过一次屏」记在会话上，收摊时才知道要不要 Hide。
        _pointerMoved = StickyWheelSession.HandlePointer;
        _pressed = left => StickyWheelSession.HandlePress(left);
        session.BackdropBound = true;
    }

    internal void ShowSession()
    {
        if (!IsVisible)
        {
            Show();
        }
        ApplyVirtualScreenBounds();
    }

    internal void HideSession()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        nint hwnd = new WindowInteropHelper(this).Handle;
        nint ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        _ = SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)((long)ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        _boundsApplied = false;
    }

    private void ApplyVirtualScreenBounds()
    {
        nint hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0) return;

        if (_boundsApplied && _appliedLeft == left && _appliedTop == top && _appliedWidth == width && _appliedHeight == height)
        {
            return;
        }

        // HWND_TOPMOST：遮罩永远待在最上层带里（轮盘窗在其之后进入同带，视觉压其上）。
        _ = SetWindowPos(hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE);
        _appliedLeft = left;
        _appliedTop = top;
        _appliedWidth = width;
        _appliedHeight = height;
        _boundsApplied = true;
    }

    private int _appliedLeft;
    private int _appliedTop;
    private int _appliedWidth;
    private int _appliedHeight;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int GWL_EXSTYLE = -20;
    private const nint WS_EX_NOACTIVATE = 0x08000000;
    private const nint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly nint HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern nint GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern nint SetWindowLong32(nint hWnd, int nIndex, nint dwNewLong);

    private static nint GetWindowLongPtr(nint hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
}
