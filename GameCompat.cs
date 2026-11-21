using System;
using System.Collections.Generic;
using System.Reflection;

namespace FullUnlockMod
{
	// 游戏 API 探测 + 反射桥。
	//
	// 为什么需要它：本插件的方法在 Mono 首次 JIT 编译时，会解析它引用到的全部游戏类型/成员；
	// 只要玩家的 Assembly-CSharp.dll 里少一个类型或成员（游戏版本不同 / 被整合包、汉化、破解替换过），
	// JIT 就抛 TypeLoadException / MissingMethodException，且这类异常不会被方法体里的 try/catch 捕获，
	// 最终冒到 BasePlatformManager.Update 变成 "[BASE] Next Update Exception"（玩家反馈的那条报错）。
	// 所以这里：
	//   1) 启动时只用反射（按字符串找类型/成员）确认依赖是否齐全，缺关键类型就不打补丁；
	//   2) 手抄本存档结构（MetaInventory）完全用反射读写 —— 该类型在不同构建里可能缺失或改名，
	//      不做编译期引用，缺它时自动降级，而不是把游戏拖崩；
	//   3) 所有 Type / FieldInfo / MethodInfo / ParameterInfo / Assembly 的比较一律用
	//      object.ReferenceEquals 或 (object)x == null —— Unity 的 .NET 2.0 Mono 没有这些类型的 op_Equality，
	//      用 == 会在 JIT 时抛 MissingMethodException（v1.3.3 踩过的坑）。
	//
	// 注意：本文件刻意不引用任何游戏类型（连 using 都没有），保证它自己永远能被 JIT 编译。
	internal static class GameCompat
	{
		private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

		private const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		internal static bool Probed;

		// 手抄本解锁主流程（含极难难度）依赖的类型/成员是否齐全
		internal static bool CoreSupported;

		// userSave.inventory（手抄本）能否读写
		internal static bool InventorySupported;

		// campaign.inventory（战役道具池）能否读写
		internal static bool CampaignSupported;

		internal static string CoreMissing = string.Empty;

		internal static string InventoryProblem = string.Empty;

		internal static string CampaignProblem = string.Empty;

		private static Type heroUpgradeDefinitionType;

		private static FieldInfo userInventoryField;

		private static MethodInfo inventoryAdd;

		private static MethodInfo inventoryGet;

