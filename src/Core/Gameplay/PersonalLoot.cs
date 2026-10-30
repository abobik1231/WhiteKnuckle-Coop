using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.NetWork;

namespace WKMPMod.Gameplay;

[HarmonyPatch]
public static class PersonalLoot {
	private struct RandomScope { public bool Active; public UnityEngine.Random.State State; public System.Reflection.FieldInfo UseSeed; public bool PreviousUseSeed; }
	private static readonly Dictionary<int, int> calls = new Dictionary<int, int>();
	private static IEnumerable<System.Reflection.MethodBase> TargetMethods() {
		yield return AccessTools.Method(typeof(UT_SPT_Spawner), "Spawn");
		yield return AccessTools.Method(typeof(UT_RadiusSpawner), "Spawn");
		yield return AccessTools.Method(typeof(UT_MeshFaceSpawner), "Spawn");
		yield return AccessTools.Method(typeof(UT_TriggerSpawner), "Spawn");
		yield return AccessTools.Method(typeof(UT_SpawnChance), "Check");
	}
	private static void Prefix(MonoBehaviour __instance, out RandomScope __state) {
		__state = default;
		if (!MPCore.IsInLobby || !MPSteamworks.Instance.IsLan || !WorldLoader.initialized) return;
		if (__instance is UT_SpawnChance && __instance.GetComponent<Item_Object>() == null && !WorldLayoutSync.Enabled) return;
		const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
		var table = __instance.GetType().GetField("spawnTable", flags)?.GetValue(__instance) as SpawnTable;
		var objects = __instance.GetType().GetField("spawnObjects", flags)?.GetValue(__instance) as List<GameObject>;
		bool found = WorldLayoutSync.Enabled || __instance is UT_SpawnChance && __instance.GetComponent<Item_Object>() != null;
		if (table?.spawnList != null) foreach (var entry in table.spawnList) {
			if (entry?.prefabs == null) continue;
			foreach (var prefab in entry.prefabs) {
				if (prefab == null) continue;
				if (prefab.GetComponent<Item_Object>() == null && !WorldLayoutSync.Enabled) return;
				found = true;
			}
		}
		else if (objects != null) foreach (var prefab in objects) {
			if (prefab == null) continue;
			if (prefab.GetComponent<Item_Object>() == null && !WorldLayoutSync.Enabled) return;
			found = true;
		}
		if (!found) return;
		int instance = __instance.GetInstanceID();
		calls.TryGetValue(instance, out int count);
		calls[instance] = count + 1;
		if (calls.Count > 4096) calls.Clear();
		ulong key = CoopIdentity.For(__instance.transform);
		__state = new RandomScope { Active = true, State = UnityEngine.Random.state };
		__state.UseSeed = __instance.GetType().GetField("useSeed", flags);
		if (__state.UseSeed != null) { __state.PreviousUseSeed = (bool)__state.UseSeed.GetValue(__instance); __state.UseSeed.SetValue(__instance, false); }
		UnityEngine.Random.InitState(unchecked((int)(key ^ (key >> 32)) + count * 397));
	}
	private static Exception Finalizer(Exception __exception, MonoBehaviour __instance, RandomScope __state) {
		if (__state.Active) UnityEngine.Random.state = __state.State;
		if (__state.UseSeed != null) __state.UseSeed.SetValue(__instance, __state.PreviousUseSeed);
		return __exception;
	}
}
