using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WKMPMod.Component;
using WKMPMod.Gameplay;
using WKMPMod.NetWork;
using WKMPMod.RemotePlayer;
using WKMPMod.World;

namespace WKMPMod.Patch;

[HarmonyPatch]
public static class Patch_EnemyAuthority {
	private static IEnumerable<MethodBase> TargetMethods() {
		foreach (var type in typeof(GameEntity).Assembly.GetTypes()) {
			if (!typeof(GameEntity).IsAssignableFrom(type) || typeof(ENT_Player).IsAssignableFrom(type) || typeof(DEN_Barnacle).IsAssignableFrom(type)) continue;
			foreach (string name in new[] { "Update", "FixedUpdate", "LateUpdate", "TickUpdate" }) {
				var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly | BindingFlags.Instance, null, System.Type.EmptyTypes, null);
				if (method != null && !method.IsAbstract) yield return method;
			}
		}
	}
	private static bool Prefix(GameEntity __instance, out bool __state) {
		__state = false;
		bool run = __instance is DEN_Barnacle || __instance is DEN_VentThing && VentCoop.Enabled || !MPSteamworks.Instance.IsLan || MPSteamworks.IsHost || !EnemySyncModule.Instance.IsEnabled || __instance.GetComponent<NetworkedEnemy>() == null;
		if (run && MPSteamworks.Instance.IsLan && MPSteamworks.IsHost && EnemySyncModule.Instance.Synced(__instance)) { CoopEnemyCombat.Depth++; __state = true; }
		return run;
	}
	private static System.Exception Finalizer(bool __state, System.Exception __exception) { if (__state) CoopEnemyCombat.Depth = System.Math.Max(0, CoopEnemyCombat.Depth - 1); return __exception; }
}

[HarmonyPatch(typeof(AIGameEntity), nameof(AIGameEntity.SetTarget))]
public static class Patch_EnemyPlayerTarget {
	private static void Prefix(AIGameEntity __instance, ref Transform entity) {
		if (!MPSteamworks.Instance.IsLan || !MPSteamworks.IsHost || !EnemySyncModule.Instance.Synced(__instance) || entity == null || entity.GetComponent<ENT_Player>() == null) return;
		var target = CoopEnemyTargets.TargetFor(__instance);
		if (target != null) entity = target;
	}
}