		private static Type boundInventoryType;
		internal static void Probe()
		{
			if (Probed)
			{
				return;
			}
			Probed = true;
			List<string> missing = new List<string>();
			Type profileType = RequireType("Voxels.TowerDefense.Profile", missing);
			Type userSaveType = RequireType("Voxels.TowerDefense.ProfileInternals.UserSave", missing);
			heroUpgradeDefinitionType = RequireType("Voxels.TowerDefense.HeroUpgradeDefinition", missing);
			Type heroUpgradeType = RequireType("Voxels.TowerDefense.HeroUpgradeType", missing);
			RequireType("Voxels.TowerDefense.HeroUpgradeTypeEnum", missing);
			Type resourceListType = RequireType("ResourceList`1", missing);
			Type difficultyType = RequireType("Voxels.TowerDefense.Difficulty", missing);
			Type campaignPrefsType = RequireType("Voxels.TowerDefense.CampaignPrefs", missing);

			// 我们代码里会直接访问的成员，逐个核对“存在 + 类型一致”，类型写错同样会在 JIT 阶段炸
			AddProblem(missing, CheckField(profileType, "Profile.userSave", "userSave", true, userSaveType));
			AddProblem(missing, CheckMethod(profileType, "Profile.SaveUserSave", "SaveUserSave", true, 0));
			AddProblem(missing, CheckMethod(profileType, "Profile.OnUserSaveLoaded", "OnUserSaveLoaded", true, 1));
			AddProblem(missing, CheckField(userSaveType, "UserSave.maxDifficulty", "maxDifficulty", false, difficultyType));
			AddProblem(missing, CheckField(userSaveType, "UserSave.campaignPrefs", "campaignPrefs", false, campaignPrefsType));
			AddProblem(missing, CheckField(campaignPrefsType, "CampaignPrefs.difficulty", "difficulty", false, difficultyType));
			AddProblem(missing, CheckField(heroUpgradeDefinitionType, "HeroUpgradeDefinition.upgradeType", "upgradeType", false, heroUpgradeType));
			AddProblem(missing, CheckProperty(heroUpgradeDefinitionType, "HeroUpgradeDefinition.numLevels", "numLevels"));
			AddProblem(missing, CheckProperty(heroUpgradeDefinitionType, "HeroUpgradeDefinition.typeEnum", "typeEnum"));
			AddProblem(missing, CheckProperty(heroUpgradeDefinitionType, "HeroUpgradeDefinition.uniqueId", "uniqueId"));
			AddProblem(missing, CheckField(heroUpgradeType, "HeroUpgradeType.canBeStartItem", "canBeStartItem", false, typeof(bool)));
			// 我们按 List<T> 迭代/取索引，字段类型必须正好是 List<HeroUpgradeDefinition>
			Type listHolder = CloseGeneric(resourceListType, heroUpgradeDefinitionType);
			AddProblem(missing, CheckField(listHolder, "ResourceList<T>.list", "list", true, MakeListOf(heroUpgradeDefinitionType)));
			CoreSupported = missing.Count == 0;
			CoreMissing = missing.Count == 0 ? string.Empty : string.Join("、", missing.ToArray());

			// 手抄本：只认 UserSave.inventory 这个字段，字段类型（MetaInventory）不写死。
			// 整个探测包 try/catch —— 如果该字段的类型本身加载失败，这里也只记录问题，不抛异常。
			userInventoryField = null;
			inventoryAdd = null;
			inventoryGet = null;
			InventoryProblem = string.Empty;
			try
			{
				if (!object.ReferenceEquals(userSaveType, null))
				{
					userInventoryField = userSaveType.GetField("inventory", InstanceAny);
				}
				if (object.ReferenceEquals(userInventoryField, null))
				{
					InventoryProblem = "UserSave 里没有 inventory 字段";
				}
				else
				{
					BindInventory(userInventoryField.FieldType);
				}
			}
			catch (Exception ex)
			{
				userInventoryField = null;
				inventoryAdd = null;
				inventoryGet = null;
				InventoryProblem = "读取 userSave.inventory 失败：" + ex.Message;
			}
			bool hasAdd = !object.ReferenceEquals(inventoryAdd, null);
			bool hasGet = !object.ReferenceEquals(inventoryGet, null);
			InventorySupported = !object.ReferenceEquals(userInventoryField, null) && hasAdd && hasGet;
			if (!InventorySupported && InventoryProblem.Length == 0)
			{
				string typeName = object.ReferenceEquals(userInventoryField, null) ? "<null>" : userInventoryField.FieldType.FullName;
				InventoryProblem = "手抄本类型缺少符合签名的 Add/Get 方法（" + typeName + "）";
			}

			// 战役道具池：这里仍按强类型访问，所以必须逐项确认成员存在且类型一致
			Type campaignSaveType = FindType("Voxels.TowerDefense.ProfileInternals.CampaignSave");
			Type serializableUpgradeType = FindType("Voxels.TowerDefense.SerializableHeroUpgrade");
			string campaignIssue = null;
			if (object.ReferenceEquals(profileType, null))
			{
				campaignIssue = "缺少类型 Profile";
			}
			else if (object.ReferenceEquals(campaignSaveType, null))
			{
				campaignIssue = "缺少类型 CampaignSave";
			}
			else if (object.ReferenceEquals(serializableUpgradeType, null))
			{
				campaignIssue = "缺少类型 SerializableHeroUpgrade";
			}
			else
			{
				campaignIssue = CheckField(profileType, "Profile.campaign", "campaign", true, campaignSaveType);
				if (object.ReferenceEquals(campaignIssue, null))
				{
					campaignIssue = CheckField(campaignSaveType, "CampaignSave.inventory", "inventory", false, MakeListOf(serializableUpgradeType));
				}
				if (object.ReferenceEquals(campaignIssue, null))
				{
					campaignIssue = CheckField(serializableUpgradeType, "SerializableHeroUpgrade.definition", "definition", false, heroUpgradeDefinitionType);
				}
				// campaign.inventory.Add(def) 依赖 SerializableHeroUpgrade 的隐式转换运算符
				if (object.ReferenceEquals(campaignIssue, null))
				{
					campaignIssue = CheckConversion(serializableUpgradeType, "SerializableHeroUpgrade.op_Implicit", heroUpgradeDefinitionType, serializableUpgradeType);
				}
			}
			CampaignProblem = object.ReferenceEquals(campaignIssue, null) ? string.Empty : campaignIssue;
			CampaignSupported = CampaignProblem.Length == 0;
		}
		// 在所有已加载程序集里按完整类型名查找（不用 typeof，避免解析失败时 JIT 报错）
		internal static Type FindType(string fullName)
		{
			try
			{
				Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < assemblies.Length; i++)
				{
					Assembly assembly = assemblies[i];
					if (object.ReferenceEquals(assembly, null))
					{
						continue;
					}
					Type type = assembly.GetType(fullName, false);
					if (!object.ReferenceEquals(type, null))
					{
						return type;
					}
				}
			}
			catch (Exception ex)
			{
				ModLog.Warn("[GameCompat] 查找游戏类型 " + fullName + " 失败：" + ex.Message);
			}
			// 兜底：直接按程序集名解析（游戏逻辑都在 Assembly-CSharp；泛型名如 ResourceList`1 也适用）
			try
			{
				Type direct = Type.GetType(fullName + ", Assembly-CSharp", false);
				if (!object.ReferenceEquals(direct, null))
				{
					return direct;
				}
			}
			catch (Exception)
			{
			}
			return null;
		}

