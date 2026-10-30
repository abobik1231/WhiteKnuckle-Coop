using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using WKMPMod.Component;
using WKMPMod.NetWork;
using WKMPMod.World;

namespace WKMPMod.Patch;

[HarmonyPatch]
public static class Patch_CoopEnemyDamage {
	private static IEnumerable<MethodBase> TargetMethods() {
		foreach (var type in typeof(GameEntity).Assembly.GetTypes()) {
			if (!typeof(GameEntity).IsAssignableFrom(type) || type == typeof(ENT_Player)) continue;
			var method = type.GetMethod(nameof(GameEntity.Damage), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new[] { typeof(Damageable.DamageInfo) }, null);
			if (method != null && !method.IsAbstract) yield return method;
		}
	}
	private static bool Prefix(GameEntity __instance, Damageable.DamageInfo info, ref bool __result) {
		var sync = EnemySyncModule.Instance;
		if (MPSteamworks.IsHost || sync.ApplyingRemoteState || !sync.Synced(__instance)) return true;
		sync.BroadcastEnemyDamage(__instance, info);
		__result = false;
		return false;
	}
}
