# BadNorthUnlockAll

Bad North（北方绝境）的 BepInEx 解锁插件：一键解锁手抄本（物品 / 特质 / 消耗品）、极难难度，
并兼容 **Relics & Realms** 遗物（宝石）系统的解锁。

## 功能

| 功能 | 实现方式 |
| --- | --- |
| 原版手抄本全解锁 | Harmony 补丁 `Profile.OnUserSaveLoaded` → 遍历 `ResourceList<HeroUpgradeDefinition>`，把每项升到最高级并通过 `MetaInventory.Add` 写入存档 |
| 全部装备可作为初始装备 | 把每个 `HeroUpgradeDefinition.upgradeType.canBeStartItem` 置为 `true` |
| 解锁极难难度 | `userSave.maxDifficulty = Difficulty.VeryHard` 并保存 |
| Relics & Realms 宝石解锁 | 该类 mod 不走原版手抄本体系，改用 Unity `PlayerPrefs` 存解锁状态，见下文 |

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
