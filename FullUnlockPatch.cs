using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Voxels.TowerDefense;
using Voxels.TowerDefense.ProfileInternals;

namespace FullUnlockMod
{
	// 解锁主流程，装上即生效，玩家不需要任何按键或配置：
	// 1. 手抄本：ResourceList<HeroUpgradeDefinition> 每项写入 userSave.inventory，Item/Trait/Consumable 标记为初始装备；
	// 2. 战役道具池：对齐游戏内置命令 Inventory.AddAll，写 campaign.inventory；
	// 3. 极难难度；4. Relics & Realms 宝石由 RelicUnlockPatch 走 PlayerPrefs。
	public static class FullUnlockPatch
	{
		private sealed class ModTally
		{
			public int unlocked;

			public int blocked;
		}

		// 常见 mod 的显示名（键为 BepInEx GUID），未列出的用插件元数据里的 Name
		private static readonly Dictionary<string, string> KnownModNames = new Dictionary<string, string>
		{
			{ "nacu.bnapi", "BN Mod API" },
			{ "nacu.plentytraits", "PlentyTraits" },
			{ "com.badnorth.plentytraits.axeenhance", "PlentyTraits 掷斧手补强" },
			{ "com.modder.giant_command_ring", "Giant Command Ring" },
			{ "com.linfeng.heavy_shield", "HeavyShield" },
			{ "com.linfeng.shield_spear", "ShieldSpear" },
			{ "com.linfeng.together", "Together" },
			{ "com.antigravity.firearrows", "火矢" },
			{ "com.modder.guerrilla_archery", "游击队弓箭" },
			{ "com.modder.viking_spearman", "维京长矛兵" },
			{ "com.gmw.relicselection", "Relics & Realms" },
			{ "com.bananasplit.badnorthmp", "Bad North Multiplayer" }
		};

		// 当前战役；引用变化（新建 / 读档 / 切档）时补一次解锁
		private static CampaignSave watchedCampaign;

		public static void ApplyFullUnlock()
		{
			try
			{
				UserSave userSave = Profile.userSave;
				bool notLoaded = userSave == null || userSave.inventory == null;
				if (notLoaded)
				{
					ModLog.Info("[FullUnlockPatch] userSave 尚未加载，跳过本次解锁");
					return;
				}
				MetaInventory inventory = userSave.inventory;
				bool veryHard = UnlockVeryHardDifficulty();
				int vanilla = 0;
				Dictionary<Assembly, ModTally> tallies = new Dictionary<Assembly, ModTally>();
				bool dumpList = FullUnlockPlugin.DumpUnlocked != null && FullUnlockPlugin.DumpUnlocked.Value;
				foreach (HeroUpgradeDefinition definition in ResourceList<HeroUpgradeDefinition>.list)
				{
					// 单个畸形定义不应中断整体解锁
					try
					{
						bool invalid = definition == null || definition.upgradeType == null;
						if (invalid)
						{
							continue;
						}
						HeroUpgradeTypeEnum typeEnum = definition.typeEnum;
						bool isStarting = typeEnum == HeroUpgradeTypeEnum.Item || typeEnum == HeroUpgradeTypeEnum.Trait || typeEnum == HeroUpgradeTypeEnum.Consumable;
						inventory.Add(definition, Math.Max(0, definition.numLevels - 1), isStarting);
						definition.upgradeType.canBeStartItem = true;
						bool added = IsInInventory(inventory, definition);
						Assembly assembly = definition.GetType().Assembly;
						if (dumpList)
						{
							ModLog.Info(string.Format("[FullUnlock] + {0} | {1} | {2} | 写入成功={3}", definition.name, typeEnum, assembly.GetName().Name, added));
						}
						// 用 ReferenceEquals：Unity 的 .NET 2.0 Mono 里没有 Assembly.op_Equality
						bool isVanilla = object.ReferenceEquals(assembly, typeof(HeroUpgradeDefinition).Assembly);
						if (isVanilla)
						{
							if (added)
							{
								vanilla++;
							}
							continue;
						}
						ModTally tally;
						bool first = !tallies.TryGetValue(assembly, out tally);
						if (first)
						{
							tally = new ModTally();
							tallies.Add(assembly, tally);
						}
						if (added)
						{
							tally.unlocked++;
						}
						else
						{
							tally.blocked++;
						}
					}
					catch (Exception ex)
					{
						string name = definition != null ? definition.name : "<null>";
						ModLog.Warn("[FullUnlockPatch] 跳过异常定义 " + name + "：" + ex.Message);
					}
				}
				string difficultyText = veryHard ? "，极难难度已开启" : "";
				ModLog.Info(string.Format("[FullUnlock] 原版手抄本和难度已解锁（{0} 项：物品/特质/消耗品已全部可作初始装备{1}）", vanilla, difficultyText));
				int modUnlocked = 0;
				int modBlocked = 0;
				foreach (KeyValuePair<Assembly, ModTally> pair in tallies)
				{
					modUnlocked += pair.Value.unlocked;
					modBlocked += pair.Value.blocked;
					string modName = GetModName(pair.Key);
					bool any = pair.Value.unlocked > 0;
					if (any)
					{
						string extra = pair.Value.blocked > 0 ? ("，" + pair.Value.blocked + " 项由该 mod 自行管理") : "";
						ModLog.Info(string.Format("[FullUnlock] {0} 已解锁（{1} 项{2}）", modName, pair.Value.unlocked, extra));
					}
					else
					{
						ModLog.Info(string.Format("[FullUnlock] {0} 无需解锁（{1} 项由该 mod 自行管理）", modName, pair.Value.blocked));
					}
				}
				ModLog.Info(string.Format("[FullUnlock] 汇总：共 {0} 项已解锁（原版 {1} 项 + mod {2} 项），另有 {3} 项由对应 mod 自行管理，无需任何额外操作", vanilla + modUnlocked, vanilla, modUnlocked, modBlocked));
				TickCampaignUnlock();
			}
			catch (Exception ex)
			{
				ModLog.Error("[FullUnlockPatch] 发生错误：" + ((ex != null) ? ex.ToString() : null));
			}
		}

