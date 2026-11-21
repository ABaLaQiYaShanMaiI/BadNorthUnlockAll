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
	// 1. 手抄本：把 ResourceList<HeroUpgradeDefinition> 每项写入 userSave.inventory —— 只写“用户存档”里的手抄本，
	//    打开手抄本界面就能看到全部条目，不会给玩家背包 / 战役仓库塞道具；
	// 2. 极难难度；
	// 3. “全部作为初始装备”（isStarting / canBeStartItem）与“战役内道具池”（campaign.inventory）默认关闭，
	//    因为它们会让新开局直接可选全部装备、并改变战役奖励生成；需要旧行为的玩家可在配置里打开。
	//
	// 手抄本字段的类型 MetaInventory 不做编译期引用：读写全部走 GameCompat 反射，
	// 玩家那份 Assembly-CSharp 缺这个类型时只会降级提示，不会在 JIT 阶段抛 TypeLoadException。
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

		// 当前战役；引用变化（新建 / 读档 / 切档）时补一次解锁。
		// 这里存 object 而不是 CampaignSave：避免本类型的静态字段对游戏类型产生硬引用。
		private static object watchedCampaign;

		// “旧版初始装备标记”的提示每次运行只打一次
		private static bool startingHintLogged;

		public static void ApplyFullUnlock()
		{
			// 关键类型缺失时直接退出：本方法引用的游戏类型一旦解析失败，Mono 会在 JIT 阶段抛
			// TypeLoadException，try/catch 兜不住（会冒到游戏的 Update 循环）。
			bool unsupported = !GameCompat.CoreSupported;
			if (unsupported)
			{
				ModLog.Warn("[FullUnlockPatch] 当前游戏缺失本插件需要的类型（" + GameCompat.CoreMissing + "），已跳过本次解锁");
				return;
			}
			try
			{
				UserSave userSave = Profile.userSave;
				bool notLoaded = userSave == null;
				if (notLoaded)
				{
					ModLog.Info("[FullUnlockPatch] userSave 尚未加载，跳过本次解锁");
					return;
				}
				bool markStarting = FullUnlockPlugin.EffectiveMarkAsStartingItems;
				bool resetStarting = FullUnlockPlugin.ResetStartingItems != null && FullUnlockPlugin.ResetStartingItems.Value;
				object inventory = GameCompat.InventorySupported ? GameCompat.GetInventory(userSave) : null;
				bool resetApplied = false;
				bool resetRequested = resetStarting && inventory != null && GameCompat.InventorySupported;
				if (resetRequested)
				{
					object fresh = GameCompat.ResetInventory(userSave);
					if (fresh != null)
					{
						inventory = fresh;
						resetApplied = true;
						ModLog.Info("[FullUnlock] 已重建手抄本条目：清掉旧版本写入的“初始装备”标记，物品 / 特质不会再出现在新开局的初始装备里");
					}
				}
				bool inventoryUsable = inventory != null && GameCompat.InventorySupported;
				if (!inventoryUsable)
				{
					ModLog.Warn("[FullUnlock] 手抄本存档结构不可用（" + GameCompat.InventoryProblem + "），本次跳过手抄本解锁");
				}
				bool veryHard = UnlockVeryHardDifficulty();
				int vanilla = 0;
				int changed = 0;
				int startableCount = 0;
				int startingCount = 0;
				Dictionary<Assembly, ModTally> tallies = new Dictionary<Assembly, ModTally>();
				bool dumpList = FullUnlockPlugin.DumpUnlocked != null && FullUnlockPlugin.DumpUnlocked.Value;
				List<HeroUpgradeDefinition> definitions = ResourceList<HeroUpgradeDefinition>.list;
				if (definitions == null)
				{
					ModLog.Warn("[FullUnlock] ResourceList<HeroUpgradeDefinition>.list 为 null（资源表尚未初始化），本次跳过手抄本条目写入");
					definitions = new List<HeroUpgradeDefinition>();
				}
				foreach (HeroUpgradeDefinition definition in definitions)
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
						bool startable = typeEnum == HeroUpgradeTypeEnum.Item || typeEnum == HeroUpgradeTypeEnum.Trait || typeEnum == HeroUpgradeTypeEnum.Consumable;
						bool wantStarting = markStarting && startable;
						int targetLevel = Math.Max(0, definition.numLevels - 1);
						bool inInventory = false;
						if (inventoryUsable)
						{
							int currentLevel;
							bool isNew;
							bool currentStarting;
							bool known = GameCompat.InventoryGet(inventory, definition, out currentLevel, out isNew, out currentStarting);
							if (startable)
							{
								startableCount++;
								if (currentStarting)
								{
									startingCount++;
								}
							}
							// 已经满级且初始装备标记符合要求时不再重复写入，避免每次启动都重写整份手抄本
							bool satisfied = known && currentLevel >= targetLevel && (!wantStarting || currentStarting);
							if (!satisfied)
							{
								GameCompat.InventoryAdd(inventory, definition, targetLevel, wantStarting);
								changed++;
								known = GameCompat.InventoryGet(inventory, definition, out currentLevel, out isNew, out currentStarting);
							}
							inInventory = known;
						}
						// 只有开启“作为初始装备”时才改 canBeStartItem，默认保持原版数值
						if (wantStarting)
						{
							definition.upgradeType.canBeStartItem = true;
						}
						Assembly assembly = definition.GetType().Assembly;
						if (dumpList)
						{
							ModLog.Info(string.Format("[FullUnlock] + {0} | {1} | {2} | 手抄本已有={3} | 初始装备={4}", definition.name, typeEnum, assembly.GetName().Name, inInventory, wantStarting));
						}
						// 用 ReferenceEquals：Unity 的 .NET 2.0 Mono 里没有 Assembly.op_Equality
						bool isVanilla = object.ReferenceEquals(assembly, typeof(HeroUpgradeDefinition).Assembly);
						if (isVanilla)
						{
							if (inInventory)
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
						if (inInventory)
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
						Exception reason = ex.InnerException != null ? ex.InnerException : ex;
						ModLog.Warn("[FullUnlockPatch] 跳过异常定义 " + name + "：" + reason.Message);
					}
				}
				string difficultyText = veryHard ? "，极难难度已开启" : "";
				string startingText = markStarting ? "已标记为初始装备" : "未标记为初始装备，新开局仍按原版提供初始装备";
				ModLog.Info(string.Format("[FullUnlock] 手抄本已解锁（原版 {0} 项；{1}{2}；本次写入 {3} 项，只写用户存档 user）", vanilla, startingText, difficultyText, changed));
				if (!markStarting && !resetStarting && startableCount > 0 && startingCount > 0 && startingCount * 2 >= startableCount && !startingHintLogged)
				{
					startingHintLogged = true;
					ModLog.Warn(string.Format("[FullUnlock] 手抄本里有 {0}/{1} 项被标记为“初始装备”（通常来自 1.3.4 及更早版本）。如果不想让新开局塞满初始装备，把配置 Unlock/ResetStartingItems 设为 true 并重启一次游戏，本插件会重建手抄本条目。", startingCount, startableCount));
				}
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
				ModLog.Info(string.Format("[FullUnlock] 汇总：共 {0} 项已写入手抄本（原版 {1} 项 + mod {2} 项），另有 {3} 项由对应 mod 自行管理，无需任何额外操作", vanilla + modUnlocked, vanilla, modUnlocked, modBlocked));
				bool campaignEnabled = FullUnlockPlugin.UnlockCampaignItemPool != null && FullUnlockPlugin.UnlockCampaignItemPool.Value;
				if (campaignEnabled && GameCompat.CampaignSupported)
				{
					TickCampaignUnlock();
				}
				// 重建过手抄本才需要立刻落盘：把“写满的条目 + 已清除的初始装备标记”一起保存，
				// 然后再复位一次性开关（否则开关已改回 false，而存档还是旧的）
				if (resetApplied && inventoryUsable)
				{
					try
					{
						Profile.SaveUserSave();
						ModLog.Info("[FullUnlock] 重建后的手抄本已保存到用户存档");
						FullUnlockPlugin.ConsumeResetRequest();
					}
					catch (Exception saveEx)
					{
						ModLog.Warn("[FullUnlock] 保存重建后的手抄本失败，下次启动会重试：" + saveEx.Message);
					}
				}
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

		// 战役道具池（campaign.inventory）与手抄本（userSave.inventory）是两套数据。
		// 写入它会直接把道具放进玩家这一局的仓库，所以默认关闭，只在 Unlock/UnlockCampaignItemPool 开启时才补写。
		public static void TickCampaignUnlock()
		{
			bool unsupported = !GameCompat.CampaignSupported;
			if (unsupported)
			{
				return;
			}
			CampaignSave campaign = Profile.campaign;
			bool sameOrNone = campaign == null || object.ReferenceEquals(campaign, watchedCampaign);
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
				List<HeroUpgradeDefinition> definitions = ResourceList<HeroUpgradeDefinition>.list;
				if (definitions == null)
				{
					return;
				}
				foreach (HeroUpgradeDefinition definition in definitions)
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
