# BadNorthUnlockAll

Bad North（北方绝境）的 BepInEx 解锁插件：一键解锁**手抄本**（物品 / 特质 / 消耗品）与**极难难度**，
并兼容 **Relics & Realms** 遗物（宝石）系统的解锁。

> **v1.3.5 起默认只写用户存档**（`user`，即游戏内“手抄本”的数据）：
> 不会往战役存档（`campaign`）里塞道具，也不会把物品 / 特质标成“初始装备”。
> 需要旧行为（新开局可选全部装备、战役道具池全解锁）可在配置里单独打开。

## 功能

| 功能 | 默认 | 实现方式 |
| --- | --- | --- |
| 手抄本全解锁 | **开** | Harmony 补丁 `Profile.OnUserSaveLoaded` → 遍历 `ResourceList<HeroUpgradeDefinition>`，把每项升到最高级写进 `userSave.inventory`（只写用户存档 `user`） |
| 解锁极难难度 | 开 | `userSave.maxDifficulty = Difficulty.VeryHard` 并保存 |
| 全部装备可作为初始装备 | **关**（多人兼容预设默认也关，见配置） | 把条目的 `isStarting` 与 `upgradeType.canBeStartItem` 置 true。开启后新开局可选全部装备，战役奖励生成也会随之变化 |
| 战役内道具池 | **关** | 仿游戏自带命令 `Inventory.AddAll`，写 `campaign.inventory`。开启后当前这局立刻全部可购买 / 升级，道具会出现在玩家仓库 |
| Relics & Realms 宝石解锁 | 开 | 该类 mod 不走手抄本体系，改用 Unity `PlayerPrefs` 存解锁状态，见下文 |

## 两套库存的区别（为什么默认只写一处）

| | 数据结构 | 读取方 | 作用 | 默认 |
| --- | --- | --- | --- | --- |
| `Profile.userSave.inventory` | `MetaInventory`（进存档的条目列表 + 运行时 `startingItems` / `startingTraits`） | 手抄本界面（`MetaInventoryMenu`）、新游戏界面（`NewGameOptionsPopup`）、开局特质分配（`HeroTraitAssigner`） | 手抄本：决定“手抄本里能看到什么” | **写** |
| `Profile.campaign.inventory` | `List<SerializableHeroUpgrade>` | 战役内升级界面（`SuperUpgradesCampaignProxy` → `IUpgradesProxy.inventory`） | 决定“当前这局能买 / 升级什么”（玩家仓库） | 不写 |

关键点：**手抄本界面只用 `MetaInventory.Get()` 判断条目是否“已知”**，所以只写 `userSave.inventory`
就能让手抄本显示全部条目，不会给玩家发放道具。

只有条目被标成 `isStarting` 时，才会进入 `startingItems` / `startingTraits`，进而：

- 新开局可选这些物品 / 特质（`NewGameOptionsPopup`）；
- `GraphGenerator` 会把 `startingItems` 里的物品从地图元奖励候选中剔除；
- `HeroTraitAssigner` 会把已解锁特质从元奖励中排除。

因此“全部标记为初始装备”会明显改变战役玩法，v1.3.5 默认关闭。

## `MetaInventory` 兼容性（v1.3.5 修复的崩溃）

v1.3.4 及更早版本在插件里**直接引用了** `Voxels.TowerDefense.ProfileInternals.MetaInventory`。
当玩家那份 `Assembly-CSharp.dll` 里没有这个类型时（游戏版本不同，或被整合包 / 汉化 / 破解替换过），
Mono 会在 JIT 编译解锁方法时抛 `TypeLoadException`：

```
[BASE] Next Update Exception | Message: System.TypeLoadException: Could not load type
'Voxels.TowerDefense.ProfileInternals.MetaInventory' from assembly 'Assembly-CSharp, Version=0.0.0.0, ...'
   at FullUnlockMod.Patch_OnUserSaveLoaded.Postfix ()
```

这种失败发生在 **JIT 编译阶段**，方法体里的 `try/catch` 拦不住（所以 v1.3.3 的“加固”并没有解决问题），
异常会冒到 `CS.Platform.BasePlatformManager.Update` 被记成 `Next Update Exception`。v1.3.5 的做法：

