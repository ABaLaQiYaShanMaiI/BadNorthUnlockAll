using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace FullUnlockMod
{
	// Relics & Realms（BepInEx GUID: com.gmw.relicselection）的解锁方式与原版手抄本完全不同：
	// 它不使用 MetaInventory / HeroUpgradeDefinition，而是把每颗宝石（Relic）的解锁状态
	// 直接写进 Unity PlayerPrefs：
	//   * 宝石：key = RelicDef.unlockKey（4.0.0 中为 "Relic_Unlocked_<名字>"），值为 1 表示已解锁，
	//     判定处为 IsUnlocked(def) => PlayerPrefs.GetInt(def.unlockKey, 0) == 1；
	//   * 境界：key = "RealmUnlocked"，取值 1~12（RomanNumerals.Length），由 GetUnlockedRealm() 读取。
	// 该 mod 自带的调试键 F9 也只是把这两类键写成全解锁（RelicPlugin.Update，默认 EnableDebugKeys=false）。
	// 它每帧用 IsUnlocked 重算签名刷新界面，所以运行期写入 PlayerPrefs 会立刻生效，无需重启游戏。
	public static class RelicUnlockPatch
	{
		private const string RealmKey = "RealmUnlocked";

		private const string RelicKeyPrefix = "Relic_Unlocked_";

		private const int DefaultMaxRealm = 12;

		private const string RelicPluginTypeName = "RelicSelectionMod.RelicPlugin";

		// Relics & Realms 4.0.0 的 20 个 RelicDef.unlockKey 后缀。
		// 用于插件加载顺序导致 mod 静态定义表（defs）尚未初始化时的兜底。
		private static readonly string[] KnownRelicNames = new string[]
		{
			"Ruby",
			"Sapphire",
			"Emerald",
			"Topaz",
			"Aquamarine",
			"Amethyst",
			"Onyx",
			"Opal",
			"Garnet",
			"Citrine",
			"Amber",
			"Tourmaline",
			"Pyrite",
			"Moonstone",
			"Obsidian1",
			"Obsidian2",
			"Obsidian3",
			"Obsidian4",
			"Diamond",
			"Bloodstone"
		};

		private static bool reflectionWarned;

		public static bool IsRelicModLoaded()
		{
			return FindRelicPluginType() != null;
		}

		// 在所有已加载程序集中查找 Relics & Realms 的插件类型。
		// BepInEx 会在调用各插件 Awake 之前就把插件程序集全部加载好，因此这里可以跨插件顺序找到它。
		private static Type FindRelicPluginType()
		{
			try
			{
				Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < assemblies.Length; i++)
				{
					Assembly assembly = assemblies[i];
					if (assembly == null)
					{
						continue;
					}
					Type type = assembly.GetType(RelicPluginTypeName, false);
					if (type != null)
					{
						return type;
					}
				}
			}
			catch (Exception ex)
			{
				if (!reflectionWarned)
				{
					reflectionWarned = true;
					Debug.LogWarning("[RelicUnlockPatch] 查找 Relics & Realms 程序集失败：" + ex.Message);
				}
			}
			return null;
		}

		// 反射读取 mod 自己的 RelicDef[] defs，取出每个定义的 unlockKey。
		// 这样即使将来版本新增宝石（键名变化），也无需改本插件。
		private static List<string> GetModUnlockKeys(Type pluginType)
		{
			List<string> keys = new List<string>();
			if (pluginType == null)
			{
				return keys;
			}
			try
			{
				FieldInfo defsField = pluginType.GetField("defs", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if (defsField == null)
				{
					return keys;
				}
				Array defs = defsField.GetValue(null) as Array;
				if (defs == null)
				{
					return keys;
				}
				for (int i = 0; i < defs.Length; i++)
				{
					object def = defs.GetValue(i);
					if (def == null)
					{
						continue;
					}
					FieldInfo keyField = def.GetType().GetField("unlockKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (keyField == null)
					{
						continue;
					}
					string key = keyField.GetValue(def) as string;
					if (!string.IsNullOrEmpty(key) && !keys.Contains(key))
					{
						keys.Add(key);
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[RelicUnlockPatch] 读取 Relics & Realms 宝石定义失败：" + ex.Message);
			}
			return keys;
		}

		// 反射读取 RomanNumerals 的长度，得到当前版本的最大境界数（4.0.0 为 12）。
		private static int GetMaxRealm(Type pluginType)
		{
			if (pluginType != null)
			{
				try
				{
					FieldInfo field = pluginType.GetField("RomanNumerals", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
					if (field != null)
					{
						Array romanNumerals = field.GetValue(null) as Array;
						if (romanNumerals != null && romanNumerals.Length > 0)
						{
							return romanNumerals.Length;
						}
					}
				}
				catch (Exception ex)
				{
					Debug.LogWarning("[RelicUnlockPatch] 读取境界数量失败：" + ex.Message);
				}
			}
			return DefaultMaxRealm;
		}

		// 把全部宝石与境界写入 PlayerPrefs。
		// 返回值：反射到的宝石定义数量；-1 表示未检测到 Relics & Realms（未做任何修改）。
		public static int Apply(bool unlockRealms)
		{
			Type pluginType = FindRelicPluginType();
			if (pluginType == null)
			{
				return -1;
			}
			List<string> keys = GetModUnlockKeys(pluginType);
			int reflected = keys.Count;
			for (int i = 0; i < KnownRelicNames.Length; i++)
			{
				string knownKey = RelicKeyPrefix + KnownRelicNames[i];
				if (!keys.Contains(knownKey))
				{
					keys.Add(knownKey);
				}
			}
			int changed = 0;
			for (int j = 0; j < keys.Count; j++)
			{
				if (PlayerPrefs.GetInt(keys[j], 0) != 1)
				{
					PlayerPrefs.SetInt(keys[j], 1);
					changed++;
				}
			}
			int maxRealm = GetMaxRealm(pluginType);
			bool realmChanged = false;
			if (unlockRealms)
			{
				if (PlayerPrefs.GetInt(RealmKey, 1) < maxRealm)
				{
					PlayerPrefs.SetInt(RealmKey, maxRealm);
					realmChanged = true;
				}
			}
			if (changed > 0 || realmChanged)
			{
				PlayerPrefs.Save();
			}
			string realmText = realmChanged ? ("；境界已全部解锁（I-" + maxRealm + "）") : "";
			Debug.Log(string.Format("[RelicUnlockPatch] Relics & Realms 宝石已解锁：共 {0} 个键，本次新写入 {1} 个，反射到 {2} 个定义{3}", new object[]
			{
				keys.Count,
				changed,
				reflected,
				realmText
			}));
			return reflected;
		}

		// BepInEx 不保证插件 Awake 的先后顺序，Relics & Realms 的静态定义表（defs）可能还没建好，
		// 因此先按已知列表兜底写入，再在几秒内轮询，直到能反射到它自己的定义表为止。
		public static IEnumerator ApplyRoutine(bool unlockRealms)
		{
			for (int attempt = 0; attempt < 10; attempt++)
			{
				int reflected = Apply(unlockRealms);
				if (reflected < 0)
				{
					// 没装 Relics & Realms，什么都不做
					yield break;
				}
				if (reflected > 0)
				{
					// 已用 mod 自己的定义表校准过，全部宝石键都已覆盖
					yield break;
				}
				yield return new WaitForSeconds(3f);
			}
		}
	}
}
