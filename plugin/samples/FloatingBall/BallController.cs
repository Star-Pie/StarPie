using System;
using System.Globalization;
using System.Windows;
using StarPie.Plugin;

namespace StarPie.Plugin.FloatingBall;

/// <summary>
/// 球的生命周期与持久化。<b>所有方法只允许在 UI 线程上调用</b>（构造 <see cref="BallWindow"/>
/// 与读写它都必须如此），从动作线程进来时由调用方经 <see cref="IDispatcherFacade"/> 切过来。
/// <para>
/// 它同时是「插件侧唯一持有 UI 状态」的地方，所以三件事都收在这里：按当前外观参数建球、
/// 把位置与外观写进插件私有设置、在停用时把窗口彻底关掉。
/// </para>
/// </summary>
internal sealed class BallController
{
    private const string KeyVisible = "ball.visible";
    private const string KeyLeft = "ball.left";
    private const string KeyTop = "ball.top";

    private readonly IPluginContext _context;

    private BallWindow? _ball;

    /// <summary>当前这个球是按哪套参数建的。用来判断「要不要重建」。</summary>
    private double _diameter;
    private double _opacity;
    private string _color = "";

    public BallController(IPluginContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 按指定外观显示球。球已经在，且外观没变 → 复用实例；外观变了 → 原地重建。
    /// </summary>
    public void Show(double diameterDiu, double opacityPercent, string color)
    {
        if (_ball != null && SameLook(diameterDiu, opacityPercent, color))
        {
            if (!_ball.IsVisible) _ball.Show();
            MarkVisible();
            return;
        }

        // 重建而不是改属性：WPF 的窗口 Close 之后不可复用，而外观参数全部定死在构造里
        // （见 BallWindow 的类注释）。位置在这条路径上被显式带过去，用户不会看到球跳回默认点。
        int? keepLeft = null;
        int? keepTop = null;
        if (_ball != null)
        {
            BallWindow.PhysicalRect rect = _ball.ReadPhysicalRect();
            if (rect.Width > 0)
            {
                keepLeft = rect.X;
                keepTop = rect.Y;
            }
        }

        CloseBall();
        CreateAndShow(diameterDiu, opacityPercent, color, keepLeft, keepTop);
    }

    /// <summary>隐藏球（保留位置与外观，下次 Show 回到原处）。</summary>
    public void Hide()
    {
        _context.Settings.Set(KeyVisible, "false");
        SaveSettings();

        if (_ball == null) return;

        CloseBall();
    }

    /// <summary>
    /// 上一轮退出时球是否处于显示状态。
    /// <para>
    /// 单独暴露出来是给调用方一个「要不要投递恢复」的判断点：读设置是内存操作，可以在
    /// 任何线程做，而建窗只能在 UI 线程做 —— 把前者留在投递之前，就不会在无事可做时
    /// 往 UI 线程队列里塞一个握着插件闭包的操作项。
    /// </para>
    /// </summary>
    public bool IsMarkedVisible() => _context.Settings.GetBool(KeyVisible, false);

    /// <summary>
    /// 按插件级设置页的外观、上次拖动到的位置把球放回来（开机预加载走这条）。
    /// <para>
    /// 外观<b>不再单独记「上次实际用了什么」</b>：那会和设置页并存出两份真相，
    /// 而用户刚在设置页改大直径、重启后发现球还是上轮的小尺寸时，只能理解为「设置没生效」。
    /// 位置不同 —— 它是用户拖动出来的事实，不是任何地方声明过的值，保留下来才符合预期。
    /// </para>
    /// </summary>
    public void Restore()
    {
        CreateAndShow(
            BallPreference.Diameter(_context),
            BallPreference.Opacity(_context),
            BallPreference.Color(_context),
            ReadInt(KeyLeft),
            ReadInt(KeyTop));
    }

    /// <summary>
    /// 现在屏幕上是否真有一颗球窗。
    /// <para>
    /// 存在的意义只有一个：让调用方在<b>投递</b>之前就能判断有没有活要干。
    /// 一次 <c>Post</c> 会把插件的闭包挂进 UI 线程的队列，直到它跑完之前宿主都判不出
    /// 「插件程序集已回收」—— 一个什么也不做的投递照样算引用残留。所以投递必须可条件化。
    /// </para>
    /// </summary>
    public bool HasWindow => _ball != null;

    /// <summary>
    /// 关掉球窗。
    /// <para>
    /// 这里用 <c>Close()</c> 而不是 <c>Hide()</c>：一个只是被藏起来的 WPF 窗口仍挂在
    /// <c>Application.Current.Windows</c> 上，那正是「插件的 AssemblyLoadContext 永远回收不掉」
    /// 的头号原因（宿主文档把它列为 WPF 插件的固有坑，且明说卸载是尽力而为）。
    /// </para>
    /// </summary>
    public void Shutdown()
    {
        CloseBall();
    }

    private void CreateAndShow(
        double diameterDiu,
        double opacityPercent,
        string color,
        int? left,
        int? top)
    {
        var ball = new BallWindow(diameterDiu, opacityPercent / 100.0, color);

        if (left is int savedX && top is int savedY) ball.RequestPhysicalLocation(savedX, savedY);

        ball.WheelRequested += RequestWheel;
        ball.HideRequested += Hide;
        ball.Moved += PersistPosition;

        _ball = ball;
        _diameter = diameterDiu;
        _opacity = opacityPercent;
        _color = color;

        ball.Show();

        MarkVisible();
    }

    private void CloseBall()
    {
        BallWindow? ball = _ball;
        _ball = null;
        if (ball == null) return;

        ball.WheelRequested -= RequestWheel;
        ball.HideRequested -= Hide;
        ball.Moved -= PersistPosition;

        try
        {
            ball.Close();
        }
        catch (Exception ex)
        {
            _context.Log.Warn($"关闭悬浮球窗口时抛了异常（已忽略）：{ex.Message}");
        }
    }

    private void RequestWheel(double physicalCenterX, double physicalCenterY)
    {
        string title = _context.I18n.T("notify.title", "悬浮球");

        if (!_context.Info.HasCapability(PluginCapability.Wheel))
        {
            // 清单没声明能力却被调到这里，只可能是有人改了 manifest 忘了同步。
            // 这里必须出声：用户点了球而屏幕上什么都没发生，是最容易被当成宿主 bug 报上来的现象。
            _context.Notify.Notify(title, _context.I18n.T("notify.no-capability", "本插件的清单里没有「轮盘呼出」能力，点球不会有任何反应。"));
            _context.Log.Warn("清单未声明 Wheel 能力，点球不会呼出轮盘。");
            return;
        }

        if (_context.Wheel.ShowWheel(physicalCenterX, physicalCenterY)) return;

        // 失败原因在宿主侧有好几种（正有一次真手势在进行 / 坐标不在任何显示器内 / 无界面模式），
        // 插件无法区分，所以措辞刻意写成「现在不行」而不是编一个具体理由。
        _context.Log.Warn($"轮盘呼出被宿主拒绝：球心=({physicalCenterX},{physicalCenterY})");
        _context.Notify.Notify(title, _context.I18n.T("notify.wheel-refused", "轮盘现在呼不出来，稍等一下再点。"));
    }

    private void PersistPosition()
    {
        if (_ball == null) return;

        BallWindow.PhysicalRect rect = _ball.ReadPhysicalRect();
        if (rect.Width <= 0) return;

        _context.Settings.Set(KeyLeft, rect.X.ToString(CultureInfo.InvariantCulture));
        _context.Settings.Set(KeyTop, rect.Y.ToString(CultureInfo.InvariantCulture));
        SaveSettings();
    }

    /// <summary>记住「球此刻是显示着的」，供下次开机预加载判断要不要把球放回来。</summary>
    private void MarkVisible()
    {
        _context.Settings.Set(KeyVisible, "true");
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            _context.Settings.Save();
        }
        catch (Exception ex)
        {
            // 设置写不进去不该让球消失：位置丢一次是可接受的，球突然没了是插件坏了。
            _context.Log.Warn($"保存悬浮球设置失败（本次仍继续显示）：{ex.Message}");
        }
    }

    private int? ReadInt(string key)
    {
        string? raw = _context.Settings.Get(key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    private bool SameLook(double diameterDiu, double opacityPercent, string color) =>
        Math.Abs(_diameter - diameterDiu) < 0.01
        && Math.Abs(_opacity - opacityPercent) < 0.01
        && string.Equals(_color, color, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 默认外观。刻意与宿主的轮盘视觉无关 —— 球只需要「在屏幕上不刺眼、在浅色深色背景上都看得见」。
/// </summary>
internal static class Defaults
{
    public const double DiameterDiu = 56;
    public const double OpacityPercent = 80;
    public const string Color = "#5884DE";

    public const double DiameterMin = 24;
    public const double DiameterMax = 160;
    public const double OpacityMin = 20;
    public const double OpacityMax = 100;
}
