using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.ProfileInternals;

namespace FullUnlockMod
{
	// 解锁主流程（装上即全部生效，玩家不需要任何按键或配置）：
	// 1) 手抄本：把 ResourceList<HeroUpgradeDefinition> 里的每一项都写进 Profile.userSave.inventory，
	//    Item / Trait / Consumable 标记为初始装备 —— 原版定义和 mod 注入的定义走同一条路径；
	// 2) 战役内可购池：对齐游戏自带控制台命令 Inventory.AddAll，写 Profile.campaign.inventory；
	// 3) 极难难度；
	// 4) Relics & Realms 的宝石 / 境界由 RelicUnlockPatch 走 PlayerPrefs。
	// 日志按"原版"和"具体 mod"分别输出，未列出的 mod 直接用其 BepInEx 插件名。
	public static class FullUnlockPatch
	{
		private sealed class ModTally
		{
			public int unlocked;

			public int blocked;
		}

		// 常见 mod 的显示名（键为 BepInEx GUID）
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

		// 战役对象（CampaignSave）每换一次就补一次解锁
		private static CampaignSave watchedCampaign;

		public static void ApplyFullUnlock()
		{
			Debug.Log("[FullUnlockPatch] ApplyFullUnlock start");
			try
			{
				UserSave userSave = Profile.userSave;
				bool flag = userSave == null || userSave.inventory == null;
				if (flag)
				{
					Debug.Log("[FullUnlockPatch] userSave 尚未加载，跳过本次解锁");
					return;
				}
				MetaInventory inventory = userSave.inventory;
				bool veryHard = UnlockVeryHardDifficulty();
				int vanilla = 0;
				Dictionary<Assembly, ModTally> tallies = new Dictionary<Assembly, ModTally>();
				foreach (HeroUpgradeDefinition definition in ResourceList<HeroUpgradeDefinition>.list)
				{
					bool flag2 = definition == null;
					if (flag2)
					{
						continue;
					}
					HeroUpgradeTypeEnum typeEnum = definition.typeEnum;
					bool isStarting = typeEnum == HeroUpgradeTypeEnum.Item || typeEnum == HeroUpgradeTypeEnum.Trait || typeEnum == HeroUpgradeTypeEnum.Consumable;
					int level = Math.Max(0, definition.numLevels - 1);
					inventory.Add(definition, level, isStarting);
					bool flag3 = definition.upgradeType != null;
					if (flag3)
					{
						definition.upgradeType.canBeStartItem = true;
					}
					// 写入后回查一次：若某个 mod 主动拦截 MetaInventory.Add（例如 Bad North MP 的自定义特质），
					// 这里能立刻识别出来，日志就不会谎报"已解锁"。
					bool added = IsInInventory(inventory, definition);
					Assembly assembly = definition.GetType().Assembly;
					bool flag4 = assembly == typeof(HeroUpgradeDefinition).Assembly;
					if (flag4)
					{
						bool flag5 = added;
						if (flag5)
						{
							vanilla++;
						}
					}
					else
					{
						ModTally modTally;
						bool flag6 = !tallies.TryGetValue(assembly, out modTally);
						if (flag6)
						{
							modTally = new ModTally();
							tallies.Add(assembly, modTally);
						}
						bool flag7 = added;
						if (flag7)
						{
							modTally.unlocked++;
						}
						else
						{
							modTally.blocked++;
						}
					}
				}
				string text = veryHard ? "，极难难度已开启" : "";
				Debug.Log(string.Format("[FullUnlock] 原版手抄本和难度已解锁（{0} 项：物品/特质/消耗品已全部可作初始装备{1}）", vanilla, text));
				foreach (KeyValuePair<Assembly, ModTally> keyValuePair in tallies)
				{
					string modName = GetModName(keyValuePair.Key);
					ModTally value = keyValuePair.Value;
					bool flag8 = value.unlocked > 0;
					if (flag8)
					{
						string text2 = value.blocked > 0 ? ("，" + value.blocked + " 项由该 mod 自行管理") : "";
						Debug.Log(string.Format("[FullUnlock] {0} 已解锁（{1} 项{2}）", modName, value.unlocked, text2));
					}
					else
					{
						Debug.Log(string.Format("[FullUnlock] {0} 无需解锁（{1} 项由该 mod 自行管理）", modName, value.blocked));
					}
				}
				int modUnlocked = 0;
				int modBlocked = 0;
				foreach (KeyValuePair<Assembly, ModTally> keyValuePair2 in tallies)
				{
					modUnlocked += keyValuePair2.Value.unlocked;
					modBlocked += keyValuePair2.Value.blocked;
				}
				Debug.Log(string.Format("[FullUnlock] 汇总：共 {0} 项已解锁（原版 {1} 项 + mod {2} 项），另有 {3} 项由对应 mod 自行管理，无需任何额外操作", vanilla + modUnlocked, vanilla, modUnlocked, modBlocked));
				TickCampaignUnlock();
			}
			catch (Exception ex)
			{
				Debug.LogError("[FullUnlockPatch] 发生错误：" + ((ex != null) ? ex.ToString() : null));
			}
		}

