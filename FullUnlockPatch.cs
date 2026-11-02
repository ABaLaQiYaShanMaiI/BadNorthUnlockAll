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
						HeroUpgradeDefinition[] array = Resources.LoadAll<HeroUpgradeDefinition>("");
						bool flag4 = array != null;
						if (flag4)
						{
							foreach (HeroUpgradeDefinition heroUpgradeDefinition2 in array)
							{
								bool flag5 = heroUpgradeDefinition2 != null && heroUpgradeDefinition2.upgradeType != null;
								if (flag5)
								{
									heroUpgradeDefinition2.upgradeType.canBeStartItem = true;
								}
							}
						}
						Debug.Log("[FullUnlockPatch] 手抄本已解锁，所有可作为初始装备的类型已开启！");
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
	}
}
