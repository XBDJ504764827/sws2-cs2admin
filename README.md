# SWS2 CS2 Admin

SwiftlyS2 管理工具：管理员分组与权限、玩家操作、封禁和解封、语音/文字限制、审计日志，以及与玩家 RTV 共用的地图管理。`!admin` 使用 menus 的分类 HUD，地图列表和切换交给 mapmanager。

## 与其他 SWS2 插件搭配

| 插件 | 职责 | 与本插件的关系 |
| --- | --- | --- |
| [sws2-cs2menus](https://github.com/XBDJ504764827/sws2-cs2menus) | 共享菜单、分页、导航、地图选择 HUD | 管理 HUD 所需的基础服务 |
| [sws2-cs2mapmanager](https://github.com/XBDJ504764827/sws2-cs2mapmanager) | 统一地图目录、评分、工坊下载和可取消换图 | 地图管理所需的基础服务 |
| [sws2-cs2admin](https://github.com/XBDJ504764827/sws2-cs2admin) | 管理员权限、玩家管理、封禁、换图控制 | 本插件 |
| [sws2-cs2rockthevote](https://github.com/XBDJ504764827/sws2-cs2rockthevote) | 玩家预定地图、RTV 发起投票与选图 | 可选；管理员可取消或暂停其投票及换图 |
| [sws2-flashlight](https://github.com/XBDJ504764827/sws2-flashlight) | 随视角移动的手电筒 | 可选，独立照明功能 |
| [sws2-nightvision](https://github.com/XBDJ504764827/sws2-nightvision) | 玩家独立夜视与亮度偏好 | 可选，独立夜视功能 |

常用组合：玩家换图安装 **menus + mapmanager + rockthevote**；需要管理面板时再加 **admin**；手电筒和夜视按需加入。工坊地图自动下载和 HUD 工坊资源自动挂载还需要服务器安装 **MultiAddonManager**。本套件不提供 KZ 计时、检查点或 CKZ/VNL 模式本身。

## 安装与首次使用

需要 SwiftlyS2 **1.5.0 或更新版本**。管理面板需要 menus，地图功能还需 mapmanager；工坊地图自动下载和 HUD 自动挂载使用 MultiAddonManager。仅需要玩家管理/封禁时可不使用 RTV。

1. 下载配套发布包或 CI 测试包，完整解压 `sws2-cs2admin/` 到 `csgo/addons/swiftlys2/plugins/`。
2. 保留主 DLL、共享合约、`.deps.json`、`Microsoft.Data.Sqlite.dll`、`SQLitePCLRaw.*.dll`、`runtimes/`、`resources/exports/` 和翻译。SQLite 是本地数据库，不需安装外部数据库服务器；只复制主 DLL 会缺依赖。
3. 安装并先加载 menus；需要地图管理时也先加载 mapmanager。然后运行服务器控制台 `sw plugins load sws2-cs2admin`，或整套安装后重启。
4. 编辑首次生成的 `csgo/addons/swiftlys2/configs/plugins/sws2-cs2admin/admins.jsonc`，加入自己的 SteamID 和分组。
5. 用服务器控制台 `sw_reloadadmins` 加载新名单；之后已获权限的管理员可在游戏中运行 `!reloadadmins`。输入 `!admin` 打开面板。

### 配置第一个管理员

以下可用作最小 `admins.jsonc`，**将示例 SteamID 替换成自己的 SteamID64**。已有名单时合并内容，不要覆盖其他管理员。

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
    "MapOperator": {
      "Immunity": 10,
      "Permissions": [
        "sws2.admin.menu",
        "sws2.admin.map",
        "sws2.admin.map.control"
      ]
    }
  }
}
```

名单支持 SteamID64、`STEAM_0:X:Y`、`[U:1:account]` 和 `$SteamID64`。直接权限与分组权限合并，免疫等级取适用值中的最高值；默认免疫由 `DefaultImmunity` 提供。修改名单后需执行重新加载命令；`config.jsonc` 的热重载不等于名单重载。

## 管理面板如何使用

`!admin` 打开独立的管理 HUD；左侧根据权限显示分类，右侧显示内容，第一项可用分类自动选中。再次选择分类会刷新列表；子页返回保留分类内导航。危险操作如踢出和封禁需要确认。

| 分类 | 常用操作 |
| --- | --- |
| 玩家管理 | 选择在线玩家，踢出、处死、伤害、复活、冻结、禁言、封禁、第一人称观战 |
| 地图管理 | 浏览地图并确认换图，查看状态，取消/暂停/恢复托管换图 |
| 封禁管理 | 按状态分页、看详情、选择原因并确认解封；查看过期/撤销历史 |
| 服务器管理 | 查看地图/人数、本人管理员信息、重载名单和查看审计目录 |

自定义图片 HUD 需要 menus 的已编译 Workshop addon；资源未挂载时使用中心 HTML 回退。先用 `!menudemo` 验证界面，再测试权限。截图和四类评分来自同一地图目录及 addon，地图管理与 `!yd` / RTV 选图一致。

### 地图管理与 RTV 配合

在 [mapmanager](https://github.com/XBDJ504764827/sws2-cs2mapmanager) 的 `Maps.Available` 配置一次地图，admin 与 RTV 共用。管理员选择目标并确认后，mapmanager 执行 **3 秒倒计时**，随后切换或下载；不需要玩家 RTV 同意。

| 管理员操作 | 快捷命令 | 对玩家 RTV 的影响 |
| --- | --- | --- |
| 取消本次 | `!cancelmap` | 清空同意票、取消活跃选图面板，取消倒计时/下载后的自动换图；之后可重新投票 |
| 暂停换图 | `!pausemaps` | 取消当前流程并拒绝新的托管 RTV/admin 换图 |
| 恢复换图 | `!resumemaps` | 接受新请求，不恢复旧票/旧任务；玩家预定保留 |

三项控制需要 `sws2.admin.map.control`，选图换图需要 `sws2.admin.map`；Owner 的 `sws2.admin.*` 包含两者。旧的自定义 Moderator 分组需要按需补权限。地图状态页显示来源、目标、倒计时或下载阶段，可刷新查看。

暂停在地图卸载/重启后重置；底层下载可能在取消后继续，已经发给引擎的命令不能撤回。原始控制台 `changelevel` 或绕过 mapmanager 的插件不受管理。

### 第一人称观战

玩家管理 → 目标玩家 → 观战，需要 `sws2.admin.spectate`。管理员会离开当前队伍并加入观察者，切到存活目标的第一人称。不能观战自己、失效或死亡目标；免疫等级不禁止正常观战。若同时安装夜视且禁止观察者使用，观战后夜视会按其团队/观察者规则关闭。

## 配置与权限

运行配置为 `csgo/addons/swiftlys2/configs/plugins/sws2-cs2admin/config.jsonc`，支持热重载：

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

| 设置 | 说明 |
| --- | --- |
| `UseSwiftlyPermissions` | 同时接受 SwiftlyS2 `permissions.jsonc` 的权限节点；免疫仍来自本插件名单 |
| `DefaultImmunity` | 未设置更高分组/个人免疫时的默认值 |
| `Bans.CheckOnJoin` | 入服检查封禁 |
| `Bans.DefaultDurationMinutes` | 默认封禁分钟数，`0` 为永久 |
| `Bans.MaxDurationMinutes` | 封禁上限，`0` 为不限 |
| `Bans.AllowUnban` / `KickMessage` | 解封开关与封禁踢出提示 |
| `Comms.EnforceGag` | 是否阻止被禁文字玩家的聊天 |
| `Comms.DefaultDurationMinutes` / `MaxDurationMinutes` | 通信限制默认时长/上限，单位分钟，最大值 `0` 为不限 |
| `Comms.ExpireCheckSeconds` | 通信限制过期扫描周期 |
| `Audit.Enabled` / `RetentionDays` | 审计开关和保留天数 |
| `Menu.PageSize` | 管理菜单页大小 |
| `Notify.OnlineAdmins` / `BroadcastBans` | 管理动作通知设置 |

权限节点均以 `sws2.admin.` 开头：

| 权限 | 作用 |
| --- | --- |
| `sws2.admin.*` | 全部管理权限 |
| `sws2.admin.menu` | 留给消费者的面板节点；本插件任何已配置管理员均可打开面板，操作仍分别检查权限 |
| `sws2.admin.map` | 地图浏览与切换 |
| `sws2.admin.map.control` | 取消、暂停和恢复换图 |
| `sws2.admin.spectate` | 第一人称观战 |
| `sws2.admin.kick` / `slay` / `slap` / `respawn` / `freeze` | 对应玩家操作，斜线后的名称也需加完整前缀 |
| `sws2.admin.mute` / `gag` / `silence` | 对应通信限制及解除 |
| `sws2.admin.ban` | 在线和离线封禁 |
| `sws2.admin.unban` | 解封 |
| `sws2.admin.bans.view` | 查询及浏览封禁记录 |
| `sws2.admin.who` / `reload` / `audit` | 管理员信息、名单重载、审计信息 |
| `sws2.admin.immunity.bypass` | 忽略目标管理员的免疫保护 |

服务器控制台动作绕过玩家权限检查；打开 HUD 需要玩家身份。RTV、手电筒默认不依赖这些管理员节点；夜视自定义亮度节点由 SwiftlyS2 权限服务检查。

## 命令速查

聊天前缀取决于服务器配置，下表使用 `!`；权限后缀需加 `sws2.admin.`。控制台动作参数与聊天相同，例如 `sw_kick Alice reason`。

| 聊天命令 | 控制台命令 | 权限后缀 | 说明 |
| --- | --- | --- | --- |
| `!admin` | `sw_admin` | `已配置管理员` | 打开管理面板，仅玩家可打开 |
| `!kick <目标> [原因]` | `sw_kick` | `kick` | 踢出玩家 |
| `!slay <目标>` | `sw_slay` | `slay` | 处死玩家 |
| `!slap <目标> [伤害]` | `sw_slap` | `slap` | 对玩家造成伤害 |
| `!respawn <目标>` | `sw_respawn` | `respawn` | 复活玩家 |
| `!freeze <目标> [秒]` | `sw_freeze` | `freeze` | 冻结玩家 |
| `!mute <目标> [时长] [原因]` | `sw_mute` | `mute` | 禁语音 |
| `!unmute <目标> [原因]` | `sw_unmute` | `mute` | 解除语音禁言 |
| `!gag <目标> [时长] [原因]` | `sw_gag` | `gag` | 禁文字聊天 |
| `!ungag <目标> [原因]` | `sw_ungag` | `gag` | 解除文字禁言 |
| `!silence <目标> [时长] [原因]` | `sw_silence` | `silence` | 同时禁语音和文字 |
| `!unsilence <目标> [原因]` | `sw_unsilence` | `silence` | 解除联合禁言 |
| `!ban <目标> [时长] [原因]` | `sw_ban` | `ban` | 封禁在线玩家 |
| `!addban <SteamID> [时长] [原因]` | `sw_addban` | `ban` | 离线或在线封禁 |
| `!unban <SteamID> [原因]` | `sw_unban` | `unban` | 解除封禁，保留历史 |
| `!listbans [SteamID或姓名]` | `sw_listbans` | `bans.view` | 显示最多 10 条近期记录 |
| `!bans [姓名/SteamID] [筛选参数]` | `sw_bans` | `bans.view` | 查询历史并打开封禁管理 |
| `!cancelmap` | `sw_cancelmap` | `map.control` | 取消本次投票及托管换图 |
| `!pausemaps` | `sw_pausemaps` | `map.control` | 暂停托管换图 |
| `!resumemaps` | `sw_resumemaps` | `map.control` | 恢复接受新换图 |
| `!who [目标]` | `sw_who` | `who` | 查看管理员信息 |
| `!reloadadmins` | `sw_reloadadmins` | `reload` | 重新加载管理员名单 |
| `!adminhelp` | `sw_adminhelp` | `无需权限` | 显示管理命令帮助 |

### 目标与时长

目标支持 `@me`、`@all`、`@t`、`@ct`、`@spec`、`@alive`、`@dead`、`@bots`、`@human`、`@random`、`#slot`、SteamID 及玩家名片段，匹配多人时逐一执行并检查免疫。

| 时长 | 含义 |
| --- | --- |
| `0`、`perm`、`permanent` | 永久 |
| `30`、`30m` | 30 分钟 |
| `2h` / `7d` / `2w` | 2 小时 / 7 天 / 2 周 |
| `1mo` / `1y` | 30 天 / 365 天 |

省略时长使用对应配置默认值。`freeze` 的可选参数为秒数，不采用上述封禁时长格式。

## 封禁查询、解封与数据备份

运行数据位于：

```text
csgo/addons/swiftlys2/data/sws2-cs2admin/
├── bans.db
├── bans.json.pre-sqlite.bak   # 从旧 JSON 迁移时生成
├── comms.json
└── audit-YYYY-MM-DD.log
```

封禁使用本地 SQLite，不跨服务器同步。入服使用活跃封禁缓存，历史按 SQL 分页读取；写入提交成功后才报告动作成功。过期或解封保留历史。

### 查找并解除永久封禁

运行 `!admin` → 封禁管理 → 选择状态 → 打开记录 → 解封 → 原因 → 确认姓名/SteamID。数据库页最多三条记录并带前后页控制。浏览需要 `bans.view`，解封需要 `unban`；旧确认不能解除同一 SteamID 后来新增的封禁。

离线玩家同样可以查询/解封：

```text
!bans Alice --status=all
!bans 76561198000000001 --status=permanent
!bans --admin=XBDJ --from=2026-11-01 --to=2026-11-16
!unban 76561198000000001 Appeal approved
```

`--status` 可用 `active`（默认）、`permanent`、`temporary`、`expired`、`revoked`、`all`。姓名为忽略大小写的字面子串匹配，SteamID 精确匹配；`--admin` 接受姓名或 SteamID。创建时间筛选使用 UTC，包含 `--to` 当日全天；详情按服务器本地时间显示。服务器控制台查询最多打印十条。

### 旧数据迁移与备份

首次启动会在事务中将旧 `bans.json` 导入 SQLite，保留原 JSON 和字节一致的 `bans.json.pre-sqlite.bak`，迁移标记防止重复导入。无效 JSON 或导入失败会阻止初始化，避免旧封禁静默丢失。迁移后 SQLite 为准，修改旧 JSON 不生效；产生新封禁/解封后不要回滚到旧 JSON。

备份应停服后进行，或用 SQLite 在线备份 API / `.backup`。数据库打开时不要只复制 `bans.db`，WAL 可能仍含已提交记录。一起备份配置、`admins.jsonc`、`comms.json` 和所需审计日志。审计文件是逐行 JSON，包含时间、操作者、目标、动作、成功与详情，按 `Audit.RetentionDays` 清理。

## 常见问题

| 问题 | 检查 |
| --- | --- |
| 第一次没有管理员，不能重载名单 | 在服务器控制台执行 `sw_reloadadmins` |
| 能开面板却不能换图/暂停 | 分别检查 `map` 和 `map.control`；面板资格不等于所有动作权限 |
| 踢出/禁言某管理员失败 | 对比免疫等级和 `immunity.bypass` |
| 面板不可用 | menus 加载日志、共享合约；HUD 外观问题用 `sw_hudstatus` 排查 |
| 没有地图或正在下载 | mapmanager 目录、MAM 下载状态和暂停状态 |
| SQLite 加载失败 | 确保完整安装 `runtimes/`、托管依赖、`.deps.json`，检查迁移日志 |

## 开发接口参考

The admin service is exported as a SwiftlyS2 shared assembly:

```
resources/exports/Sws2Cs2Admin.Contracts.dll
```

Reference `Sws2Cs2Admin.Contracts.dll` at compile time with `Private=false`, then resolve it:

```csharp
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using Sws2Cs2Admin.Contracts;
using SwiftlyS2.Shared.Players;

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

    private bool CanManageMaps(IPlayer player)
    {
        return _admins?.HasPermission(player, AdminPermissions.ChangeMap) == true;
    }
}
```

Use the constants in `AdminPermissions` instead of duplicating node strings.

## 源码构建与升级

需要 .NET 10 SDK；本地将 menus、mapmanager、admin 仓库放在同级目录，或使用项目支持的仓库内依赖路径：

```text
cs2-plugins/
├── sws2-cs2menus/
├── sws2-cs2mapmanager/
└── sws2-cs2admin/
```

```sh
dotnet restore sws2-cs2admin.csproj
dotnet build sws2-cs2admin.csproj -c Release -warnaserror
dotnet publish sws2-cs2admin.csproj -c Release --no-restore
dotnet run --project tests/BanChecks/BanChecks.csproj -c Release
```

安装包：`build/sws2-cs2admin.zip`。CI 同时检出 menus 与 mapmanager 合约。共享合约变更时配套更新 menus、mapmanager、admin、rockthevote 并重启；保留现有数据库和配置。仅新增服务端菜单动作无需重新发布 HUD，布局/截图变化则需单独编译更新 addon。

仓库采用 `develop` → `main`，PR 产出测试包与预览版本，合并 main 自动计算版本并发布 Release；本地 `src/VersionInfo.cs` 仅为回退版本。

## 致谢与许可证

- [FemboyKZ/mm-cs2admin](https://github.com/FemboyKZ/mm-cs2admin)：管理、封禁、通信限制与审计行为参考。
- [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2)：插件框架与共享服务。
- [MIT License](LICENSE)。
