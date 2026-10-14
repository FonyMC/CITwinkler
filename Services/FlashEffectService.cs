using Avalonia.Media;

namespace CITwinkler.Services;

/// <summary>
/// 一次闪烁请求。由「闪烁」自动化行动发起，交由 <see cref="IFlashTarget"/> 执行。
/// </summary>
public class FlashRequest
{
    /// <summary>
    /// 本次闪烁的总时长（秒）。小于等于 0 时由目标组件按自身循环时间计算。
    /// </summary>
    public double DurationSeconds { get; init; }

    /// <summary>
    /// 在总时长内闪烁的循环次数。小于 1 时按 1 处理。
    /// </summary>
    public int Times { get; init; } = 3;

    /// <summary>
    /// 覆盖描边颜色。为 <c>null</c> 时使用组件自身设置的颜色。
    /// </summary>
    public Color? Color { get; init; }

    /// <summary>
    /// 覆盖描边粗细。为 <c>null</c> 时使用组件自身设置的粗细。
    /// </summary>
    public double? Thickness { get; init; }

    /// <summary>
    /// 是否忽略组件自身的规则集限制。
    /// </summary>
    public bool IgnoreRuleset { get; init; }
}

/// <summary>
/// 可被闪烁的组件（即「描边渐变闪烁」/「文字描边」组件实例）。
/// </summary>
public interface IFlashTarget
{
    /// <summary>
    /// 组件实例 ID。该 ID 保存在组件设置中，重启后保持不变。
    /// </summary>
    Guid InstanceId { get; }

    /// <summary>
    /// 组件实例显示名称，用于在「闪烁」行动设置中选择目标。
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 收到一次闪烁请求。
    /// </summary>
    void OnFlashRequested(FlashRequest request);

    /// <summary>
    /// 请求立即停止闪烁（行动恢复时调用）。
    /// </summary>
    void OnFlashStopped();
}

/// <summary>
/// 闪烁服务：负责收集主界面上所有可闪烁组件实例（「描边渐变闪烁」/「文字描边」），并把「闪烁」行动的请求转发给它们。
/// 由插件以单例方式注册到容器中。
/// </summary>
public class FlashEffectService
{
    private readonly object _lock = new();
    private readonly List<IFlashTarget> _targets = new();

    /// <summary>
    /// 可闪烁目标列表发生变化时触发。
    /// </summary>
    public event EventHandler? TargetsChanged;

    /// <summary>
    /// 当前已加载的可闪烁组件实例。
    /// </summary>
    public IReadOnlyList<IFlashTarget> Targets
    {
        get
        {
            lock (_lock)
            {
                return _targets.ToList();
            }
        }
    }

    internal void Register(IFlashTarget target)
    {
        lock (_lock)
        {
            if (_targets.Contains(target))
            {
                return;
            }

            _targets.Add(target);
        }

        TargetsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void Unregister(IFlashTarget target)
    {
        bool removed;
        lock (_lock)
        {
            removed = _targets.Remove(target);
        }

        if (removed)
        {
            TargetsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 向匹配的目标发起闪烁。
    /// </summary>
    /// <param name="request">闪烁请求参数。</param>
    /// <param name="targetAll">是否对所有可闪烁组件生效。</param>
    /// <param name="targetIds">在 <paramref name="targetAll"/> 为 false 时生效的目标实例 ID 列表。</param>
    public void RequestFlash(FlashRequest request, bool targetAll, IReadOnlyList<Guid>? targetIds)
    {
        foreach (var target in Targets)
        {
            if (!targetAll && (targetIds is null || !targetIds.Contains(target.InstanceId)))
            {
                continue;
            }

            try
            {
                target.OnFlashRequested(request);
            }
            catch
            {
                // 单个目标失败不应影响其它目标。
            }
        }
    }

    /// <summary>
    /// 停止匹配目标的闪烁。
    /// </summary>
    public void StopFlash(bool targetAll, IReadOnlyList<Guid>? targetIds)
    {
        foreach (var target in Targets)
        {
            if (!targetAll && (targetIds is null || !targetIds.Contains(target.InstanceId)))
            {
                continue;
            }

            try
            {
                target.OnFlashStopped();
            }
            catch
            {
                // 忽略。
            }
        }
    }

    /// <summary>
    /// 目标数量。
    /// </summary>
    public int TargetCount => Targets.Count;
}
