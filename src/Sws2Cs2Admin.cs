using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Plugins;
using Sws2Cs2Admin.Configuration;
using Sws2Cs2Admin.Contracts;
using Sws2Cs2Admin.Services;
using Sws2Cs2Menus.Contracts;
using MenuServiceContract = Sws2Cs2Menus.Contracts.IMenuService;

namespace Sws2Cs2Admin;

/// <summary>
/// Main plugin entry point. Plugin metadata lives in <c>Metadata.cs</c>.
/// </summary>
public partial class Sws2Cs2Admin : BasePlugin
{
    private AdminSettings _settings = new();
    private AdminService _admins = null!;
    private AuditLogger _audit = null!;
    private BanService _bans = null!;
    private CommService _comms = null!;
    private AdminActionService _actions = null!;
    private MenuServiceContract? _menus;
    private ServiceProvider _provider = null!;
    private IDisposable? _configSubscription;

    private volatile bool _loaded;

    public Sws2Cs2Admin(ISwiftlyCore core) : base(core)
    {
    }

    public override void Load(bool hotReload)
    {
        Core.Configuration
            .InitializeWithTemplate("config.jsonc", "config.template.jsonc")
            .InitializeWithTemplate("admins.jsonc", "admins.template.jsonc")
            .Configure(builder =>
            {
                builder.AddJsonFile("config.jsonc", optional: false, reloadOnChange: true);
            });

        var services = new ServiceCollection();
        services.AddSwiftly(Core)
            .AddOptionsWithValidateOnStart<AdminSettings>()
            .BindConfiguration("sws2-cs2admin");

        _provider = services.BuildServiceProvider();
        var options = _provider.GetRequiredService<IOptionsMonitor<AdminSettings>>();
        _settings = options.CurrentValue;

        _admins = new AdminService(Core, _settings);
        _admins.Reload();

        _audit = new AuditLogger(Core, _settings.Audit);
        var banStore = new BanStore(Core);
        _bans = new BanService(Core, banStore, _admins, _audit, _settings);
        var commStore = new CommStore(Core);
        _comms = new CommService(Core, commStore, _admins, _audit, _settings);
        _comms.Start();
        _actions = new AdminActionService(Core, _admins, _audit, _comms, _bans, _settings);

        _configSubscription = options.OnChange(newSettings =>
        {
            Core.Scheduler.NextTick(() =>
            {
                if (!_loaded)
                {
                    return;
                }

                _settings = newSettings;
                _admins.ApplySettings(newSettings);
                _audit.ApplySettings(newSettings.Audit);
                _bans.ApplySettings(newSettings);
                _comms.ApplySettings(newSettings);
                _actions.ApplySettings(newSettings);

                Core.Logger.LogInformation("[sws2-cs2admin] Configuration reloaded");
            });
        });

        Core.Event.OnClientSteamAuthorize += OnClientSteamAuthorize;
        Core.Event.OnClientPutInServer += OnClientPutInServer;
        Core.Event.OnClientDisconnected += OnClientDisconnected;
        Core.Event.OnMapUnload += OnMapUnload;

        Core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn);

        _loaded = true;
        Core.Logger.LogInformation("[sws2-cs2admin] Plugin loaded (hotReload: {HotReload})", hotReload);
    }

    public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
    {
        if (_admins == null)
        {
            return;
        }

        interfaceManager.AddSharedInterface<IAdminService, AdminService>(
            AdminServiceConstants.InterfaceKey,
            _admins);
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        if (interfaceManager.TryGetSharedInterface<IMenuService>(
                MenuServiceConstants.InterfaceKey,
                out var menus))
        {
            _menus = menus;
        }
    }

    public override void Unload()
    {
        _loaded = false;

        Core.Event.OnClientSteamAuthorize -= OnClientSteamAuthorize;
        Core.Event.OnClientPutInServer -= OnClientPutInServer;
        Core.Event.OnClientDisconnected -= OnClientDisconnected;
        Core.Event.OnMapUnload -= OnMapUnload;

        Core.GameEvent.UnhookPost<EventPlayerSpawn>();

        _configSubscription?.Dispose();
        _configSubscription = null;

        _menus = null;
        _actions?.Reset();
        _comms?.Dispose();
        _admins?.Shutdown();
        _audit?.Dispose();
        _provider?.Dispose();
    }
}