		// 返回 true 表示当前已是 VeryHard（本次开启或本来就已开启）
		private static bool UnlockVeryHardDifficulty()
		{
			try
			{
				UserSave userSave = Profile.userSave;
				bool notLoaded = userSave == null;
				if (notLoaded)
				{
					return false;
				}
				bool already = userSave.maxDifficulty == Difficulty.VeryHard;
				if (already)
				{
					return true;
				}
				userSave.maxDifficulty = Difficulty.VeryHard;
				userSave.campaignPrefs.difficulty = Difficulty.VeryHard;
				Profile.SaveUserSave();
				return true;
			}
			catch (Exception ex)
			{
				ModLog.Error("[FullUnlockPatch] 解锁极难难度时发生错误：" + ((ex != null) ? ex.ToString() : null));
				return false;
			}
		}

		// 战役道具池（campaign.inventory）与手抄本（userSave.inventory）是两套数据，
		// 这里按 Profile.campaign 引用变化补写，覆盖新建 / 读档 / 切档。
		public static void TickCampaignUnlock()
		{
			CampaignSave campaign = Profile.campaign;
			bool sameOrNone = campaign == null || campaign == watchedCampaign;
			if (sameOrNone)
			{
				return;
			}
			watchedCampaign = campaign;
			ApplyCampaignUnlock(campaign);
		}

		public static void ApplyCampaignUnlock(CampaignSave campaign)
		{
			try
			{
				bool notLoaded = campaign == null || campaign.inventory == null;
				if (notLoaded)
				{
					return;
				}
				int count = 0;
				foreach (HeroUpgradeDefinition definition in ResourceList<HeroUpgradeDefinition>.list)
				{
					bool invalid = definition == null || definition.upgradeType == null;
					if (invalid)
					{
						continue;
					}
					HeroUpgradeTypeEnum typeEnum = definition.typeEnum;
					bool purchasable = typeEnum == HeroUpgradeTypeEnum.Item || typeEnum == HeroUpgradeTypeEnum.Consumable;
					if (!purchasable)
					{
						continue;
					}
					bool exists = CampaignHas(campaign, definition);
					if (exists)
					{
						continue;
					}
					campaign.inventory.Add(definition);
					count++;
				}
				bool added = count > 0;
				if (added)
				{
					ModLog.Info(string.Format("[FullUnlock] 当前战役道具池已解锁（新增 {0} 项，可直接购买/升级）", count));
				}
			}
			catch (Exception ex)
			{
				ModLog.Error("[FullUnlockPatch] 解锁当前战役道具时发生错误：" + ((ex != null) ? ex.ToString() : null));
			}
		}

		private static bool CampaignHas(CampaignSave campaign, HeroUpgradeDefinition definition)
		{
			string uniqueId = definition.uniqueId;
			for (int i = 0; i < campaign.inventory.Count; i++)
			{
				SerializableHeroUpgrade entry = campaign.inventory[i];
				// 按 uniqueId 比较：读档后 definition 可能被重新解析成新实例，引用比较会漏判
				bool match = entry != null && entry.definition != null && entry.definition.uniqueId == uniqueId;
				if (match)
				{
					return true;
				}
			}
			return false;
		}

		// 回查写入结果：被 mod 的 Harmony Prefix 拦掉时返回 false
		private static bool IsInInventory(MetaInventory inventory, HeroUpgradeDefinition definition)
		{
			int level;
			bool isNew;
			bool isStarting;
			return inventory.Get(definition, out level, out isNew, out isStarting);
		}

		// mod 显示名：优先 BepInEx 元数据（已知 GUID 用别名），否则退回程序集名
		private static string GetModName(Assembly assembly)
		{
			try
			{
				foreach (PluginInfo pluginInfo in Chainloader.PluginInfos.Values)
				{
					bool usable = pluginInfo != null && pluginInfo.Instance != null;
					if (!usable)
					{
						continue;
					}
					bool sameAssembly = object.ReferenceEquals(pluginInfo.Instance.GetType().Assembly, assembly);
					if (!sameAssembly)
					{
						continue;
					}
					BepInPlugin metadata = pluginInfo.Metadata;
					string guid = metadata != null ? metadata.GUID : null;
					string name = metadata != null ? metadata.Name : null;
					string alias = null;
					if (guid != null)
					{
						KnownModNames.TryGetValue(guid, out alias);
					}
					bool hasAlias = alias != null;
					if (hasAlias)
					{
						return alias;
					}
					bool hasName = !string.IsNullOrEmpty(name);
					if (hasName)
					{
						return name;
					}
					break;
				}
			}
			catch (Exception ex)
			{
				ModLog.Warn("[FullUnlockPatch] 读取 BepInEx 插件信息失败：" + ex.Message);
			}
			return assembly.GetName().Name;
		}
	}
}
