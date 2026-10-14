using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using CITwinkler.Models;
using CITwinkler.Services;
using Microsoft.Extensions.Logging;

namespace CITwinkler.Components;

/// <summary>
/// 「文字描边」容器组件的交互逻辑。
/// 在子组件内容外围按设定间距绘制一圈实线描边，并可让该描边平滑闪烁。
/// </summary>
/// <remarks>
/// <para><b>与「描边渐变闪烁」的差别</b></para>
/// <list type="bullet">
///   <item>单色实线描边，无渐变、无亮度范围设置。</item>
///   <item>描边是一条独立于内容的线，与内容之间可调间距（5 - 500 px）。</item>
///   <item>不使用任何 <c>Effect</c>，只对独立 Border 做透明度动画，渲染开销极低。</item>
/// </list>
/// <para><b>布局约定（勿改）</b></para>
/// <para>
/// 描边线通过**负外边距**向外扩展绘制区域，组件自身的布局尺寸完全不变（与官方分组容器一致）。
/// 绝不能改用 <c>Padding</c> 来预留间距：ClassIsland 主界面容器高度受限，
/// 组件期望高度一旦超过可用高度，子组件会被排成 0×0，表现为整个组件空白。
/// </para>
/// </remarks>
[ContainerComponent]
[ComponentInfo(ComponentGuid, "文字描边", "\uE8D2",
    "容器组件。在包含的组件外围按可调间距绘制一圈实线描边，可自定义颜色、粗细与间距，并支持闪烁与规则集限制。")]
public partial class TextOutlineComponent : ComponentBase<TextOutlineComponentSettings>, IFlashTarget
{
    /// <summary>
    /// 组件 GUID。
    /// </summary>
    public const string ComponentGuid = "03800C41-0AE5-4713-864B-9A04CE4A9FB5";

    /// <summary>
    /// 动画帧间隔（20fps）。描边呼吸动画不需要更高帧率。
    /// </summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 不透明度量化级数。量化后相邻帧数值常常相同，可直接跳过重绘。
    /// </summary>
    private const int OpacityLevels = 32;

    /// <summary>
    /// 低于该不透明度时认为描边不可见，直接隐藏。
    /// </summary>
    private const double InvisibleOpacityThreshold = 0.01;

    /// <summary>
    /// 间距允许范围（像素）。
    /// </summary>
    private const double MinGap = 5;
    private const double MaxGap = 500;

    /// <summary>
    /// 粗细允许范围（像素）。
    /// </summary>
    private const double MinThickness = 1;
    private const double MaxThickness = 20;

    private readonly DispatcherTimer _timer = new() { Interval = FrameInterval };

    private bool _isAttached;
    private bool _isVisualsReady;
    private double _loopPhase;
    private DateTime _lastTick = DateTime.UtcNow;

    private bool _isBursting;
    private DateTime _burstStart;
    private double _burstDuration = 3;
    private int _burstTimes = 3;
    private Color? _burstColor;
    private double? _burstThickness;
    private bool _appliedAllowedState;

    /// <summary>
    /// 当前描边不透明度缓存，用于跳过数值未变化的帧。
    /// </summary>
    private double _currentOpacity = double.NaN;

    private IRulesetService? _rulesetService;
    private FlashEffectService? _flashService;

    private static readonly ILogger? Logger = IAppHost.TryGetService<ILogger<TextOutlineComponent>>();

    public TextOutlineComponent()
    {
        // 必须调用：Avalonia 通过它加载 TextOutlineComponent.axaml 并填充 x:Name 字段。
        // 缺失会导致组件没有任何可视树（界面空白）且访问命名控件抛 NullReferenceException。
        InitializeComponent();

        _timer.Tick += OnTick;
    }

    #region IFlashTarget

    /// <inheritdoc />
    public Guid InstanceId => Settings?.InstanceId ?? Guid.Empty;