		// 极难难度。返回 true 表示当前已经是 VeryHard（本次开启或本来就已开启）。
		private static bool UnlockVeryHardDifficulty()
		{
			try
			{
				UserSave userSave = Profile.userSave;
				bool flag = userSave == null;
				if (flag)
				{
					return false;
				}
				bool flag2 = userSave.maxDifficulty == Difficulty.VeryHard;
				if (flag2)
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
				Debug.LogError("[FullUnlockPatch] 解锁极难难度时发生错误：" + ((ex != null) ? ex.ToString() : null));
				return false;
			}
		}

		// 战役内的"可购买池"是 CampaignSave.inventory（List<SerializableHeroUpgrade>），
		// 与手抄本 userSave.inventory（MetaInventory）是两套独立数据：前者由战役升级界面
		// （SuperUpgradesCampaignProxy）读取，后者由新游戏选人界面读取。
		// 游戏自带的控制台命令 Inventory.AddAll 会把道具同时写进这两边，这里照做，
		// 这样进行中的战役也无需重开就能拿到全部道具。
		public static void TickCampaignUnlock()
		{
			CampaignSave campaign = Profile.campaign;
			bool flag = campaign == null || campaign == watchedCampaign;
			if (!flag)
			{
				watchedCampaign = campaign;
				ApplyCampaignUnlock(campaign);
			}
		}

		public static void ApplyCampaignUnlock(CampaignSave campaign)
		{
			try
			{
				bool flag = campaign == null || campaign.inventory == null;
				if (flag)
				{
					return;
				}
				int num = 0;
				foreach (HeroUpgradeDefinition heroUpgradeDefinition in ResourceList<HeroUpgradeDefinition>.list)
				{
					bool flag2 = heroUpgradeDefinition == null || heroUpgradeDefinition.upgradeType == null;
					if (flag2)
					{
						continue;
					}
					HeroUpgradeTypeEnum typeEnum = heroUpgradeDefinition.typeEnum;
					bool flag3 = typeEnum == HeroUpgradeTypeEnum.Item || typeEnum == HeroUpgradeTypeEnum.Consumable;
					if (!flag3)
					{
						continue;
					}
					bool flag4 = CampaignHas(campaign, heroUpgradeDefinition);
					if (!flag4)
					{
						campaign.inventory.Add(heroUpgradeDefinition);
						num++;
					}
				}
				bool flag5 = num > 0;
				if (flag5)
				{
					Debug.Log(string.Format("[FullUnlock] 当前战役道具池已解锁（新增 {0} 项，可直接购买/升级）", num));
				}
			}
			catch (Exception ex)
			{
				Debug.LogError("[FullUnlockPatch] 解锁当前战役道具时发生错误：" + ((ex != null) ? ex.ToString() : null));
			}
		}

		private static bool CampaignHas(CampaignSave campaign, HeroUpgradeDefinition definition)
		{
			for (int i = 0; i < campaign.inventory.Count; i++)
			{
				SerializableHeroUpgrade serializableHeroUpgrade = campaign.inventory[i];
				bool flag = serializableHeroUpgrade != null && serializableHeroUpgrade.definition == definition;
				if (flag)
				{
					return true;
				}
			}
			return false;
		}

		private static bool IsInInventory(MetaInventory inventory, HeroUpgradeDefinition definition)
		{
			int level;
			bool isNew;
			bool isStarting;
			return inventory.Get(definition, out level, out isNew, out isStarting);
		}

		// 解析某个程序集对应的 mod 显示名：优先用 BepInEx 插件元数据（GUID/Name），
		// 已知 GUID 走别名表，实在找不到就退回程序集名。
		private static string GetModName(Assembly assembly)
		{
			try
			{
				foreach (PluginInfo pluginInfo in Chainloader.PluginInfos.Values)
				{
					bool flag = pluginInfo == null || pluginInfo.Instance == null;
					if (!flag)
					{
						bool flag2 = pluginInfo.Instance.GetType().Assembly == assembly;
						if (flag2)
						{
							BepInPlugin metadata = pluginInfo.Metadata;
							string text = metadata != null ? metadata.GUID : null;
							string text2 = metadata != null ? metadata.Name : null;
							string text3 = null;
							bool flag3 = text != null;
							if (flag3)
							{
								KnownModNames.TryGetValue(text, out text3);
							}
							bool flag4 = text3 != null;
							if (flag4)
							{
								return text3;
							}
							bool flag5 = !string.IsNullOrEmpty(text2);
							if (flag5)
							{
								return text2;
							}
							break;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[FullUnlockPatch] 读取 BepInEx 插件信息失败：" + ex.Message);
			}
			return assembly.GetName().Name;
		}
	}
}
