using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Shared;
using CITwinkler.Models;
using CITwinkler.Services;

namespace CITwinkler.Actions;

/// <summary>
/// 「闪烁」行动设置界面的交互逻辑。
/// </summary>
public partial class FlashActionSettingsControl : ActionSettingsControlBase<FlashActionSettings>
{
    private FlashEffectService? _service;

    /// <summary>
    /// 当前可选的闪烁目标组件。
    /// </summary>
    public ObservableCollection<FlashTargetSelection> Targets { get; } = [];

    /// <summary>
    /// 公开设置，便于 XAML 绑定。
    /// </summary>
    public new FlashActionSettings Settings => base.Settings;

    public FlashActionSettingsControl()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _service = IAppHost.TryGetService<FlashEffectService>();
        if (_service is not null)
        {
            _service.TargetsChanged -= ServiceOnTargetsChanged;
            _service.TargetsChanged += ServiceOnTargetsChanged;
        }

        RefreshTargets();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_service is not null)
        {
            _service.TargetsChanged -= ServiceOnTargetsChanged;
        }
    }

    private void ServiceOnTargetsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshTargets();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshTargets);
        }
    }

    private void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _service = IAppHost.TryGetService<FlashEffectService>();
        RefreshTargets();
    }

    private void RefreshTargets()
    {
        var selected = Settings.TargetInstanceIds.ToHashSet();
        Targets.Clear();

        var list = _service?.Targets;
        if (list is not null)
        {
            foreach (var target in list)
            {
                Targets.Add(new FlashTargetSelection(
                    target.InstanceId,
                    target.DisplayName,
                    selected.Contains(target.InstanceId.ToString()),
                    OnSelectionChanged));
            }
        }

        EmptyTargetHint.IsVisible = Targets.Count == 0;
    }

    private void OnSelectionChanged(FlashTargetSelection selection)
    {
        var id = selection.Id.ToString();
        if (selection.IsSelected)
        {
            if (!Settings.TargetInstanceIds.Contains(id))
            {
                Settings.TargetInstanceIds.Add(id);
            }
        }
        else
        {
            Settings.TargetInstanceIds.Remove(id);
        }
    }
}
