using BepInEx.Logging;
using UnityEngine;

namespace FullUnlockMod
{
	// 统一日志出口：优先走 BepInEx 日志（会写进 LogOutput.log），插件未就绪时退回 Unity 日志。
	// 为什么不直接用 Debug.Log：BepInEx 默认 WriteUnityLog=false，Unity 日志只进控制台、不进日志文件，
	// 玩家反馈日志时解锁摘要就会丢。
	internal static class ModLog
	{
		internal static ManualLogSource Source;

		internal static void Info(string message)
		{
			bool ready = Source != null;
			if (ready)
			{
				Source.LogInfo(message);
			}
			else
			{
				Debug.Log(message);
			}
		}

		internal static void Warn(string message)
		{
			bool ready = Source != null;
			if (ready)
			{
				Source.LogWarning(message);
			}
			else
			{
				Debug.LogWarning(message);
			}
		}

		internal static void Error(string message)
		{
			bool ready = Source != null;
			if (ready)
			{
				Source.LogError(message);
			}
			else
			{
				Debug.LogError(message);
			}
		}
	}
}