    /// <inheritdoc />
    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Settings?.InstanceName) ? "文字描边" : Settings!.InstanceName;
            var id = InstanceId;
            return id == Guid.Empty ? name : $"{name}（{id.ToString()[..8]}）";
        }
    }

    /// <inheritdoc />
    public void OnFlashRequested(FlashRequest request)
    {
        if (!_isVisualsReady || Settings is null || !Settings.IsEnabled)
        {
            return;
        }

        var ignoreRuleset = request.IgnoreRuleset || Settings.ActionIgnoresRuleset;
        if (!ignoreRuleset && Settings.UseRuleset &&
            !(_rulesetService?.IsRulesetSatisfied(Settings.FlashRuleset) ?? true))
        {
            return;
        }

        _burstTimes = Math.Max(1, request.Times);
        _burstDuration = request.DurationSeconds > 0
            ? request.DurationSeconds
            : Math.Max(1.0, Math.Max(0.2, Settings.CycleSeconds) * _burstTimes);
        _burstColor = request.Color;
        _burstThickness = request.Thickness;

        _isBursting = true;
        _burstStart = DateTime.UtcNow;
        _lastTick = _burstStart;
        _loopPhase = 0;

        ApplyStaticVisuals();
        UpdateTimerState();
    }

    /// <inheritdoc />
    public void OnFlashStopped()
    {
        if (!_isBursting)
        {
            return;
        }

        StopBurst();
    }

    #endregion

    #region 生命周期

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isAttached = false;
        Detach();
    }

    private void Attach()
    {
        if (Settings is null)
        {
            return;
        }

        if (!HasVisualTree)
        {
            Logger?.LogWarning("「文字描边」组件的可视树未加载，组件会显示为空白。");
            return;
        }

        // 兜底：配置里 Children 为 null 时补上空集合，否则子组件永远无法显示。
        if (Settings.Children is null)
        {
            Settings.Children = [];
        }

        if (!_isVisualsReady)
        {
            _flashService ??= IAppHost.TryGetService<FlashEffectService>();
            _rulesetService ??= IAppHost.TryGetService<IRulesetService>();

            Settings.PropertyChanged += SettingsOnPropertyChanged;
            if (_rulesetService is not null)
            {
                _rulesetService.StatusUpdated += RulesetServiceOnStatusUpdated;
            }

            _isVisualsReady = true;

            _flashService?.Register(this);

            Logger?.LogInformation("「文字描边」组件已加载：子组件 {ChildCount} 个，线 {Thickness}px，间距 {Gap}px，模式 {Mode}。",
                Settings.Children?.Count ?? 0, Settings.LineThickness, Settings.LineGap, Settings.Mode);
        }

        _appliedAllowedState = IsFlashingAllowed();
        ApplyStaticVisuals();
        ApplyStaticOpacity();
        UpdateTimerState();
    }

    /// <summary>
    /// 可视树是否已加载。
    /// XAML 未加载时 <c>x:Name</c> 字段全为 null，必须直接跳过视觉相关逻辑。
    /// </summary>
    private bool HasVisualTree => OutlineHost is not null;

    private void Detach()
    {
        _timer.Stop();
        _isBursting = false;

        if (!_isVisualsReady)
        {
            return;
        }

        _isVisualsReady = false;
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        if (_rulesetService is not null)
        {
            _rulesetService.StatusUpdated -= RulesetServiceOnStatusUpdated;
        }

        _flashService?.Unregister(this);
    }

    private void SettingsOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TextOutlineComponentSettings.InstanceId))
        {
            // 实例 ID 变化时刷新服务中的登记信息，保证行动目标选择列表同步。
            _flashService?.Unregister(this);
            _flashService?.Register(this);
            return;
        }

        var affectsVisuals =
            e.PropertyName is nameof(TextOutlineComponentSettings.IsEnabled) or
                nameof(TextOutlineComponentSettings.LineColor) or
                nameof(TextOutlineComponentSettings.LineThickness) or
                nameof(TextOutlineComponentSettings.LineGap);

        var affectsAnimation =
            e.PropertyName is nameof(TextOutlineComponentSettings.Mode) or
                nameof(TextOutlineComponentSettings.CycleSeconds) or
                nameof(TextOutlineComponentSettings.UseRuleset) or
                nameof(TextOutlineComponentSettings.FlashRuleset) or
                nameof(TextOutlineComponentSettings.ActionIgnoresRuleset);

        if (!affectsVisuals && !affectsAnimation)
        {
            return;
        }

        if (affectsAnimation)
        {
            _appliedAllowedState = IsFlashingAllowed();
        }

        if (affectsVisuals)
        {
            ApplyStaticVisuals();
        }

        ApplyStaticOpacity();
        UpdateTimerState();
    }

    private void RulesetServiceOnStatusUpdated(object? sender, EventArgs e)
    {
        if (Settings is null)
        {
            return;
        }

        var allowed = IsFlashingAllowed();
        if (allowed == _appliedAllowedState)
        {
            return;
        }

        _appliedAllowedState = allowed;
        ApplyStaticOpacity();
        UpdateTimerState();
    }

    #endregion

    #region 描边渲染

    /// <summary>
    /// 判断当前是否允许显示描边 / 闪烁（受开关与规则集约束）。
    /// </summary>
    private bool IsFlashingAllowed()
    {
        var settings = Settings;
        if (settings is null || !settings.IsEnabled)
        {
            return false;
        }

        if (!settings.UseRuleset)
        {
            return true;
        }

        return _rulesetService?.IsRulesetSatisfied(settings.FlashRuleset) ?? true;
    }

    /// <summary>
    /// 应用描边颜色、粗细与间距。
    /// 只在设置变化时调用，绝不要放进动画帧里。
    /// </summary>
    private void ApplyStaticVisuals()
    {
        var settings = Settings;
        if (settings is null || !_isVisualsReady)
        {
            return;
        }

        if (OutlineHost is not { } host)
        {
            return;
        }

        var thickness = Math.Clamp(settings.LineThickness, MinThickness, MaxThickness);
        if (_isBursting && _burstThickness is { } burstThickness)
        {
            thickness = Math.Clamp(burstThickness, MinThickness, MaxThickness);
        }

        var color = _isBursting && _burstColor is { } burstColor ? burstColor : settings.LineColor;
        var gap = Math.Clamp(settings.LineGap, MinGap, MaxGap);

        if (!settings.IsEnabled)
        {
            host.IsVisible = false;
            SetFrameOpacity(0);
            return;
        }

        host.IsVisible = true;
        host.BorderThickness = new Thickness(thickness);
        host.BorderBrush = new SolidColorBrush(color);

        // 负外边距把描边线的绘制区域向外扩张，扩张量 = 间距 + 线宽，
        // 于是描边线内边缘正好落在内容边缘外「间距」像素处，且不影响布局。
        host.Margin = new Thickness(-(gap + thickness));
    }

    /// <summary>
    /// 设置描边不透明度。数值未变化时会直接跳过，避免无谓的重绘。
    /// </summary>
    private void SetFrameOpacity(double opacity)
    {
        var settings = Settings;
        if (settings is null || !_isVisualsReady)
        {
            return;
        }

        if (OutlineHost is not { } host)
        {
            return;
        }

        if (double.IsNaN(opacity))
        {
            opacity = 0;
        }

        var value = Math.Round(Math.Clamp(opacity, 0, 1) * OpacityLevels) / OpacityLevels;

        if (!double.IsNaN(_currentOpacity) && Math.Abs(value - _currentOpacity) < 1e-6)
        {
            return;
        }

        _currentOpacity = value;

        if (!settings.IsEnabled || value <= InvisibleOpacityThreshold)
        {
            host.Opacity = 0;
            return;
        }

        host.Opacity = value;
    }

    private void ApplyStaticOpacity()
    {
        var settings = Settings;
        if (settings is null || !_isVisualsReady || _isBursting)
        {
            return;
        }

        if (!_appliedAllowedState)
        {
            SetFrameOpacity(0);
            return;
        }

        SetFrameOpacity(settings.Mode == FlashMode.Hold ? 1.0 : 0.0);
    }

    #endregion

    #region 动画

    private void UpdateTimerState()
    {
        var settings = Settings;
        if (settings is null || !_isAttached)
        {
            _timer.Stop();
            return;
        }

        var needed = settings.IsEnabled &&
                     (_isBursting || (settings.Mode == FlashMode.Loop && _appliedAllowedState));

        if (needed)
        {
            _lastTick = DateTime.UtcNow;
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }

            return;
        }

        if (_timer.IsEnabled)
        {
            _timer.Stop();
        }

        ApplyStaticOpacity();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var settings = Settings;
        if (settings is null || !_isVisualsReady)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var delta = Math.Max(0, (now - _lastTick).TotalSeconds);
        _lastTick = now;

        if (_isBursting)
        {
            var elapsed = (now - _burstStart).TotalSeconds;
            if (elapsed >= _burstDuration)
            {
                StopBurst();
                return;
            }

            var cycle = _burstDuration / Math.Max(1, _burstTimes);
            var phase = elapsed % cycle / cycle;
            SetFrameOpacity(EaseInOut(phase));
            return;
        }

        if (settings.Mode != FlashMode.Loop || !_appliedAllowedState)
        {
            UpdateTimerState();
            return;
        }

        // 主界面隐藏（最小化 / 未显示）时不做无谓刷新，等重新可见再继续。
        if (!IsEffectivelyVisible)
        {
            return;
        }

        var loopCycle = Math.Max(0.2, settings.CycleSeconds);
        _loopPhase = (_loopPhase + delta / loopCycle) % 1.0;
        SetFrameOpacity(EaseInOut(_loopPhase));
    }

    private void StopBurst()
    {
        _isBursting = false;
        _burstColor = null;
        _burstThickness = null;
        _loopPhase = 0;
        ApplyStaticVisuals();
        _appliedAllowedState = IsFlashingAllowed();
        ApplyStaticOpacity();
        UpdateTimerState();
    }

    /// <summary>
    /// 余弦缓动：0 → 1 → 0 的平滑渐变。
    /// </summary>
    private static double EaseInOut(double phase) => 0.5 - 0.5 * Math.Cos(2 * Math.PI * Math.Clamp(phase, 0, 1));

    #endregion
}
