using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;

namespace FullUnlockMod
{
	[HarmonyPatch(typeof(Profile), "OnUserSaveLoaded")]
	public static class Patch_OnUserSaveLoaded
	{
		private static void Postfix()
		{
			Debug.Log("[FullUnlockPatch] OnUserSaveLoaded Postfix triggered");
			FullUnlockPatch.ApplyFullUnlock();
			bool relicEnabled = FullUnlockPlugin.UnlockRelics != null && FullUnlockPlugin.UnlockRelics.Value;
			if (relicEnabled)
			{
				bool unlockRealms = FullUnlockPlugin.UnlockRealms != null && FullUnlockPlugin.UnlockRealms.Value;
				RelicUnlockPatch.Apply(unlockRealms);
			}
		}
	}
}
