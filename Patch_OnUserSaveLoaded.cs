using System;
using HarmonyLib;
using Voxels.TowerDefense;

namespace FullUnlockMod
{
	[HarmonyPatch(typeof(Profile), "OnUserSaveLoaded")]
	public static class Patch_OnUserSaveLoaded
	{
		private static void Postfix()
		{
			// 整体兜底：任何异常都不能冒进游戏的主循环（否则会被记成 Next Update Exception）
			try
			{
				ModLog.Info("[FullUnlockPatch] OnUserSaveLoaded Postfix triggered");
				FullUnlockPatch.ApplyFullUnlock();
				bool relicEnabled = FullUnlockPlugin.UnlockRelics != null && FullUnlockPlugin.UnlockRelics.Value;
				if (relicEnabled)
				{
					bool unlockRealms = FullUnlockPlugin.UnlockRealms != null && FullUnlockPlugin.UnlockRealms.Value;
					RelicUnlockPatch.Apply(unlockRealms);
				}
			}
			catch (Exception ex)
			{
				ModLog.Error("[FullUnlockPatch] Postfix 异常（已拦截，不影响游戏）：" + ((ex != null) ? ex.ToString() : null));
			}
		}
	}
}
