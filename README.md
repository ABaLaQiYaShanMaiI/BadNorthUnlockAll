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
