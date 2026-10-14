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
/// 「描边渐变闪烁」容器组件的交互逻辑。
/// 通过 <see cref="DropShadowEffect"/> 在子组件内容（含文字字形）边缘绘制描边，
/// 并把描边不透明度在最小 / 最大值之间做平滑渐变，形成「渐变闪烁」。
/// </summary>
/// <remarks>
/// <para><b>性能要点（修改前务必阅读）</b></para>
/// <para>
/// Avalonia 会为带 <c>Effect</c> 的元素建立独立离屏图层并在其上执行模糊滤镜，
/// 因此「每次修改滤镜属性」的代价 = 重新合成整棵子树 + 重新模糊整个图层。
/// 之前的实现每 33ms 修改一次滤镜不透明度、且嵌套两层滤镜，在低功耗设备上会把 CPU 打满。
/// 现版本做了如下约束，请勿回退：
/// </para>
/// <list type="number">
///   <item>描边宿主用 <c>VerticalAlignment="Stretch"</c> 且**不设置 Padding**：布局与官方「分组容器」逐像素一致，
///         再加上 <c>ClipToBounds="True"</c>，离屏图层尺寸被限制在组件自身范围内。
///         切勿改回 Top / Center，也切勿用 Padding 预留光晕空间 —— 见 FlashContentComponent.axaml 中的说明。</item>
///   <item>不透明度做量化，数值未变化时直接跳过刷新。</item>
///   <item>不透明度为 0（描边完全不可见）时直接卸下滤镜，完全不产生渲染开销。</item>
///   <item>仅在启用「渐变描边」时才挂载第二层滤镜。</item>
///   <item>模糊半径设上限，且动画帧率降到 20fps。</item>
///   <item>规则集判定结果做缓存，不在每帧重复求值。</item>
/// </list>
/// </remarks>
[ContainerComponent]
[ComponentInfo(ComponentGuid, "描边渐变闪烁", "\uE945",
    "容器组件。包含的组件会在其内容字符边缘进行描边渐变闪烁，可自定义循环时间、颜色与粗细，并可用规则集限制闪烁条件。")]
public partial class FlashContentComponent : ComponentBase<FlashContentComponentSettings>, IFlashTarget
{
    /// <summary>
    /// 组件 GUID。
    /// </summary>
    public const string ComponentGuid = "8E4F2C31-6A7D-4E5B-9C3A-2D1E7B5A9F60";

    /// <summary>
    /// 动画帧间隔（20fps）。
    /// 描边呼吸类动画不需要更高帧率，而每次刷新都会让带滤镜的子树重新合成，帧率越高 CPU 占用越高。
    /// </summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 不透明度量化级数。量化后相邻帧数值常常相同，可直接跳过重绘。
    /// </summary>
    private const int OpacityLevels = 32;

    /// <summary>
    /// 模糊半径上限。模糊半径越大，离屏图层越大、模糊开销越高。
    /// </summary>
    private const double MaxBlurRadius = 18;

    /// <summary>
    /// 低于该不透明度时认为描边不可见，直接卸下滤镜。
    /// </summary>
    private const double InvisibleOpacityThreshold = 0.01;

    /// <summary>
    /// 允许挂载滤镜的最大渲染面积（像素）。
    /// 该值约束的是离屏缓冲的内存占用：1920×780 约 150 万像素（单图层约 6 MB）。
    /// 布局异常（例如宿主容器给出无限大的可用尺寸）时，宁可不出描边也不能生成超大离屏图层。
    /// </summary>
    private const double MaxEffectPixels = 1_500_000;

    private readonly DispatcherTimer _timer = new() { Interval = FrameInterval };
    private readonly DropShadowEffect _innerEffect = new();
    private readonly DropShadowEffect _outerEffect = new();

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

    // ---- 渲染状态缓存：避免重复设置相同的滤镜参数（每次设置都会触发整棵带滤镜子树重绘） ----
    private bool _effectsAttached;
    private bool _useOuterLayer;
    private double _currentOpacity = double.NaN;
    private bool _loggedEffectAreaSkip;

    private IRulesetService? _rulesetService;
    private FlashEffectService? _flashService;

    /// <summary>
    /// 诊断日志。写入 ClassIsland 主日志，便于定位「组件空白」这类可视树问题。
    /// </summary>
    private static readonly ILogger? Logger = IAppHost.TryGetService<ILogger<FlashContentComponent>>();

    public FlashContentComponent()
    {
        // 必须调用 InitializeComponent()：Avalonia 通过它加载 FlashContentComponent.axaml 并填充 x:Name 字段。
        // 一旦缺失，本组件就没有任何可视树 —— 表现为「在界面上完全空白」，且访问命名控件时会抛 NullReferenceException。
        InitializeComponent();

        _timer.Tick += OnTick;

        // 描边需要四周均匀扩散，偏移固定为 0。
        _innerEffect.OffsetX = 0;
        _innerEffect.OffsetY = 0;
        _outerEffect.OffsetX = 0;
        _outerEffect.OffsetY = 0;
    }