1. **手抄本类型完全走反射**（`GameCompat`）：从 `UserSave.inventory` 字段拿到实例，再按签名反射调用
   `Add(HeroUpgradeDefinition, int, bool)` / `Get(HeroUpgradeDefinition, out int, out bool, out bool)`，
   程序集里不再有 `MetaInventory` 的 TypeRef（已用 Mono.Cecil 核对）；
2. **启动自检**：按字符串查 `Profile` / `UserSave` / `HeroUpgradeDefinition` / `HeroUpgradeType` /
   `ResourceList<T>` / `Difficulty` / `CampaignPrefs` 及其用到的成员；缺关键项就只打一条日志并**不挂补丁**，
   不会把游戏拖崩；
3. **战役部分按需访问**：`CampaignSave` / `SerializableHeroUpgrade` 只在开启 `UnlockCampaignItemPool`
   且自检通过时才会被 JIT 编译。

## 从 1.3.4 升级：清掉旧的“初始装备”标记

1.3.4 及更早版本会把全部物品 / 特质标成初始装备（写进**用户存档**）。
`MetaInventory.Add` 只能把 `isStarting` 置 true，没有公开 API 能单独清除，所以 v1.3.5 提供一次性开关：

1. 把配置 `[Unlock] ResetStartingItems` 改成 `true`；
2. 重启游戏一次 —— 插件会重建手抄本条目（`userSave.inventory = new MetaInventory()`），再按当前配置重新写入；
3. 执行后该开关会自动改回 `false`。

> 注意：重建会一并清掉你自己在游戏内正常解锁的初始装备。
> 不重建也可以，只影响“新开局可选装备”和战役奖励生成，不影响手抄本解锁本身；
> 插件在检测到大量条目仍带初始装备标记时，会在日志里提示一次。

## 关于 Relics & Realms（4.0.0）

`Relics & Realms`（BepInEx GUID `com.gmw.relicselection`）把每颗宝石的解锁状态写进 `PlayerPrefs`：

- 宝石：键为 `RelicDef.unlockKey`（4.0.0 中即 `Relic_Unlocked_<名字>`，共 20 个），值 `1` = 已解锁；
- 境界：键 `RealmUnlocked`，取值 1~12。

本插件启动时会：

1. 在所有已加载程序集中查找 `RelicSelectionMod.RelicPlugin`，反射读取它的 `defs`（宝石定义表）与
   `RomanNumerals`（境界数）——这样 mod 升级新增宝石时无需修改本插件；
2. 兜底使用内置的 20 个 4.0.0 键名（插件加载顺序导致静态定义表尚未初始化时依然生效）；
3. 把这些键写为 `1`，并按配置写入 `RealmUnlocked`，最后 `PlayerPrefs.Save()`。

由于该 mod 每帧用 `IsUnlocked()` 重算界面签名，解锁在运行期即刻生效，无需重启游戏。未安装该 mod 时不会改动任何 `PlayerPrefs`。

## 与其它 mod 注入的特质 / 道具的关系

- **原版特质重做（Vanilla Traits Overhaul 一类）**：就地改写原版 `Hero_Trait_*` 定义的文本与数值，ID 不变，本插件的手抄本循环已经覆盖，无需额外处理。
- **运行时注入的新特质（如 Bad North Multiplayer 的 6 个自定义特质）**：由 mod 在 `Awake` 里用 `ScriptableObject.CreateInstance<HeroUpgradeDefinition>()` 创建、注册进 `ResourceList<HeroUpgradeDefinition>.list`，再通过它自己的选人界面直接提供，并用 Harmony Prefix 主动屏蔽 `MetaInventory.Add` 使其不落盘。也就是说这类特质“天然永远可选、本来就不需要解锁”，本插件写入时会被对方的 Prefix 拦下，属于预期行为。
- 本插件对这些注入型定义唯一的动作是写入手抄本条目（`isStarting` 默认 false）。
  只有开启 `MarkAsStartingItems` 时才会把 `upgradeType.canBeStartItem` 置 `true`。

## 兼容性核对（v1.3.5，逐个反编译确认）

