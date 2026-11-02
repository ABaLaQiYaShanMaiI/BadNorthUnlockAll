using System;
using BepInEx;
using HarmonyLib;

namespace FullUnlockMod
{
	// Token: 0x02000002 RID: 2
	[BepInPlugin("com.shanmai.fullunlock", "Full Unlock Mod", "1.1.0")]
	public class FullUnlockPlugin : BaseUnityPlugin
	{
		// Token: 0x06000001 RID: 1 RVA: 0x00002050 File Offset: 0x00000250
		private void Awake()
		{
			base.Logger.LogInfo("Full Unlock Mod is loading...");
			Harmony harmony = new Harmony("com.shanmai.fullunlock");
			harmony.PatchAll();
			base.Logger.LogInfo("Full Unlock Mod has been patched!");
		}
	}
}
