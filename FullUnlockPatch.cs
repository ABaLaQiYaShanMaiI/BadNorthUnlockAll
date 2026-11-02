using System;
using System.Collections.Generic;
using UnityEngine;
using Voxels.TowerDefense;
using Voxels.TowerDefense.ProfileInternals;

namespace FullUnlockMod
{
	// Token: 0x02000004 RID: 4
	public static class FullUnlockPatch
	{
		// Token: 0x06000004 RID: 4 RVA: 0x000020B8 File Offset: 0x000002B8
		public static void ApplyFullUnlock()
		{
			Debug.Log("[FullUnlockPatch] ApplyFullUnlock start");
			try
			{
				UserSave userSave = Profile.userSave;
				bool flag = userSave == null;
				if (flag)
				{
					Debug.Log("[FullUnlockPatch] userSave == null，提前返回");
				}
				else
				{
					MetaInventory inventory = userSave.inventory;
					bool flag2 = inventory == null;
					if (flag2)
					{
						Debug.Log("[FullUnlockPatch] userSave.inventory == null，提前返回");
					}
					else
					{
						List<HeroUpgradeTypeEnum> list = new List<HeroUpgradeTypeEnum>
						{
							HeroUpgradeTypeEnum.Item,
							HeroUpgradeTypeEnum.Trait,
							HeroUpgradeTypeEnum.Consumable
						};
						foreach (HeroUpgradeDefinition heroUpgradeDefinition in ResourceList<HeroUpgradeDefinition>.list)
						{
							bool flag3 = heroUpgradeDefinition == null;
							if (!flag3)
							{
								int level = Math.Max(0, heroUpgradeDefinition.numLevels - 1);
								bool isStarting = list.Contains(heroUpgradeDefinition.typeEnum);
								inventory.Add(heroUpgradeDefinition, level, isStarting);
							}
						}
						// 用 ResourceList 而不是 Resources.LoadAll：mod 通过 ScriptableObject.CreateInstance 注入的
						// 定义不在 Resources 里（例如 Bad North MP 的自定义特质），但它们同样带 upgradeType，
						// 需要一起置位，否则 InfoPanel 里会显示为不可作为初始装备。
						foreach (HeroUpgradeDefinition heroUpgradeDefinition2 in ResourceList<HeroUpgradeDefinition>.list)
						{
							bool flag5 = heroUpgradeDefinition2 != null && heroUpgradeDefinition2.upgradeType != null;
							if (flag5)
							{
								heroUpgradeDefinition2.upgradeType.canBeStartItem = true;
							}
						}
						Debug.Log("[FullUnlockPatch] 手抄本已解锁，所有可作为初始装备的类型已开启！");
						FullUnlockPatch.TickCampaignUnlock();
					}
				}
			}
			catch (Exception ex)
			{
				string str = "[FullUnlockPatch] 发生错误：";
				Exception ex2 = ex;
				Debug.LogError(str + ((ex2 != null) ? ex2.ToString() : null));
			}
		}

		// Token: 0x06000005 RID: 5 RVA: 0x00002270 File Offset: 0x00000470
		public static void UnlockVeryHardDifficulty()
		{
			Debug.Log("[FullUnlockPatch] UnlockVeryHardDifficulty start");
			try
			{
				UserSave userSave = Profile.userSave;
				bool flag = userSave == null;
				if (flag)
				{
					Debug.Log("[FullUnlockPatch] userSave == null，提前返回");
				}
				else
				{
					bool flag2 = userSave.maxDifficulty == Difficulty.VeryHard;
					if (flag2)
					{
						Debug.Log("[FullUnlockPatch] 已经是 VeryHard，无需重复解锁");
					}
					else
					{
						userSave.maxDifficulty = Difficulty.VeryHard;
						userSave.campaignPrefs.difficulty = Difficulty.VeryHard;
						Debug.Log("[FullUnlockPatch] 极难难度已解锁！");
						Profile.SaveUserSave();
					}
				}
			}
			catch (Exception ex)
			{
				string str = "[FullUnlockPatch] 解锁极难难度时发生错误：";
				Exception ex2 = ex;
				Debug.LogError(str + ((ex2 != null) ? ex2.ToString() : null));
			}
		}

		// 战役内的"可购买池"是 CampaignSave.inventory（List<SerializableHeroUpgrade>），
		// 与手抄本 userSave.inventory（MetaInventory）是两套独立数据：前者由作战升级界面
		// （SuperUpgradesCampaignProxy）读取，后者由新游戏选人界面读取。
		// 游戏自带的控制台命令 Inventory.AddAll 会把道具同时写进这两边，这里照做，
		// 这样进行中的战役也无需重开就能拿到全部道具。
		private static CampaignSave watchedCampaign;

		// 由 FullUnlockPlugin.Update 每帧调用，开销只是一次静态字段读取 + 引用比较；
		// 当战役对象变化（新建 / 读档 / 切换）时自动补一次解锁。
		public static void TickCampaignUnlock()
		{
			CampaignSave campaign = Profile.campaign;
			bool flag = campaign == null || campaign == FullUnlockPatch.watchedCampaign;
			if (!flag)
			{
				FullUnlockPatch.watchedCampaign = campaign;
				FullUnlockPatch.ApplyCampaignUnlock(campaign);
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
					bool flag4 = FullUnlockPatch.CampaignHas(campaign, heroUpgradeDefinition);
					if (!flag4)
					{
						campaign.inventory.Add(heroUpgradeDefinition);
						num++;
					}
				}
				bool flag5 = num > 0;
				if (flag5)
				{
					Debug.Log(string.Format("[FullUnlockPatch] 当前战役已解锁全部道具：新增 {0} 项，可直接购买/升级。", num));
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
	}
}