| mod | GUID / 版本 | 结论 / 本插件的处理 |
| --- | --- | --- |
| Relics & Realms | `com.gmw.relicselection` 4.0.0 | 走 PlayerPrefs：20 个 `Relic_Unlocked_<宝石名>` + `RealmUnlocked`(1–12)。已核对 4.0.0 的 `defs`（`private static RelicDef[20]`）、`RomanNumerals`（12 项）以及全部 `unlockKey` 名称，与 `RelicUnlockPatch` 的反射 + 内置兜底完全一致；它本身**不引用** `ProfileInternals.MetaInventory`（只按字符串查 `UI.MetaInventory.*` 的界面类型），所以本插件把手抄本改成反射访问不影响它 |
| Commanders Sail In | `gmw.commanderssailin` 2.4.1 | 只改镜头 / 船速，不新增可解锁定义，无需处理 |
| Bad North Multiplayer | `com.bananasplit.badnorthmp` 1.0.10 | ① 自定义特质由它的 Harmony Prefix 拦截 `MetaInventory.Add` —— 本插件的反射调用同样会被拦下，日志记“由该 mod 自行管理”；② 它的 `PartyLoadout.Catalog` 直接读取 `startingItems` / `startingTraits`，因此提供 `AutoStartItemsForMultiplayer` 兼容预设（默认关；开启后等价 1.3.4 的初始装备行为，多人可全选），检测到它时日志会给出提示；③ 选装应用走 `Resources.FindObjectsOfTypeAll`，与本插件写入方式无冲突 |
| BN Mod API（BNAPI） | `nacu.bnapi` | 用 MonoMod 钩住 `MetaInventory.InitStartingUpgrades` 注入 `alwaysUnlocked` 特质；本插件重建手抄本（`new MetaInventory()`）会触发该钩子，注入不会丢；反射 `Add` 也会经过它的钩子 |
| PlentyTraits / AxeEnhance | `nacu.plentytraits` 等 | 通过 BNAPI 注册进 `ResourceList<HeroUpgradeDefinition>`，本插件遍历表即可覆盖 |

> 除 Relics & Realms 的 PlayerPrefs（其公开机制）外，本插件不修改上述任何 mod 的数据 / 配置。

## 解锁覆盖面（v1.3.5 核对结果）

Bad North 里所有“可解锁内容”最终都是 `HeroUpgradeDefinition`（物品 / 特质 / 消耗品 / 技能 / 职业），且必须注册进 `ResourceList<HeroUpgradeDefinition>` 才会被游戏 UI 使用。本插件直接遍历这张表，因此：

| 来源 | 是否需要额外操作 | 说明 |
| --- | --- | --- |
| 原版游戏 | 否 | 写入手抄本（`userSave.inventory`），日志 `手抄本已解锁` |
| 旧灰复燃的战旗（林风柳整合包） | 否 | 其内容烘焙在替换版 `Assembly-CSharp.dll` 中，同样出现在 `ResourceList`，随原版一起解锁 |
| PlentyTraits / PlentyTraits AxeEnhance（`nacu.*`，走 BNAPI） | 否 | `CustomTraits.RegisterTrait` 会注册进 `ResourceList`；其中 `alwaysUnlocked=false` 的（荆棘、追猎）原本要随进度解锁，本插件直接解锁，日志 `PlentyTraits 已解锁（N 项）` |
| 只改数值 / 技能 / 敌人的 mod（HeavyShield、ShieldSpear、FireMod、EndlessFinale、Commanders Sail In…） | 不需要 | 它们不新增可解锁定义 |
| Bad North Multiplayer 的 6 个自定义特质 | 不需要 | 该 mod 用 Harmony Prefix 主动拦截 `MetaInventory.Add` 并在自己的选人界面提供，日志 `Bad North Multiplayer 无需解锁（6 项由该 mod 自行管理）` |
| Relics & Realms（gwm 的宝石系统） | 否 | 走 PlayerPrefs，由 `RelicUnlockPatch` 处理，日志 `Relics & Realms 已解锁（宝石 20 颗，境界 I-12 已解锁）` |

> 唯一无法自动覆盖的情况：mod 自带独立存档/配置、且完全不经过 `HeroUpgradeDefinition` 与 `PlayerPrefs` —— 这类必须按它自己的机制单独适配（Relics & Realms 就是这种，已适配）。

### 日志示例（装齐 mod 时，默认配置）

