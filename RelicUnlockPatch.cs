using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace FullUnlockMod
{
	// Relics & Realms（GUID: com.gmw.relicselection）不走手抄本体系，改用 PlayerPrefs 存解锁状态：
	// 宝石 = RelicDef.unlockKey（4.0.0 即 "Relic_Unlocked_<名字>"，值为 1 表示已解锁），
	// 境界 = "RealmUnlocked"（1~12，取自 RomanNumerals.Length）。
	// 该 mod 每帧用 IsUnlocked 重算界面签名，所以运行期写入立即生效。
	public static class RelicUnlockPatch
	{
		private const string RealmKey = "RealmUnlocked";

		private const string RelicKeyPrefix = "Relic_Unlocked_";

		private const int DefaultMaxRealm = 12;

		private const string RelicPluginTypeName = "RelicSelectionMod.RelicPlugin";

		// 4.0.0 的 20 个 RelicDef.unlockKey 后缀，作为反射拿不到定义表时的兜底
		private static readonly string[] KnownRelicNames = new string[]
		{
			"Ruby", "Sapphire", "Emerald", "Topaz", "Aquamarine",
			"Amethyst", "Onyx", "Opal", "Garnet", "Citrine",
			"Amber", "Tourmaline", "Pyrite", "Moonstone",
			"Obsidian1", "Obsidian2", "Obsidian3", "Obsidian4",
			"Diamond", "Bloodstone"
		};

		private static bool reflectionWarned;

		// BepInEx 会在插件 Awake 之前加载好全部插件程序集，所以可跨加载顺序找到该类型
		private static Type FindRelicPluginType()
		{
			try
			{
				Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < assemblies.Length; i++)
				{
					Assembly assembly = assemblies[i];
					bool invalid = (object)assembly == null;
					if (invalid)
					{
						continue;
					}
					Type type = assembly.GetType(RelicPluginTypeName, false);
					bool found = (object)type != null;
					if (found)
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

		// 反射读取 mod 的 RelicDef[] defs，取出每个 unlockKey，兼容后续版本新增宝石
		private static List<string> GetModUnlockKeys(Type pluginType)
		{
			List<string> keys = new List<string>();
			bool noType = (object)pluginType == null;
			if (noType)
			{
				return keys;
			}
			try
			{
				FieldInfo defsField = pluginType.GetField("defs", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				bool noDefsField = (object)defsField == null;
				if (noDefsField)
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
					bool noKeyField = (object)keyField == null;
					if (noKeyField)
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

		// 反射 RomanNumerals 长度得到当前版本境界数（4.0.0 为 12）
		private static int GetMaxRealm(Type pluginType)
		{
			bool hasType = (object)pluginType != null;
			if (hasType)
			{
				try
				{
					FieldInfo field = pluginType.GetField("RomanNumerals", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
					bool hasField = (object)field != null;
					if (hasField)
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

		// 写入全部宝石与境界；返回反射到的宝石定义数，-1 表示未检测到 Relics & Realms
		public static int Apply(bool unlockRealms)
		{
			Type pluginType = FindRelicPluginType();
			bool noType = (object)pluginType == null;
			if (noType)
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
			string realmText = unlockRealms ? ("，境界 I-" + maxRealm + " 已解锁") : "";
			Debug.Log(string.Format("[FullUnlock] Relics & Realms 已解锁（宝石 {0} 颗{1}；本次新写入 {2} 个键，反射到 {3} 个定义）", new object[]
			{
				keys.Count,
				realmText,
				changed,
				reflected
			}));
			return reflected;
		}

		// 定义表可能尚未初始化：先按已知列表兜底写入，再轮询到能反射到它自己的定义表为止
		public static IEnumerator ApplyRoutine(bool unlockRealms)
		{
			for (int attempt = 0; attempt < 10; attempt++)
			{
				int reflected;
				try
				{
					reflected = Apply(unlockRealms);
				}
				catch (Exception ex)
				{
					Debug.LogError("[RelicUnlockPatch] 写入宝石解锁状态失败：" + ((ex != null) ? ex.ToString() : null));
					yield break;
				}
				if (reflected <= 0)
				{
					if (reflected < 0)
					{
						yield break;
					}
					yield return new WaitForSeconds(3f);
					continue;
				}
				yield break;
			}
		}
	}
}
