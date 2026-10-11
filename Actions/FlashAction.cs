using System;
using System.Linq;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using CITwinkler.Models;
using CITwinkler.Services;

namespace CITwinkler.Actions;

/// <summary>
/// 「闪烁」自动化行动：让指定的「闪烁内容」组件做一次描边渐变闪烁。
/// </summary>
[ActionInfo("fonyu.citwinkler.flash", "闪烁", "\uE945", true, "闪烁内容")]
public class FlashAction : ActionBase<FlashActionSettings>
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();

        var service = IAppHost.TryGetService<FlashEffectService>();
        if (service is null)
        {
            return;
        }

        service.RequestFlash(BuildRequest(), Settings.TargetAll, GetTargetIds());
    }

    protected override async Task OnRevert()
    {
        await base.OnRevert();

        var service = IAppHost.TryGetService<FlashEffectService>();
        service?.StopFlash(Settings.TargetAll, GetTargetIds());
    }

    private FlashRequest BuildRequest()
    {
        return new FlashRequest
        {
            DurationSeconds = Settings.OverrideDuration ? Math.Max(0.1, Settings.DurationSeconds) : 0,
            Times = Settings.OverrideTimes ? Math.Clamp(Settings.Times, 1, 200) : 3,
            Color = Settings.OverrideColor ? Settings.Color : null,
            Thickness = Settings.OverrideThickness ? Settings.Thickness : null,
            IgnoreRuleset = Settings.IgnoreRuleset
        };
    }

    private IReadOnlyList<Guid> GetTargetIds()
    {
        return Settings.TargetInstanceIds
            .Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();
    }
}