		// 读取 userSave.inventory；字段缺失/类型加载失败时返回 null（调用方按“不可用”处理）
		internal static object GetInventory(object userSave)
		{
			bool unusable = object.ReferenceEquals(userSave, null) || object.ReferenceEquals(userInventoryField, null);
			if (unusable)
			{
				return null;
			}
			try
			{
				object inventory = userInventoryField.GetValue(userSave);
				EnsureBound(inventory);
				return inventory;
			}
			catch (Exception ex)
			{
				ModLog.Warn("[GameCompat] 读取手抄本条目失败：" + ex.Message);
				return null;
			}
		}

		// 重建手抄本（userSave.inventory = new MetaInventory()）：用于清掉旧版本写入的“初始装备”标记，
		// 因为 MetaInventory.Add 只能把 isStarting 置 true，没有办法单独清除。
		internal static object ResetInventory(object userSave)
		{
			bool invalid = object.ReferenceEquals(userSave, null) || object.ReferenceEquals(userInventoryField, null);
			if (invalid)
			{
				return null;
			}
			try
			{
				object fresh = Activator.CreateInstance(userInventoryField.FieldType);
				userInventoryField.SetValue(userSave, fresh);
				EnsureBound(fresh);
				return fresh;
			}
			catch (Exception ex)
			{
				ModLog.Warn("[GameCompat] 重建手抄本条目失败：" + ex.Message);
				return null;
			}
		}

		internal static bool InventoryAdd(object inventory, object definition, int level, bool isStarting)
		{
			bool unusable = object.ReferenceEquals(inventory, null) || object.ReferenceEquals(definition, null) || object.ReferenceEquals(inventoryAdd, null);
			if (unusable)
			{
				return false;
			}
			EnsureBound(inventory);
			if (object.ReferenceEquals(inventoryAdd, null))
			{
				return false;
			}
			inventoryAdd.Invoke(inventory, new object[] { definition, level, isStarting });
			return true;
		}