    #region IFlashTarget

    /// <inheritdoc />
    public Guid InstanceId => Settings?.InstanceId ?? Guid.Empty;

    /// <inheritdoc />
    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Settings?.InstanceName) ? "描边渐变闪烁" : Settings!.InstanceName;
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
            // 可视树缺失（XAML 未加载）时必须停在这里：
            // 继续执行会在 ApplyStaticVisuals 访问命名控件时抛 NullReferenceException。
            Logger?.LogWarning("「描边渐变闪烁」组件的可视树未加载，组件会显示为空白。");
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

            Logger?.LogInformation("「描边渐变闪烁」组件已加载：子组件 {ChildCount} 个，描边 {Thickness}px，模式 {Mode}。",
                Settings.Children?.Count ?? 0, Settings.StrokeThickness, Settings.Mode);
        }

        _appliedAllowedState = IsFlashingAllowed();
        ApplyStaticVisuals();
        ApplyStaticOpacity();
        UpdateTimerState();
    }

    /// <summary>
    /// 可视树是否已加载。
    /// XAML 未加载（例如漏调 <see cref="Avalonia.Markup.Xaml.AvaloniaXamlLoader"/> 对应的 <c>InitializeComponent</c>）时，
    /// <c>x:Name</c> 字段全为 null，此时必须直接跳过视觉相关逻辑，否则会在布局回调里抛 NullReferenceException。
    /// </summary>
    private bool HasVisualTree => OuterStrokeHost is not null && InnerStrokeHost is not null;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // 尺寸变化后重新校验渲染面积：布局若给出异常尺寸（例如无限大），
        // 必须在挂载滤镜前把它挡下来，否则会生成超大离屏图层。
        if (change.Property == BoundsProperty && _effectsAttached)
        {
            SetEffectsAttached(true);
        }
    }

    private void Detach()
    {
        _timer.Stop();
        _isBursting = false;

        // 组件离开可视树时彻底卸下滤镜，避免遗留的离屏图层继续被合成。
        SetEffectsAttached(false);

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
        if (e.PropertyName == nameof(FlashContentComponentSettings.InstanceId))
        {
            // 实例 ID 变化时刷新服务中的登记信息，保证行动目标选择列表同步。
            _flashService?.Unregister(this);
            _flashService?.Register(this);
            return;
        }

        var affectsVisuals =
            e.PropertyName is nameof(FlashContentComponentSettings.IsEnabled) or
                nameof(FlashContentComponentSettings.StrokeColor) or
                nameof(FlashContentComponentSettings.StrokeColorSecondary) or
                nameof(FlashContentComponentSettings.UseGradientStroke) or
                nameof(FlashContentComponentSettings.StrokeThickness);

        var affectsAnimation =
            e.PropertyName is nameof(FlashContentComponentSettings.Mode) or
                nameof(FlashContentComponentSettings.CycleSeconds) or
                nameof(FlashContentComponentSettings.MinStrokeOpacity) or
                nameof(FlashContentComponentSettings.MaxStrokeOpacity) or
                nameof(FlashContentComponentSettings.UseRuleset) or
                nameof(FlashContentComponentSettings.FlashRuleset) or
                nameof(FlashContentComponentSettings.ActionIgnoresRuleset);

        if (!affectsVisuals && !affectsAnimation)
        {
            return;
        }

        if (affectsAnimation)
        {
            // 规则集判定结果缓存下来，避免在动画帧里反复求值。
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
    /// 应用描边颜色、粗细等静态视觉参数。
    /// 只在设置变化时调用，绝不要放进动画帧里。
    /// 注意：本方法**不得**修改任何布局相关属性（Padding / Margin / 对齐方式），
    /// 否则会改变组件在 ClassIsland 主界面上的尺寸与位置。
    /// </summary>
    private void ApplyStaticVisuals()
    {
        var settings = Settings;
        if (settings is null || !_isVisualsReady)
        {
            return;
        }

        // 可视树缺失时命名控件为 null，直接跳过。
        if (InnerStrokeHost is null || OuterStrokeHost is null)
        {
            return;
        }

        var thickness = Math.Clamp(settings.StrokeThickness, 0, 40);
        if (_isBursting && _burstThickness is { } burstThickness)
        {
            thickness = Math.Clamp(burstThickness, 0, 40);
        }

        var burstColor = _isBursting ? _burstColor : null;
        var primary = burstColor ?? settings.StrokeColor;

        if (!settings.IsEnabled || thickness <= 0.01)
        {
            _useOuterLayer = false;
            SetEffectsAttached(false);
            return;
        }

        // 只有「渐变描边」且两种颜色确实不同时才挂第二层滤镜；否则第二层纯属浪费（成本翻倍）。
        _useOuterLayer = settings.UseGradientStroke && burstColor is null &&
                         settings.StrokeColorSecondary != primary;

        var blur = Math.Clamp(thickness * 1.1, 0.5, MaxBlurRadius);

        _innerEffect.Color = primary;
        _innerEffect.BlurRadius = blur;
        _outerEffect.Color = settings.StrokeColorSecondary;
        _outerEffect.BlurRadius = Math.Min(MaxBlurRadius * 1.7, blur * 1.7);

        // 只有当前确实可见时才挂载滤镜。
        SetEffectsAttached(_currentOpacity > InvisibleOpacityThreshold);
    }

    /// <summary>
    /// 设置描边不透明度。数值未变化时会直接跳过，避免无谓的重绘。
    /// </summary>
    private void SetStrokeOpacity(double opacity)
    {
        var settings = Settings;
        if (settings is null || !_isVisualsReady)
        {
            return;
        }

        var value = Math.Clamp(opacity, 0, 1);
        value = Math.Round(value * OpacityLevels) / OpacityLevels;

        if (!double.IsNaN(_currentOpacity) && Math.Abs(value - _currentOpacity) < 1e-6)
        {
            return;
        }

        _currentOpacity = value;

        if (!settings.IsEnabled || value <= InvisibleOpacityThreshold)
        {
            // 描边完全不可见时卸下滤镜：不产生任何模糊 / 图层开销。
            SetEffectsAttached(false);
            return;
        }

        _innerEffect.Opacity = value;
        // 外层颜色略淡，形成由内向外的渐变过渡。
        _outerEffect.Opacity = value * 0.85;
        SetEffectsAttached(true);
    }

    private void SetEffectsAttached(bool attached)
    {
        if (InnerStrokeHost is null || OuterStrokeHost is null)
        {
            _effectsAttached = false;
            return;
        }

        var innerHost = InnerStrokeHost;
        var outerHost = OuterStrokeHost;

        if (!attached)
        {
            if (!_effectsAttached)
            {
                return;
            }

            _effectsAttached = false;
            outerHost.Effect = null;
            innerHost.Effect = null;
            return;
        }

        // 最后一道保险：尺寸异常时拒绝挂载滤镜。
        // 布局若给出无限大（或极大）的可用尺寸，离屏图层会膨胀到不可接受的地步，
        // 此时宁可不出描边，也不能让超大离屏缓冲把 CPU / 显存打满。
        if (!IsEffectAreaSane(outerHost, out var area))
        {
            if (_effectsAttached)
            {
                _effectsAttached = false;
                outerHost.Effect = null;
                innerHost.Effect = null;
            }

            if (!_loggedEffectAreaSkip)
            {
                _loggedEffectAreaSkip = true;
                Logger?.LogWarning("「描边渐变闪烁」组件当前渲染区域过大（约 {Area:F0} 像素），已暂时停用描边以免占用过多资源。", area);
            }

            return;
        }

        _effectsAttached = true;
        innerHost.Effect = _innerEffect;
        outerHost.Effect = _useOuterLayer ? _outerEffect : null;
    }

    /// <summary>
    /// 判断当前渲染区域面积是否适合挂载滤镜。
    /// </summary>
    private static bool IsEffectAreaSane(Control host, out double area)
    {
        var bounds = host.Bounds;
        area = bounds.Width * bounds.Height;
        return double.IsFinite(area) && area <= MaxEffectPixels;
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
            SetStrokeOpacity(0);
            return;
        }

        SetStrokeOpacity(settings.Mode == FlashMode.Hold ? settings.MaxStrokeOpacity : settings.MinStrokeOpacity);
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
            SetStrokeOpacity(LerpOpacity(settings, EaseInOut(phase)));
            return;
        }

        if (settings.Mode != FlashMode.Loop || !_appliedAllowedState)
        {
            UpdateTimerState();
            return;
        }

        // 主界面隐藏（最小化 / 未显示）时不做无谓的滤镜刷新，等重新可见再继续。
        if (!IsEffectivelyVisible)
        {
            return;
        }

        var loopCycle = Math.Max(0.2, settings.CycleSeconds);
        _loopPhase = (_loopPhase + delta / loopCycle) % 1.0;
        SetStrokeOpacity(LerpOpacity(settings, EaseInOut(_loopPhase)));
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

    private static double LerpOpacity(FlashContentComponentSettings settings, double amount)
    {
        var min = Math.Clamp(settings.MinStrokeOpacity, 0, 1);
        var max = Math.Clamp(settings.MaxStrokeOpacity, 0, 1);
        if (max < min)
        {
            (min, max) = (max, min);
        }

        return min + (max - min) * amount;
    }

    #endregion
}
