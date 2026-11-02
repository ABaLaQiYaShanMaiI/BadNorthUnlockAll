# BadNorthUnlockAll

Bad North（北方绝境）的 BepInEx 解锁插件：一键解锁手抄本（物品 / 特质 / 消耗品）、极难难度，
并兼容 **Relics & Realms** 遗物（宝石）系统的解锁。

## 功能

| 功能 | 实现方式 |
| --- | --- |
| 原版手抄本全解锁 | Harmony 补丁 `Profile.OnUserSaveLoaded` → 遍历 `ResourceList<HeroUpgradeDefinition>`，把每项升到最高级并通过 `MetaInventory.Add` 写入存档 |
| 全部装备可作为初始装备 | 把每个 `HeroUpgradeDefinition.upgradeType.canBeStartItem` 置为 `true` |
| 解锁极难难度 | `userSave.maxDifficulty = Difficulty.VeryHard` 并保存 |
| 战役内道具池 | 照抄游戏自带的控制台命令 `Inventory.AddAll`：同时写 `campaign.inventory`，进行中的战役也立刻全部可购买/升级 |
| Relics & Realms 宝石解锁 | 该类 mod 不走原版手抄本体系，改用 Unity `PlayerPrefs` 存解锁状态，见下文 |

## 两套库存的区别（为什么两处都要写）

| | 数据结构 | 读取方 | 作用 |
| --- | --- | --- | --- |
| `Profile.userSave.inventory` | `MetaInventory`（进存档的 `UpgradeEntry` 列表 + 运行时 `startingItems` / `startingTraits`） | 新游戏界面（`NewGameOptionsPopup`）、开局特质分配（`HeroTraitAssigner`） | 手抄本：决定"新开一局时能选什么" |
| `Profile.campaign.inventory` | `List<SerializableHeroUpgrade>` | 战役内升级界面（`SuperUpgradesCampaignProxy` → `IUpgradesProxy.inventory`） | 决定"当前这局能买/升级什么" |

所以只写第一处的话，老存档（进行中的战役）里拿不到新解锁的道具；本插件按游戏自带作弊命令的做法两处都写，且用"引用变化才追加"的轮询来覆盖新建 / 读档 / 切档，玩家不需要做任何操作。

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
- **运行时注入的新特质（如 Bad North Multiplayer 的 6 个自定义特质）**：由 mod 在 `Awake` 里用 `ScriptableObject.CreateInstance<HeroUpgradeDefinition>()` 创建、注册进 `ResourceList<HeroUpgradeDefinition>.list`，再通过它自己的选人界面直接提供，并用 Harmony Prefix 主动屏蔽 `MetaInventory.Add` 使其不落盘。也就是说这类特质"天然永远可选、本来就不需要解锁"，本插件写入时会被对方的 Prefix 拦下，属于预期行为。
- 本插件对这些注入型定义唯一的动作是把 `upgradeType.canBeStartItem` 置为 `true`（遍历 `ResourceList` 而非 `Resources.LoadAll`，因此也能覆盖注入型定义），只影响信息面板的展示判定，不改变任何数值。

## 解锁覆盖面（v1.3.1 核对结果）

Bad North 里所有"可解锁内容"最终都是 `HeroUpgradeDefinition`（物品 / 特质 / 消耗品 / 技能 / 职业），且必须注册进 `ResourceList<HeroUpgradeDefinition>` 才会被游戏 UI 使用。本插件直接遍历这张表，因此：

| 来源 | 是否需要额外操作 | 说明 |
| --- | --- | --- |
| 原版游戏 | 否 | 写入手抄本 + 战役道具池，日志 `原版手抄本和难度已解锁` |
| 旧灰复燃的战旗（林风柳整合包） | 否 | 其内容烘焙在替换版 `Assembly-CSharp.dll` 中，同样出现在 `ResourceList`，随原版一起解锁 |
| PlentyTraits / PlentyTraits AxeEnhance（`nacu.*`，走 BNAPI） | 否 | `CustomTraits.RegisterTrait` 会注册进 `ResourceList`；其中 `alwaysUnlocked=false` 的（荆棘、追猎）原本要随进度解锁，本插件直接解锁，日志 `PlentyTraits 已解锁（N 项）` |
| 只改数值 / 技能 / 敌人的 mod（HeavyShield、ShieldSpear、FireMod、EndlessFinale、Commanders Sail In…） | 不需要 | 它们不新增可解锁定义 |
| Bad North Multiplayer 的 6 个自定义特质 | 不需要 | 该 mod 用 Harmony Prefix 主动拦截 `MetaInventory.Add` 并在自己的选人界面提供，日志 `Bad North Multiplayer 无需解锁（6 项由该 mod 自行管理）` |
| Relics & Realms（gwm 的宝石系统） | 否 | 走 PlayerPrefs，由 `RelicUnlockPatch` 处理，日志 `Relics & Realms 已解锁（宝石 20 颗，境界 I-12 已解锁）` |

> 唯一无法自动覆盖的情况：mod 自带独立存档/配置、且完全不经过 `HeroUpgradeDefinition` 与 `PlayerPrefs` —— 这类必须按它自己的机制单独适配（Relics & Realms 就是这种，已适配）。

### 日志示例（装齐 mod 时）

```
[FullUnlock] 原版手抄本和难度已解锁（58 项：物品/特质/消耗品已全部可作初始装备，极难难度已开启）
[FullUnlock] PlentyTraits 已解锁（4 项）
[FullUnlock] Bad North Multiplayer 无需解锁（6 项由该 mod 自行管理）
[FullUnlock] Relics & Realms 已解锁（宝石 20 颗，境界 I-12 已解锁；本次新写入 20 个键，反射到 20 个定义）
[FullUnlock] 汇总：共 62 项已解锁（原版 58 项 + mod 4 项），另有 6 项由对应 mod 自行管理，无需任何额外操作
[FullUnlock] 当前战役道具池已解锁（新增 46 项，可直接购买/升级）
```

> 数字随所装 mod 变化；mod 名优先取 BepInEx 插件元数据里的 `Name`，已知 GUID 会替换为更常用的简称（如 `nacu.plentytraits` → `PlentyTraits`、`com.gmw.relicselection` → `Relics & Realms`）。若某个 mod 用 `ScriptableObject.CreateInstance<HeroUpgradeDefinition>()`（而非自己的子类）创建条目，这些条目会被归入"原版"计数——只是统计口径问题，解锁行为不受影响。

## 配置（BepInEx/config/com.shanmai.fullunlock.cfg）

| 段 / 键 | 默认 | 说明 |
| --- | --- | --- |
| `Relics & Realms` / `UnlockAllRelics` | `true` | 解锁 Relics & Realms 全部宝石 |
| `Relics & Realms` / `UnlockAllRealms` | `true` | 解锁 Relics & Realms 全部境界（I-XII） |

## 构建

项目为老式（非 SDK）csproj，目标框架 .NET Framework 4.7.2，引用本机游戏目录下的
`Assembly-CSharp.dll`、`UnityEngine.CoreModule.dll`、`0Harmony.dll`、`BepInEx.dll`：

```
D:\Steam\steamapps\common\BadNorth\BadNorth_Data\Managed\
D:\Steam\steamapps\common\BadNorth\BepInEx\core\
```

若游戏安装在其他位置，请同步修改 `BadNorthUnlockAll.csproj` 里的 `HintPath`。

编译产物 `BadNorthUnlockAll.dll` 放入 `BepInEx\plugins\` 即可。
