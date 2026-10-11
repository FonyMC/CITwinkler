using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using CITwinkler.Actions;
using CITwinkler.Components;
using CITwinkler.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CITwinkler;

/// <summary>
/// 插件入口。
/// </summary>
[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // 闪烁服务：登记主界面上所有「闪烁内容」组件，并把「闪烁」行动的请求转发给它们。
        services.AddSingleton<FlashEffectService>();

        // 「闪烁内容」容器组件（含设置界面）。
        services.AddComponent<FlashContentComponent, FlashContentComponentSettingsControl>();

        // 「闪烁」自动化行动（含设置界面）。
        services.AddAction<FlashAction, FlashActionSettingsControl>();
    }
}
