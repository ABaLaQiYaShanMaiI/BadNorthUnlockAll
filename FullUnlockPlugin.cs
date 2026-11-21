using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

namespace FullUnlockMod
{
	[BepInPlugin("com.shanmai.fullunlock", "Full Unlock Mod", "1.3.5")]
	public class FullUnlockPlugin : BaseUnityPlugin
	{
		// 是否把物品 / 特质标记为“初始装备”（旧行为）。
		// 默认关闭：只解锁手抄本，新开局仍按原版提供初始装备，战役奖励生成也保持原样。
		internal static ConfigEntry<bool> MarkAsStartingItems;

		// 是否把道具写进当前战役存档（campaign.inventory，相当于游戏内玩家仓库 / 可购买的升级池）。
		// 默认关闭：避免玩家一开新档就发现自己仓库里被塞满了全部道具。
		internal static ConfigEntry<bool> UnlockCampaignItemPool;

		// 一次性开关：重建手抄本条目，清掉旧版本（≤1.3.4）写入的“初始装备”标记。
		// 注意会同时清掉玩家在游戏内正常解锁的初始装备；执行后会自动改回 false。
		internal static ConfigEntry<bool> ResetStartingItems;

		// 兼容预设：Bad North Multiplayer（com.bananasplit.badnorthmp）的指挥官选装列表直接读取
		// MetaInventory.startingItems / startingTraits，所以检测到它时仍然把全部物品/特质标记为初始装备，
		// 否则多人局的选装列表会只剩原版初始装备。单人环境不受影响。
		internal static ConfigEntry<bool> AutoStartItemsForMultiplayer;

		private const string MultiplayerGuid = "com.bananasplit.badnorthmp";

		private static bool multiplayerChecked;

		private static bool multiplayerDetected;

		// 本次运行实际是否标记初始装备 = 玩家显式开启 || (兼容预设已开启 && 检测到 Bad North Multiplayer)
		internal static bool EffectiveMarkAsStartingItems
		{
			get
			{
				bool explicitOn = MarkAsStartingItems != null && MarkAsStartingItems.Value;
				if (explicitOn)
				{
					return true;
				}
				bool presetEnabled = AutoStartItemsForMultiplayer != null && AutoStartItemsForMultiplayer.Value;
				EnsureMultiplayerChecked(presetEnabled);
				return presetEnabled && multiplayerDetected;
			}
		}

		// 按需检测一次 Bad North Multiplayer，并给出一条与预设状态对应的提示
		private static void EnsureMultiplayerChecked(bool presetEnabled)
		{
			if (multiplayerChecked)
			{
				return;
			}
			multiplayerChecked = true;
			multiplayerDetected = IsPluginLoaded(MultiplayerGuid);
			if (!multiplayerDetected)
			{
				return;
			}
			if (presetEnabled)
			{
				ModLog.Info("[FullUnlock] 检测到 Bad North Multiplayer：已启用初始装备兼容（Unlock/AutoStartItemsForMultiplayer=true），多人指挥官选装列表可使用全部物品 / 特质；单人新开局的初始装备也会变成全部。");
			}
			else
			{
				ModLog.Info("[FullUnlock] 检测到 Bad North Multiplayer：它的指挥官选装列表读取手抄本的初始装备（startingItems / startingTraits）。本次按默认“只解锁手抄本”处理；如果希望多人局能选全部装备，把配置 Unlock/AutoStartItemsForMultiplayer 设为 true（同时单人新开局的初始装备也会变成全部）。");
			}
		}

		// Relics & Realms 用 PlayerPrefs 记录解锁，需在插件启动时单独处理
		internal static ConfigEntry<bool> UnlockRelics;

		internal static ConfigEntry<bool> UnlockRealms;

		// 诊断为 true 时逐条打印解锁了哪些定义，便于核对
		internal static ConfigEntry<bool> DumpUnlocked;

		private static ConfigFile configFile;

		// 游戏 API 自检通过并成功打上 Harmony 补丁后才为 true
		private bool active;

