using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using Sws2Cs2Admin.Contracts;
using Sws2Cs2Admin.Services;
using Sws2Cs2Menus.Contracts;

namespace Sws2Cs2Admin;

public partial class Sws2Cs2Admin
{
    private IMenuDefinition BuildMapManagementMenu(IPlayer player)
    {
        var text = Core.Translation.GetPlayerLocalizer(player);
        var state = _mapManager?.GetChangeState();
        var status = state is null ? text["cs2admin.menu.mapmanager.unavailable"]
            : state.IsPaused ? text["cs2admin.mapcontrol.paused_by", state.PausedBy ?? "-"]
            : state.PendingMap is null ? text["cs2admin.mapcontrol.idle"]
            : text["cs2admin.mapcontrol.pending", state.PendingMap.Name, state.Source ?? "-",
                state.IsDownloading ? text["cs2admin.mapcontrol.downloading"]
                    : Math.Max(0, (int)Math.Ceiling((state.ScheduledAt!.Value - DateTimeOffset.UtcNow).TotalSeconds)) + "s"];
        var canControl = _mapManager is not null && _admins.HasPermission(player, AdminPermissions.MapControl);
        return _menus!.CreateMenu(text["cs2admin.menu.maps"])
            .Id("admin.mapcontrol")
            .Comment(status)
            .PageSize(5)
            .ShowBack()
            .ShowClose()
            .CloseOnSelect(false)
            .AddOption(option => option.Id("maps").Text(text["cs2admin.mapcontrol.browse"])
                .Disabled(_mapManager is null || !_admins.HasPermission(player, AdminPermissions.ChangeMap))
                .OnSelected(context =>
                {
                    if (_admins.HasPermission(context.Player, AdminPermissions.ChangeMap))
                        context.Service.OpenSubMenu(context.Player, BuildMapsMenu(context.Player, text));
                }))
            .AddOption(option => option.Id("cancel-map").Text(text["cs2admin.mapcontrol.cancel"])
                .Comment(text["cs2admin.mapcontrol.cancel_hint"])
                .Disabled(!canControl)
                .OnSelected(context =>
                {
                    ControlMapChange(AdminActor.FromPlayer(context.Player), "cancel");
                    RefreshMapControls(context.Player);
                }))
            .AddOption(option => option.Id("pause-maps").Text(text["cs2admin.mapcontrol.pause"])
                .Comment(text["cs2admin.mapcontrol.pause_hint"])
                .Disabled(!canControl || state!.IsPaused)
                .OnSelected(context =>
                {
                    ControlMapChange(AdminActor.FromPlayer(context.Player), "pause");
                    RefreshMapControls(context.Player);
                }))
            .AddOption(option => option.Id("resume-maps").Text(text["cs2admin.mapcontrol.resume"])
                .Disabled(!canControl || !state!.IsPaused)
                .OnSelected(context =>
                {
                    ControlMapChange(AdminActor.FromPlayer(context.Player), "resume");
                    RefreshMapControls(context.Player);
                }))
            .AddOption("refresh", text["cs2admin.mapcontrol.refresh"], context => RefreshMapControls(context.Player))
            .Build();
    }

    private void RefreshMapControls(IPlayer player)
    {
        if (_menus is not { IsAvailable: true }) return;
        if (_menus.GetCurrentMenu(player)?.Id == "admin.mapcontrol")
            _menus.ReplaceCurrent(player, BuildMapManagementMenu(player));
    }

    [Command("cancelmap", false, "", "Cancel the pending managed map change")]
    public void CancelMapCommand(ICommandContext context) => ControlMapChange(Actor(context), "cancel");

    [Command("pausemaps", false, "", "Pause map changes and cancel pending work")]
    public void PauseMapsCommand(ICommandContext context) => ControlMapChange(Actor(context), "pause");

    [Command("resumemaps", false, "", "Resume accepting map changes without reviving cancelled work")]
    public void ResumeMapsCommand(ICommandContext context) => ControlMapChange(Actor(context), "resume");

    private void ControlMapChange(AdminActor actor, string action)
    {
        var localizer = actor.Player is null ? Core.Localizer : Core.Translation.GetPlayerLocalizer(actor.Player);
        void Send(string key)
        {
            if (actor.Player is null) Core.Logger.LogInformation("{Message}", localizer[key]);
            else ReplyMapChange(actor.Player, localizer, key);
        }
        if (!actor.IsConsole && !_admins.HasPermission(actor.SteamId, AdminPermissions.MapControl))
        {
            _audit.Log(actor, "map." + action + ".denied");
            Send("cs2admin.no_permission");
            return;
        }
        if (_mapManager is null)
        {
            Send("cs2admin.menu.mapmanager.unavailable");
            return;
        }
        if (action == "cancel")
        {
            Send(_mapManager.CancelPendingChange() ? "cs2admin.mapcontrol.cancel_success" : "cs2admin.mapcontrol.nothing");
        }
        else
        {
            var paused = action == "pause";
            if (_mapManager.GetChangeState().IsPaused == paused)
            {
                Send(paused ? "cs2admin.mapcontrol.paused" : "cs2admin.mapcontrol.idle");
                return;
            }
            _mapManager.SetChangesPaused(paused, actor.Name);
            BroadcastMapMessage(paused ? "cs2admin.mapcontrol.pause_success" : "cs2admin.mapcontrol.resume_success", actor.Name);
        }
        _audit.Log(actor, "map." + action);
    }
}