		internal static bool InventoryGet(object inventory, object definition, out int level, out bool isNew, out bool isStarting)
		{
			level = -1;
			isNew = false;
			isStarting = false;
			bool unusable = object.ReferenceEquals(inventory, null) || object.ReferenceEquals(definition, null) || object.ReferenceEquals(inventoryGet, null);
			if (unusable)
			{
				return false;
			}
			EnsureBound(inventory);
			if (object.ReferenceEquals(inventoryGet, null))
			{
				return false;
			}
			object[] arguments = new object[] { definition, -1, false, false };
			object known = inventoryGet.Invoke(inventory, arguments);
			level = (int)arguments[1];
			isNew = (bool)arguments[2];
			isStarting = (bool)arguments[3];
			return known is bool && (bool)known;
		}

		private static void EnsureBound(object inventory)
		{
			if (object.ReferenceEquals(inventory, null))
			{
				return;
			}
			Type type = inventory.GetType();
			bool alreadyBound = object.ReferenceEquals(type, boundInventoryType);
			if (!alreadyBound)
			{
				BindInventory(type);
			}
		}
		// 从实际类型上找 MetaInventory.Add / MetaInventory.Get，签名不符合就当成不可用
		private static void BindInventory(Type inventoryType)
		{
			inventoryAdd = null;
			inventoryGet = null;
			boundInventoryType = inventoryType;
			if (object.ReferenceEquals(inventoryType, null))
			{
				return;
			}
			try
			{
				MethodInfo[] methods = inventoryType.GetMethods(InstanceAny);
				for (int i = 0; i < methods.Length; i++)
				{
					MethodInfo method = methods[i];
					string methodName = method.Name;
					if (methodName == "Add")
					{
						if (MatchesAdd(method))
						{
							inventoryAdd = method;
						}
					}
					else if (methodName == "Get")
					{
						if (MatchesGet(method))
						{
							inventoryGet = method;
						}
					}
				}
			}
			catch (Exception ex)
			{
				ModLog.Warn("[GameCompat] 解析手抄本方法失败：" + ex.Message);
				inventoryAdd = null;
				inventoryGet = null;
			}
		}

		// Add(HeroUpgradeDefinition upgrade, int level, bool isStarting)
		private static bool MatchesAdd(MethodInfo method)
		{
			try
			{
				ParameterInfo[] parameters = method.GetParameters();
				return parameters.Length == 3
					&& SameType(parameters[0].ParameterType, heroUpgradeDefinitionType)
					&& object.ReferenceEquals(parameters[1].ParameterType, typeof(int))
					&& object.ReferenceEquals(parameters[2].ParameterType, typeof(bool));
			}
			catch (Exception)
			{
				return false;
			}
		}

		// Get(HeroUpgradeDefinition upgrade, out int level, out bool isNew, out bool isStarting) -> bool
		private static bool MatchesGet(MethodInfo method)
		{
			try
			{
				ParameterInfo[] parameters = method.GetParameters();
				return parameters.Length == 4
					&& SameType(parameters[0].ParameterType, heroUpgradeDefinitionType)
					&& object.ReferenceEquals(parameters[1].ParameterType, typeof(int).MakeByRefType())
					&& object.ReferenceEquals(parameters[2].ParameterType, typeof(bool).MakeByRefType())
					&& object.ReferenceEquals(parameters[3].ParameterType, typeof(bool).MakeByRefType())
					&& object.ReferenceEquals(method.ReturnType, typeof(bool));
			}
			catch (Exception)
			{
				return false;
			}
		}

		// Type 用 ReferenceEquals 比较：Unity 的 .NET 2.0 Mono 里没有 Type.op_Equality
		private static bool SameType(Type left, Type right)
		{
			return object.ReferenceEquals(left, right);
		}

		private static Type RequireType(string fullName, List<string> missing)
		{
			Type type = FindType(fullName);
			if (object.ReferenceEquals(type, null))
			{
				missing.Add(fullName);
			}
			return type;
		}

