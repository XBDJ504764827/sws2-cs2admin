<div align="center">
  <h2><strong>SWS2 CS2 Admin</strong></h2>
  <h3>Admin identities, permission nodes, player actions, bans and audit logging for SwiftlyS2 KZ servers.</h3>
</div>

<p align="center">
  <img src="https://img.shields.io/github/downloads/XBDJ504764827/sws2-cs2admin/total" alt="Downloads">
  <img src="https://img.shields.io/github/v/release/XBDJ504764827/sws2-cs2admin" alt="Release">
  <img src="https://img.shields.io/github/license/XBDJ504764827/sws2-cs2admin" alt="License">
</p>

## Features

- Admin identities loaded from `admins.jsonc`
- Groups, direct permission nodes and immunity levels
- Optional permission nodes from SwiftlyS2's `permissions.jsonc`
- Player actions: kick, slay, slap, respawn and freeze
- Communication controls: mute, gag, silence and their removals
- Persistent bans with temporary/permanent durations and offline `addban`
- Join ban checks
- JSONL audit logging for successful and denied admin actions
- Admin panel built on the shared [`sws2-cs2menus`](https://github.com/XBDJ504764827/sws2-cs2menus) service
- Shared `IAdminService` contract so Whitelist, RTV and other plugins can reuse admin permission checks
- Hot-reloadable `config.jsonc`
- Translations: English, 简体中文

## Requirements

- [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2) **1.5.0 or newer**
- [sws2-cs2menus](https://github.com/XBDJ504764827/sws2-cs2menus) (required; provides the shared menu contracts and admin panel service)
- .NET 10 SDK (only needed to build from source)

## Installation

1. Download the latest release zip.
2. Extract the `sws2-cs2admin` folder into `csgo/addons/swiftlys2/plugins/`:

```
csgo/addons/swiftlys2/plugins/
└── sws2-cs2admin/
    ├── sws2-cs2admin.dll
    ├── Sws2Cs2Admin.Contracts.dll
    ├── Sws2Cs2Menus.Contracts.dll
    └── resources/
        ├── exports/Sws2Cs2Admin.Contracts.dll
        ├── templates/config.template.jsonc
        ├── templates/admins.template.jsonc
        └── translations/
```

3. Install `sws2-cs2menus` as well, then start or reload the server, or run `sw plugins load sws2-cs2menus` and `sw plugins load sws2-cs2admin`.

The first load creates:

```
csgo/addons/swiftlys2/configs/plugins/sws2-cs2admin/
├── config.jsonc
└── admins.jsonc
```

Add your SteamID and groups to `admins.jsonc`, then run `!reloadadmins` or reload the plugin.

## Configuration

`config.jsonc` controls behavior:

```jsonc
{
  "sws2-cs2admin": {
    "Debug": false,
    "UseSwiftlyPermissions": true,
    "DefaultImmunity": 0,
    "Bans": {
      "CheckOnJoin": true,
      "DefaultDurationMinutes": 0,
      "MaxDurationMinutes": 525600,
      "AllowUnban": true,
      "KickMessage": "Banned: {0} ({1})"
    },
    "Comms": {
      "EnforceGag": true,
      "DefaultDurationMinutes": 30,
      "MaxDurationMinutes": 0,
      "ExpireCheckSeconds": 30
    },
    "Audit": {
      "Enabled": true,
      "RetentionDays": 30
    },
    "Menu": {
      "PageSize": 5
    },
    "Notify": {
      "OnlineAdmins": false,
      "BroadcastBans": false
    }
  }
}
```

`admins.jsonc` defines admins and groups. SteamID64, `STEAM_0:X:Y`, `[U:1:account]` and `$SteamID64` are accepted:

```jsonc
{
  "Admins": {
    "76561198000000000": {
      "Name": "Server Owner",
      "Groups": ["Owner"],
      "Permissions": [],
      "Immunity": 100
    }
  },
  "Groups": {
    "Owner": {
      "Immunity": 100,
      "Permissions": ["sws2.admin.*"]
    },
    "Moderator": {
      "Immunity": 50,
      "Permissions": [
        "sws2.admin.kick",
        "sws2.admin.slay",
        "sws2.admin.ban",
        "sws2.admin.unban",
        "sws2.admin.bans.view"
      ]
    }
  }
}
```

Direct permissions, group permissions and `settings.DefaultImmunity` are combined. Immunity is the highest value found. `UseSwiftlyPermissions` also reads nodes from SwiftlyS2's `permissions.jsonc`; immunity still comes from `admins.jsonc`.

## Permission nodes

| Node | Allows |
| :--- | :--- |
| `sws2.admin.*` | Every admin permission (wildcard) |
| `sws2.admin.menu` | Reserved for consumers; any configured admin can open `!admin` |
| `sws2.admin.kick` | `!kick` |
| `sws2.admin.slay` | `!slay` |
| `sws2.admin.slap` | `!slap` |
| `sws2.admin.respawn` | `!respawn` |
| `sws2.admin.freeze` | `!freeze` |
| `sws2.admin.mute` | `!mute` / `!unmute` |
| `sws2.admin.gag` | `!gag` / `!ungag` |
| `sws2.admin.silence` | `!silence` / `!unsilence` |
| `sws2.admin.ban` | `!ban` / `!addban` |
| `sws2.admin.unban` | `!unban` |
| `sws2.admin.bans.view` | `!listbans` and the ban management menu |
| `sws2.admin.who` | `!who` |
| `sws2.admin.reload` | `!reloadadmins` and the server tools menu |
| `sws2.admin.audit` | Audit information in the menu |
| `sws2.admin.immunity.bypass` | Ignore immunity when targeting another admin |

Admins with a node do not need a specific group. Console commands bypass permission checks.

## Commands

Chat commands use the server's configured command prefix, normally `!`.
Console commands use the `sw_` prefix. The permission column omits the `sws2.admin.` prefix.

| Chat | Console | Permission | Description |
| :--- | :--- | :--- | :--- |
| `!admin` | `sw_admin` | any admin | Open the admin panel |
| `!kick <target> [reason]` | `sw_kick` | `kick` | Kick a player |
| `!slay <target>` | `sw_slay` | `slay` | Slay a player |
| `!slap <target> [damage]` | `sw_slap` | `slap` | Damage a player |
| `!respawn <target>` | `sw_respawn` | `respawn` | Respawn a player |
| `!freeze <target> [seconds]` | `sw_freeze` | `freeze` | Freeze a player |
| `!mute <target> [duration] [reason]` | `sw_mute` | `mute` | Mute voice chat |
| `!unmute <target> [reason]` | `sw_unmute` | `mute` | Remove a mute |
| `!gag <target> [duration] [reason]` | `sw_gag` | `gag` | Gag text chat |
| `!ungag <target> [reason]` | `sw_ungag` | `gag` | Remove a gag |
| `!silence <target> [duration] [reason]` | `sw_silence` | `silence` | Mute and gag |
| `!unsilence <target> [reason]` | `sw_unsilence` | `silence` | Remove a silence |
| `!ban <target> [duration] [reason]` | `sw_ban` | `ban` | Ban an online player |
| `!addban <steamid> [duration] [reason]` | `sw_addban` | `ban` | Ban a SteamID offline or online |
| `!unban <steamid> [reason]` | `sw_unban` | `unban` | Lift a ban |
| `!listbans [steamid\|name]` | `sw_listbans` | `bans.view` | Show recent bans |
| `!who [target]` | `sw_who` | `who` | Show online admins or one admin |
| `!reloadadmins` | `sw_reloadadmins` | `reload` | Reload `admins.jsonc` |
| `!adminhelp` | `sw_adminhelp` | none | List admin commands |

### Targets

`@me`, `@all`, `@t`, `@ct`, `@spec`, `@alive`, `@dead`, `@bots`, `@human`, `@random`, `#slot`, SteamID and partial player names are supported. Commands that accept a target apply the action to every match.

### Durations

| Value | Meaning |
| :--- | :--- |
| `0`, `perm`, `permanent` | Permanent |
| `30` or `30m` | 30 minutes |
| `2h` | 2 hours |
| `7d` | 7 days |
| `2w` | 2 weeks |
| `1mo` | 30 days |
| `1y` | 365 days |

If no duration is given, the configured default is used.

## Data and audit

Runtime data is stored in:

```
csgo/addons/swiftlys2/data/sws2-cs2admin/
├── bans.json
├── comms.json
└── audit-YYYY-MM-DD.log
```

Audit entries are JSON lines containing timestamp, action, success, actor, target and details. Old daily files are removed according to `Audit.RetentionDays`.

## For other plugins

The admin service is exported as a SwiftlyS2 shared assembly:

```
resources/exports/Sws2Cs2Admin.Contracts.dll
```

Reference `Sws2Cs2Admin.Contracts.dll` at compile time with `Private=false`, then resolve it:

```csharp
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using Sws2Cs2Admin.Contracts;

public partial class MyPlugin : BasePlugin
{
    private IAdminService? _admins;

    public MyPlugin(ISwiftlyCore core) : base(core) { }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
        if (interfaceManager.TryGetSharedInterface<IAdminService>(
                AdminServiceConstants.InterfaceKey,
                out var admins))
        {
            _admins = admins;
        }
    }

    public override void Load(bool hotReload) { }

    public override void Unload() { }

    private bool CanUseRtv(IPlayer player)
    {
        return _admins?.HasPermission(player, "sws2.admin.rtv") == true;
    }
}
```

Use the constants in `AdminPermissions` instead of duplicating node strings.

## Building

The admin plugin references the menu contracts project. Install/load `sws2-cs2menus` before using the admin plugin. Locally, keep both repositories as siblings:

```
cs2-plugins/
├── sws2-cs2menus/
└── sws2-cs2admin/
```

Then:

```bash
dotnet restore
dotnet build -c Release
dotnet publish -c Release
```

The release package is generated at `build/sws2-cs2admin.zip`. CI checks out `sws2-cs2menus` into the `sws2-cs2menus/` subdirectory automatically.

## Versioning and releases

This repository uses a `develop` -> `main` flow:

- Pull requests run CI: `dotnet format`, a Release build with warnings as errors, package validation and a test-package artifact.
- Each PR run gets a traceable preview version such as `1.0.1-pr.12.37`.
- PR previews are published as `vX.Y.Z-pr.<PR>.<run>` releases and removed when the PR closes.
- Merging to `main` computes the next patch version, injects it into `src/VersionInfo.cs`, builds the package, creates the tag and publishes a GitHub Release.
- A manual release can be started with `workflow_dispatch` and a `patch`, `minor` or `major` bump.

No manual version editing is required. The value committed in `src/VersionInfo.cs` is only a fallback for local builds.

## Credits

- [FemboyKZ/mm-cs2admin](https://github.com/FemboyKZ/mm-cs2admin) - admin command, ban, comms and audit behavior reference
- [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2) - plugin framework, permissions and player APIs
- [sws2-cs2menus](https://github.com/XBDJ504764827/sws2-cs2menus) - shared menu service

## License

[MIT](LICENSE)