		private void Awake()
		{
			ModLog.Source = base.Logger;
			ModLog.Info("Full Unlock Mod is loading...");
			configFile = base.Config;
			MarkAsStartingItems = base.Config.Bind<bool>("Unlock", "MarkAsStartingItems", false, "把全部物品 / 特质标记为“初始装备”：开启后新开局可以从全部装备里挑选，并且战役奖励生成会相应变化。默认关闭，只解锁手抄本。");
			UnlockCampaignItemPool = base.Config.Bind<bool>("Unlock", "UnlockCampaignItemPool", false, "把全部道具写进当前战役存档（campaign.inventory）。开启后新开一局就能直接购买 / 升级全部道具，但道具会出现在玩家的战役仓库里。默认关闭。");
			ResetStartingItems = base.Config.Bind<bool>("Unlock", "ResetStartingItems", false, "一次性修复开关：重建手抄本条目，清掉旧版本（1.3.4 及更早）写入的“初始装备”标记。注意会一并清掉你自己正常解锁的初始装备；执行后本项会自动改回 false。");
			AutoStartItemsForMultiplayer = base.Config.Bind<bool>("Unlock", "AutoStartItemsForMultiplayer", false, "兼容预设：开启后，检测到 Bad North Multiplayer（com.bananasplit.badnorthmp）时把全部物品 / 特质标记为初始装备 —— 该 mod 的指挥官选装列表直接读取手抄本的初始装备。默认关闭（只解锁手抄本）；开启后单人新开局的初始装备也会变成全部。");
			UnlockRelics = base.Config.Bind<bool>("Relics & Realms", "UnlockAllRelics", true, "解锁 Relics & Realms 全部宝石（该 mod 不走原版手抄本体系，改用 PlayerPrefs 存解锁状态）。");
			UnlockRealms = base.Config.Bind<bool>("Relics & Realms", "UnlockAllRealms", true, "解锁 Relics & Realms 全部境界（I-XII）。");
			DumpUnlocked = base.Config.Bind<bool>("Diagnostics", "DumpUnlockedList", false, "逐条打印本次解锁的定义（名称/类型/来源程序集），仅排查问题时开启。");

			// 先自检再打补丁：任何被我们写死引用的游戏类型缺失时，直接不挂补丁，
			// 否则 Mono 在 JIT 编译解锁方法时会抛 TypeLoadException（try/catch 兜不住）。
			GameCompat.Probe();
			if (!GameCompat.CoreSupported)
			{
				ModLog.Error("当前游戏的 Assembly-CSharp 缺少本插件需要的类型 / 成员：" + GameCompat.CoreMissing + "。已跳过全部解锁，不会影响游戏。常见原因：游戏版本与插件不匹配，或 Assembly-CSharp.dll 被整合包 / 汉化 / 破解替换过。");
				return;
			}
			if (!GameCompat.InventorySupported)
			{
				ModLog.Warn("手抄本存档结构不可用（" + GameCompat.InventoryProblem + "），本插件将跳过手抄本解锁，其余功能不受影响。");
			}
			if (!GameCompat.CampaignSupported)
			{
				ModLog.Info("战役道具池功能不可用（" + GameCompat.CampaignProblem + "），不影响手抄本解锁。");
			}
			try
			{
				new Harmony("com.shanmai.fullunlock").PatchAll();
			}
			catch (Exception ex)
			{
				Exception reason = ex.InnerException != null ? ex.InnerException : ex;
				ModLog.Error("挂载 Harmony 补丁失败，本插件不会生效（不影响游戏）：" + reason.Message);
				return;
			}
			active = true;
			if (UnlockRelics.Value)
			{
				base.StartCoroutine(RelicUnlockPatch.ApplyRoutine(UnlockRealms.Value));
			}
			ModLog.Info("Full Unlock Mod has been patched!");
		}

		// 战役对象比插件晚出现，这里只做一次引用比较，变化的帧自动补解锁。
		// 默认不开启（UnlockCampaignItemPool=false），此时完全不碰战役存档。
		private void Update()
		{
			bool enabled = active && UnlockCampaignItemPool != null && UnlockCampaignItemPool.Value;
			bool supported = GameCompat.CampaignSupported;
			if (!enabled || !supported)
			{
				return;
			}
			FullUnlockPatch.TickCampaignUnlock();
		}

		// 按 GUID 检测插件是否已加载（只读 BepInEx 的插件表，不产生对目标 mod 程序集的硬依赖）
		private static bool IsPluginLoaded(string guid)
		{
			try
			{
				foreach (PluginInfo pluginInfo in Chainloader.PluginInfos.Values)
				{
					bool usable = pluginInfo != null && pluginInfo.Metadata != null;
					if (!usable)
					{
						continue;
					}
					if (pluginInfo.Metadata.GUID == guid)
					{
						return true;
					}
				}
			}
			catch (Exception ex)
			{
				ModLog.Warn("[FullUnlockPlugin] 读取已加载插件列表失败：" + ex.Message);
			}
			return false;
		}

		// ResetStartingItems 是一次性开关：用掉后写回 false，避免每次启动都重建整份手抄本
		internal static void ConsumeResetRequest()
		{
			if (ResetStartingItems == null)
			{
				return;
			}
			try
			{
				ResetStartingItems.Value = false;
				if (configFile != null)
				{
					configFile.Save();
				}
			}
			catch (Exception ex)
			{
				ModLog.Warn("[FullUnlockPlugin] 复位 ResetStartingItems 失败：" + ex.Message);
			}
		}
	}
}