```
[FullUnlockPatch] OnUserSaveLoaded 触发，开始解锁手抄本
[FullUnlock] 手抄本已解锁（原版 58 项；未标记为初始装备，新开局仍按原版提供初始装备，极难难度已开启；本次写入 58 项，只写用户存档 user）
[FullUnlock] PlentyTraits 已解锁（4 项）
[FullUnlock] Bad North Multiplayer 无需解锁（6 项由该 mod 自行管理）
[FullUnlock] 汇总：共 62 项已写入手抄本（原版 58 项 + mod 4 项），另有 6 项由对应 mod 自行管理，无需任何额外操作
[FullUnlock] Relics & Realms 已解锁（宝石 20 颗，境界 I-12 已解锁；本次新写入 20 个键，反射到 20 个定义）
```

> 第二次进游戏时“本次写入 0 项”属于正常：条目已经满级且初始装备标记符合配置，插件不会重复写。
> 开启 `UnlockCampaignItemPool` 时才会额外出现 `当前战役道具池已解锁（新增 N 项）`。

## 配置（BepInEx/config/com.shanmai.fullunlock.cfg）

| 段 / 键 | 默认 | 说明 |
| --- | --- | --- |
| `Unlock` / `MarkAsStartingItems` | `false` | 把全部物品 / 特质标记为“初始装备”。开启后新开局可以从全部装备里挑选，战役奖励生成也会变化 |
| `Unlock` / `UnlockCampaignItemPool` | `false` | 把全部道具写进当前战役存档（`campaign.inventory`），道具会出现在玩家的战役仓库里 |
| `Unlock` / `ResetStartingItems` | `false` | 一次性修复开关：重建手抄本条目，清掉旧版本写入的初始装备标记；执行后自动改回 `false` |
| `Unlock` / `AutoStartItemsForMultiplayer` | `false` | 兼容预设：仅当**检测到 Bad North Multiplayer** 时生效，把物品 / 特质标记为初始装备（其指挥官选装列表读取 `startingItems` / `startingTraits`）。默认关闭；开启后多人局可选全部装备，但单人新开局的初始装备也会变成全部 |
| `Relics & Realms` / `UnlockAllRelics` | `true` | 解锁 Relics & Realms 全部宝石 |
| `Relics & Realms` / `UnlockAllRealms` | `true` | 解锁 Relics & Realms 全部境界（I-XII） |
| `Diagnostics` / `DumpUnlockedList` | `false` | 逐条打印本插件本次写入/跳过的定义，排查问题时开启 |

## 排错

- 日志出现 `当前游戏的 Assembly-CSharp 缺少本插件需要的类型 / 成员：...`：
  说明游戏版本与插件不匹配，或 `Assembly-CSharp.dll` 被整合包 / 汉化 / 破解替换过。
  此时插件**不挂补丁**，不会崩游戏，也不会改存档。
- 日志出现 `手抄本存档结构不可用（...）`：手抄本类型/方法签名与预期不符，插件跳过手抄本解锁，其余功能照常。
- 让玩家自查 DLL 里是否有这个类型（在游戏根目录执行，输出为空即缺失）：

```
powershell -NoProfile -Command "$a=[Reflection.Assembly]::ReflectionOnlyLoadFrom('BadNorth_Data\Managed\Assembly-CSharp.dll'); $a.GetType('Voxels.TowerDefense.ProfileInternals.MetaInventory')"
```

## 构建

项目为老式（非 SDK）csproj，目标框架 .NET Framework 4.7.2，引用本机游戏目录下的
`Assembly-CSharp.dll`、`UnityEngine.CoreModule.dll`、`0Harmony.dll`、`BepInEx.dll`：

```
D:\Steam\steamapps\common\BadNorth\BadNorth_Data\Managed\
D:\Steam\steamapps\common\BadNorth\BepInEx\core\
```

若游戏安装在其他位置，请同步修改 `BadNorthUnlockAll.csproj` 里的 `HintPath`。

### 严格编译校验（推荐）

用游戏自带的 `mscorlib.dll`（.NET 2.0 profile）做 `/nostdlib+` 编译，可以提前暴露任何 .NET 4 专属 API：

