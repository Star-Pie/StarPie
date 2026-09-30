namespace StarPie.Plugin;

/// <summary>本次目标进程启动的权限模式；不改变宿主或插件进程本身的权限。</summary>
public enum ProcessLaunchMode
{
    /// <summary>沿用对应宿主功能的原有默认行为。</summary>
    Default = 0,
    /// <summary>请求管理员启动；取消或失败时不得回退为其他权限。</summary>
    Administrator = 1,
    /// <summary>通过普通用户 Shell 启动；失败时不得回退到宿主进程权限。</summary>
    StandardUser = 2,
}
