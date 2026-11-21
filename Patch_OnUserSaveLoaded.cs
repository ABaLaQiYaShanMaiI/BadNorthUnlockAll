using System;
using HarmonyLib;
using Voxels.TowerDefense;

namespace FullUnlockMod
{
	[HarmonyPatch(typeof(Profile), "OnUserSaveLoaded")]
	public static class Patch_OnUserSaveLoaded
	{
		// 注意：try/catch 只能兜住“运行期”异常。
		// 如果本调用链上的某个游戏类型在玩家那份 Assembly-CSharp 里不存在，Mono 会在 JIT 编译阶段抛
		// TypeLoadException，catch 是拦不住的（会冒到 BasePlatformManager.Update 记成 Next Update Exception）。
		// 所以真正的防护在别处：FullUnlockPlugin.Awake 里的 GameCompat 自检 + 手抄本(MetaInventory)全部走反射。
		private static void Postfix()
		{
			try
			{
				ModLog.Info("[FullUnlockPatch] OnUserSaveLoaded 触发，开始解锁手抄本");
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
