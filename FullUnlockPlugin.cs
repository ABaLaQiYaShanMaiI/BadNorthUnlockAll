using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace FullUnlockMod
{
	[BepInPlugin("com.shanmai.fullunlock", "Full Unlock Mod", "1.3.2")]
	public class FullUnlockPlugin : BaseUnityPlugin
	{
		// Relics & Realms 用 PlayerPrefs 记录解锁，需在插件启动时单独处理
		internal static ConfigEntry<bool> UnlockRelics;

		internal static ConfigEntry<bool> UnlockRealms;

		private void Awake()
		{
			base.Logger.LogInfo("Full Unlock Mod is loading...");
			UnlockRelics = base.Config.Bind<bool>("Relics & Realms", "UnlockAllRelics", true, "解锁 Relics & Realms 全部宝石（该 mod 不走原版手抄本体系，改用 PlayerPrefs 存解锁状态）。");
			UnlockRealms = base.Config.Bind<bool>("Relics & Realms", "UnlockAllRealms", true, "解锁 Relics & Realms 全部境界（I-XII）。");
			new Harmony("com.shanmai.fullunlock").PatchAll();
			if (UnlockRelics.Value)
			{
				base.StartCoroutine(RelicUnlockPatch.ApplyRoutine(UnlockRealms.Value));
			}
			base.Logger.LogInfo("Full Unlock Mod has been patched!");
		}

		// 战役对象比插件晚出现，这里只做一次引用比较，变化的帧自动补解锁
		private void Update()
		{
			FullUnlockPatch.TickCampaignUnlock();
		}
	}
}


