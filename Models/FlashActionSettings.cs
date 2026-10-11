using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CITwinkler.Models;

/// <summary>
/// 「闪烁」自动化行动设置。
/// </summary>
public partial class FlashActionSettings : ObservableObject
{
    /// <summary>
    /// 是否对所有「闪烁内容」组件生效。为 false 时仅对 <see cref="TargetInstanceIds"/> 中的组件生效。
    /// </summary>
    [ObservableProperty]
    private bool _targetAll = true;

    /// <summary>
    /// 目标组件实例 ID（字符串形式的 Guid）。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _targetInstanceIds = [];

    /// <summary>
    /// 是否自定义闪烁总时长。为 false 时使用「组件循环时间 × 闪烁次数」。
    /// </summary>
    [ObservableProperty]
    private bool _overrideDuration;

    /// <summary>
    /// 闪烁总时长（秒）。
    /// </summary>
    [ObservableProperty]
    private double _durationSeconds = 3.0;

    /// <summary>
    /// 是否自定义闪烁次数。
    /// </summary>
    [ObservableProperty]
    private bool _overrideTimes;

    /// <summary>
    /// 闪烁次数。
    /// </summary>
    [ObservableProperty]
    private int _times = 3;

    /// <summary>
    /// 是否覆盖描边颜色。
    /// </summary>
    [ObservableProperty]
    private bool _overrideColor;

    /// <summary>
    /// 覆盖使用的描边颜色。
    /// </summary>
    [ObservableProperty]
    private Color _color = Color.FromArgb(255, 0xFF, 0x3B, 0x30);

    /// <summary>
    /// 是否覆盖描边粗细。
    /// </summary>
    [ObservableProperty]
    private bool _overrideThickness;

    /// <summary>
    /// 覆盖使用的描边粗细。
    /// </summary>
    [ObservableProperty]
    private double _thickness = 8;

    /// <summary>
    /// 是否忽略目标组件自身的规则集限制。
    /// </summary>
    [ObservableProperty]
    private bool _ignoreRuleset;
}

/// <summary>
/// 「闪烁」行动设置界面中的目标选择项。
/// </summary>
public partial class FlashTargetSelection : ObservableObject
{
    private readonly Action<FlashTargetSelection>? _onChanged;

    public FlashTargetSelection(Guid id, string name, bool isSelected, Action<FlashTargetSelection>? onChanged = null)
    {
        Id = id;
        Name = name;
        _onChanged = onChanged;
        _isSelected = isSelected;
    }

    /// <summary>
    /// 目标实例 ID。
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// 目标显示名称。
    /// </summary>
    public string Name { get; }

    private bool _isSelected;

    /// <summary>
    /// 是否选中。
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value == _isSelected)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            _onChanged?.Invoke(this);
        }
    }
}