		// 校验字段：存在 + 声明类型一致（expectedType 为 null 时只校验存在）
		private static string CheckField(Type owner, string label, string name, bool isStatic, Type expectedType)
		{
			if (object.ReferenceEquals(owner, null))
			{
				return null;
			}
			FieldInfo field;
			try
			{
				field = owner.GetField(name, isStatic ? StaticAny : InstanceAny);
			}
			catch (Exception ex)
			{
				return label + " 读取失败：" + ex.Message;
			}
			if (object.ReferenceEquals(field, null))
			{
				return label + " 不存在";
			}
			if (object.ReferenceEquals(expectedType, null))
			{
				return null;
			}
			Type actual;
			try
			{
				actual = field.FieldType;
			}
			catch (Exception ex2)
			{
				return label + " 类型无法解析：" + ex2.Message;
			}
			if (object.ReferenceEquals(actual, expectedType))
			{
				return null;
			}
			return label + " 类型不符（期望 " + expectedType.FullName + "，实际 " + actual.FullName + "）";
		}

		private static string CheckProperty(Type owner, string label, string name)
		{
			if (object.ReferenceEquals(owner, null))
			{
				return null;
			}
			try
			{
				if (object.ReferenceEquals(owner.GetProperty(name, InstanceAny), null))
				{
					return label + " 不存在";
				}
			}
			catch (Exception ex)
			{
				return label + " 读取失败：" + ex.Message;
			}
			return null;
		}

		private static string CheckMethod(Type owner, string label, string name, bool isStatic, int parameterCount)
		{
			if (object.ReferenceEquals(owner, null))
			{
				return null;
			}
			try
			{
				MethodInfo[] methods = owner.GetMethods(isStatic ? StaticAny : InstanceAny);
				for (int i = 0; i < methods.Length; i++)
				{
					MethodInfo method = methods[i];
					bool match = method.Name == name && method.GetParameters().Length == parameterCount;
					if (match)
					{
						return null;
					}
				}
			}
			catch (Exception ex)
			{
				return label + " 读取失败：" + ex.Message;
			}
			return label + " 不存在";
		}

		// 校验隐式/显式转换运算符：sourceType -> targetType
		private static string CheckConversion(Type owner, string label, Type sourceType, Type targetType)
		{
			bool invalid = object.ReferenceEquals(owner, null) || object.ReferenceEquals(sourceType, null) || object.ReferenceEquals(targetType, null);
			if (invalid)
			{
				return null;
			}
			try
			{
				MethodInfo[] methods = owner.GetMethods(StaticAny);
				for (int i = 0; i < methods.Length; i++)
				{
					MethodInfo method = methods[i];
					string methodName = method.Name;
					bool isConversion = methodName == "op_Implicit" || methodName == "op_Explicit";
					if (!isConversion)
					{
						continue;
					}
					ParameterInfo[] parameters = method.GetParameters();
					if (parameters.Length != 1)
					{
						continue;
					}
					bool matches = object.ReferenceEquals(parameters[0].ParameterType, sourceType) && object.ReferenceEquals(method.ReturnType, targetType);
					if (matches)
					{
						return null;
					}
				}
			}
			catch (Exception ex)
			{
				return label + " 读取失败：" + ex.Message;
			}
			return label + " 不存在";
		}

		// 把开放泛型 ResourceList<T> 闭合到 ResourceList<HeroUpgradeDefinition>
		private static Type CloseGeneric(Type genericDefinition, Type argument)
		{
			bool invalid = object.ReferenceEquals(genericDefinition, null) || object.ReferenceEquals(argument, null);
			if (invalid)
			{
				return genericDefinition;
			}
			try
			{
				return genericDefinition.MakeGenericType(new Type[] { argument });
			}
			catch (Exception)
			{
				return genericDefinition;
			}
		}

		private static Type MakeListOf(Type elementType)
		{
			if (object.ReferenceEquals(elementType, null))
			{
				return null;
			}
			try
			{
				return typeof(List<>).MakeGenericType(new Type[] { elementType });
			}
			catch (Exception)
			{
				return null;
			}
		}

		private static void AddProblem(List<string> problems, string problem)
		{
			if (!object.ReferenceEquals(problem, null))
			{
				problems.Add(problem);
			}
		}
	}
}
