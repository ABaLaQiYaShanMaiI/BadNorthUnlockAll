using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace FullUnlockMod
{
	// Token: 0x02000002 RID: 2
	[BepInPlugin("com.shanmai.fullunlock", "Full Unlock Mod", "1.2.0")]
	public class FullUnlockPlugin : BaseUnityPlugin
	{
		// Relics & Realms 用 PlayerPrefs 记录宝石解锁，需要在插件启动时单独处理
		internal static ConfigEntry<bool> UnlockRelics;

		internal static ConfigEntry<bool> UnlockRealms;

		// Token: 0x06000001 RID: 1 RVA: 0x00002050 File Offset: 0x00000250
		private void Awake()
		{
			base.Logger.LogInfo("Full Unlock Mod is loading...");
			FullUnlockPlugin.UnlockRelics = base.Config.Bind<bool>("Relics & Realms", "UnlockAllRelics", true, "解锁 Relics & Realms 全部宝石（该 mod 不走原版手抄本体系，改用 PlayerPrefs 存解锁状态）。");
			FullUnlockPlugin.UnlockRealms = base.Config.Bind<bool>("Relics & Realms", "UnlockAllRealms", true, "解锁 Relics & Realms 全部境界（I-XII）。");
			Harmony harmony = new Harmony("com.shanmai.fullunlock");
			harmony.PatchAll();
			bool flag = FullUnlockPlugin.UnlockRelics.Value;
			if (flag)
			{
				base.StartCoroutine(RelicUnlockPatch.ApplyRoutine(FullUnlockPlugin.UnlockRealms.Value));
			}
			base.Logger.LogInfo("Full Unlock Mod has been patched!");
		}
	}
}

