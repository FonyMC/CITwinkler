using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Models;
using ClassIsland.Core.Models.Components;
using ClassIsland.Core.Models.Ruleset;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CITwinkler.Models;

/// <summary>
/// 「文字描边」组件设置。
/// 与「描边渐变闪烁」相比：取消了渐变双色与亮度范围，改为单色实线描边 + 可调间距。
/// 注意：类型名 / 属性名会参与 JSON 序列化，改动会导致已有配置失效。
/// </summary>
public partial class TextOutlineComponentSettings : ObservableObject, IComponentContainerSettings
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
    private string _instanceName = "文字描边";

    /// <summary>
    /// 组件实例 ID。首次创建时生成，之后随配置持久化，用于「闪烁」行动定位目标。
    /// </summary>
    [ObservableProperty]
    private Guid _instanceId = Guid.NewGuid();

    /// <summary>
    /// 描边线的颜色。
    /// </summary>
    [ObservableProperty]
    private Color _lineColor = Color.FromArgb(255, 0x6E, 0xC7, 0xFF);

    /// <summary>
    /// 描边线的粗细（像素）。
    /// </summary>
    [ObservableProperty]
    private double _lineThickness = 2;

    /// <summary>
    /// 描边线到内容之间的间距（像素），范围 5 - 500。
    /// </summary>
    [ObservableProperty]
    private double _lineGap = 12;

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

    /// <summary>
    /// 供 NumericUpDown（其 Value 为 decimal?）双向绑定的间距值。
    /// 直接绑定 double 属性在部分 Avalonia 版本上无法正确转换，因此这里显式提供 decimal 包装。
    /// </summary>
    [JsonIgnore]
    public decimal LineGapValue
    {
        get => (decimal)Math.Round(Math.Clamp(LineGap, MinGap, MaxGap));
        set
        {
            var clamped = Math.Clamp((double)value, MinGap, MaxGap);
            if (Math.Abs(clamped - LineGap) > 0.001)
            {
                LineGap = clamped;
            }
        }
    }

    /// <summary>
    /// 间距下限（像素）。
    /// </summary>
    [JsonIgnore]
    public double MinGap => 5;

    /// <summary>
    /// 间距上限（像素）。
    /// </summary>
    [JsonIgnore]
    public double MaxGap => 500;

    partial void OnModeChanged(FlashMode value)
    {
        OnPropertyChanged(nameof(ModeValue));
    }

    partial void OnLineGapChanged(double value)
    {
        OnPropertyChanged(nameof(LineGapValue));
    }
}
