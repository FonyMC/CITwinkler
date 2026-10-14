using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Models;
using ClassIsland.Core.Models.Components;
using ClassIsland.Core.Models.Ruleset;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CITwinkler.Models;

/// <summary>
/// 闪烁模式。
/// </summary>
public enum FlashMode
{
    /// <summary>
    /// 按循环时间在最小 / 最大描边不透明度之间循环渐变闪烁。
    /// </summary>
    Loop = 0,

    /// <summary>
    /// 一直保持最大描边不透明度（常亮描边）。
    /// </summary>
    Hold = 1
}

/// <summary>
/// 「描边渐变闪烁」组件设置。
/// 注意：类型名 / 属性名会参与 JSON 序列化，改动会导致已有配置失效。
/// </summary>
public partial class FlashContentComponentSettings : ObservableObject, IComponentContainerSettings
{
    /// <summary>
    /// 是否启用描边（关闭后组件退化为普通分组容器）。
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>
    /// 组件实例名称，仅用于在「闪烁」行动中标识这个组件。
    /// </summary>
    [ObservableProperty]
    private string _instanceName = "描边渐变闪烁";

    /// <summary>
    /// 组件实例 ID。首次创建时生成，之后随配置持久化，用于「闪烁」行动定位目标。
    /// </summary>
    [ObservableProperty]
    private Guid _instanceId = Guid.NewGuid();

    /// <summary>
    /// 描边主颜色。
    /// </summary>
    [ObservableProperty]
    private Color _strokeColor = Color.FromArgb(255, 0x6E, 0xC7, 0xFF);

    /// <summary>
    /// 描边第二颜色（仅在启用渐变描边时作为外层颜色使用）。
    /// </summary>
    [ObservableProperty]
    private Color _strokeColorSecondary = Color.FromArgb(255, 0xC2, 0x6E, 0xFF);

    /// <summary>
    /// 是否使用双色渐变描边（内层主色 + 外层第二色，形成颜色过渡）。
    /// </summary>
    [ObservableProperty]
    private bool _useGradientStroke;

    /// <summary>
    /// 描边粗细（像素，约等于描边的柔和扩散半径）。
    /// </summary>
    [ObservableProperty]
    private double _strokeThickness = 6;

    /// <summary>
    /// 闪烁模式。
    /// </summary>
    [ObservableProperty]
    private FlashMode _mode = FlashMode.Loop;

    /// <summary>
    /// 闪烁循环时间（秒）。仅在循环模式下生效。
    /// </summary>
    [ObservableProperty]
    private double _cycleSeconds = 2.0;

    /// <summary>
    /// 循环到最暗时的不透明度。
    /// </summary>
    [ObservableProperty]
    private double _minStrokeOpacity;

    /// <summary>
    /// 循环到最亮时的不透明度。
    /// </summary>
    [ObservableProperty]
    private double _maxStrokeOpacity = 1.0;

    /// <summary>
    /// 是否启用规则集限制。启用后只有规则集满足时才会闪烁 / 显示描边。
    /// </summary>
    [ObservableProperty]
    private bool _useRuleset;

    /// <summary>
    /// 闪烁条件规则集（使用 ClassIsland 现有规则集）。
    /// </summary>
    [ObservableProperty]
    private Ruleset _flashRuleset = new();

    /// <summary>
    /// 「闪烁」自动化行动触发时是否忽略规则集限制。
    /// </summary>
    [ObservableProperty]
    private bool _actionIgnoresRuleset;

    /// <summary>
    /// 包含的子组件。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ComponentSettings> _children = [];

    /// <summary>
    /// 供 ComboBox 双向绑定的模式索引。
    /// </summary>
    [JsonIgnore]
    public int ModeValue
    {
        get => (int)Mode;
        set => Mode = (FlashMode)Math.Clamp(value, 0, 1);
    }

    partial void OnModeChanged(FlashMode value)
    {
        OnPropertyChanged(nameof(ModeValue));
    }
}
