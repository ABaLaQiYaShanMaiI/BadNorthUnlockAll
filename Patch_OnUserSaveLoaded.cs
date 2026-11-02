using System;
using HarmonyLib;
using UnityEngine;
using Voxels.TowerDefense;

namespace FullUnlockMod
{
	// Token: 0x02000003 RID: 3
	[HarmonyPatch(typeof(Profile), "OnUserSaveLoaded")]
	public static class Patch_OnUserSaveLoaded
	{
		// Token: 0x06000003 RID: 3 RVA: 0x0000209B File Offset: 0x0000029B
		private static void Postfix()
		{
			Debug.Log("[FullUnlockPatch] OnUserSaveLoaded Postfix triggered");
			FullUnlockPatch.ApplyFullUnlock();
			FullUnlockPatch.UnlockVeryHardDifficulty();
		}
	}
}