```
$m = 'D:\Steam\steamapps\common\BadNorth\BadNorth_Data\Managed\'
$b = 'D:\Steam\steamapps\common\BadNorth\BepInEx\core\'
dotnet exec "C:\Program Files\dotnet\sdk\8.0.421\Roslyn\bincore\csc.dll" /nologo /target:library `
  /out:build_out\BadNorthUnlockAll.dll /nostdlib+ /langversion:7.3 /optimize+ /define:TRACE `
  /reference:${m}mscorlib.dll /reference:${m}System.dll /reference:${m}System.Core.dll `
  /reference:${m}UnityEngine.dll /reference:${m}UnityEngine.CoreModule.dll /reference:${m}Assembly-CSharp.dll `
  /reference:${b}BepInEx.dll /reference:${b}0Harmony.dll `
  FullUnlockPatch.cs FullUnlockPlugin.cs GameCompat.cs Patch_OnUserSaveLoaded.cs RelicUnlockPatch.cs ModLog.cs Properties\AssemblyInfo.cs
```

编译产物 `BadNorthUnlockAll.dll` 放入 `BepInEx\plugins\` 即可。

### 构建产物自检（防回归）

上面两条路（VS/csproj 用 .NET 4.7.2 引用、严格模式用游戏自带 mscorlib）都要满足：

1. 程序集里 **不能出现 `MetaInventory` 的 TypeRef**（手抄本必须走反射）；
2. `op_*` 成员引用只允许：`System.String::op_Equality`、`UnityEngine.Object::op_Equality/op_Inequality`、
   `SerializableHeroUpgrade::op_Implicit`。**不允许出现 `System.Type` / `System.Reflection.*` / `Assembly` 的 `op_*`**
   （Unity 的 .NET 2.0 Mono 没有这些运算符，v1.3.3 与 v1.3.5 都在这上面踩过）。

用 Mono.Cecil 一条命令即可核对：

```
Add-Type -Path "$b..\core\Mono.Cecil.dll"
$a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($pwd.Path + '\build_out\BadNorthUnlockAll.dll')
$a.MainModule.GetMemberReferences() | ? { $_.Name -like 'op_*' } |
  % { $_.DeclaringType.FullName + '::' + $_.Name } | Sort-Object -Unique
$a.MainModule.GetTypeReferences() | ? { $_.Name -like '*MetaInventory*' }   # 必须为空
```

另外，`GameCompat.Probe()` 会对**每一个**被强类型引用的游戏成员（`Profile.userSave`、`UserSave.campaignPrefs`、
`CampaignPrefs.difficulty`、`HeroUpgradeDefinition.upgradeType/numLevels/typeEnum/uniqueId`、
`HeroUpgradeType.canBeStartItem`、`ResourceList<T>.list`、`CampaignSave.inventory`、
`SerializableHeroUpgrade.definition/op_Implicit`）逐个校验“存在 + 类型一致”，缺任何一个都只降级、不崩溃。

## 变更记录

- **v1.3.5**
  - 修复玩家缺少 `MetaInventory` 类型时 `TypeLoadException` 冒到游戏 Update 循环的问题：手抄本改为反射访问 + 启动 API 自检，缺类型时安全跳过。
  - 默认只写用户存档里的手抄本：不再写战役存档（`campaign.inventory`），不再把物品 / 特质标成初始装备。
  - 新增 `MarkAsStartingItems` / `UnlockCampaignItemPool` / `ResetStartingItems` 三项配置；扩展、mod 定义同样覆盖。
  - 新增 `AutoStartItemsForMultiplayer`（默认 false，仅在检测到 Bad North Multiplayer 时生效）：需要多人局可选全部装备的玩家可开启，等价 1.3.4 的初始装备行为。
  - 已有条目且满级时不再重复写入，减少存档写入与日志噪音。
- **v1.3.4**：解锁日志改走 BepInEx 日志通道，新增逐项清单开关。
- **v1.3.3**：修复 .NET 2.0 运行时缺失 Type/Assembly 运算符导致的崩溃。
- **v1.3.2**：精简注释与无用代码，修复两处隐患。
- **v1.3.1**：解锁日志按原版 / 具体 mod 分项输出。
- **v1.3.0**：战役内道具池同步解锁，`canBeStartItem` 覆盖注入型定义。
- **v1.2.0**：兼容 Relics & Realms 的宝石 / 境界解锁。
- **v1.0.0**：创建项目。
