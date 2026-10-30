using HarmonyLib;
using WKMPMod.World;

namespace WKMPMod.Patch;

[HarmonyPatch]
public static class Patch_EnemyLifecycleOverride {
	private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods() {
		foreach (var type in typeof(GameEntity).Assembly.GetTypes()) {
			if (type == typeof(GameEntity) || !typeof(GameEntity).IsAssignableFrom(type) || typeof(ENT_Player).IsAssignableFrom(type)) continue;
			foreach (string name in new[] { "OnEnable", "OnDisable" }) {
				var method = type.GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly, null, System.Type.EmptyTypes, null);
				if (method != null && !method.IsAbstract) yield return method;
			}
		}
	}
	private static void Postfix(GameEntity __instance, System.Reflection.MethodBase __originalMethod) {
		if (__originalMethod.Name == "OnEnable") EnemySyncModule.Instance.OnEntityEnabled(__instance);
		else EnemySyncModule.Instance.OnEntityDisabled(__instance);
	}
}

[HarmonyPatch(typeof(GameEntity))]
public class Patch_GameEntity {
	#region[伤害同步]

	// DEN_Mother 没调用父类Damage
	public static void Patch_Damage(GameEntity __instance, Damageable.DamageInfo info) {
		EnemySyncModule.Instance.BroadcastEnemyDamage(__instance, info);
	}

	#endregion

	#region[实体生命周期拦截]

	// DEN_Teeth DEN_EngravedDoor 没调用父类OnDisable OnEnable
	// DEN_Hunter 没调用父类OnDisable

	/// <summary>
	/// 监听 GameEntity 启用/生成,提供增量扫描数据源
	/// </summary>
	[HarmonyPatch(nameof(GameEntity.OnEnable))]
	[HarmonyPostfix]
	public static void Patch_OnEnable(GameEntity __instance) {
		EnemySyncModule.Instance.OnEntityEnabled(__instance);
	}

	/// <summary>
	/// 监听 GameEntity 禁用/销毁,立即通知注销记录
	/// </summary>
	[HarmonyPatch(nameof(GameEntity.OnDisable))]
	[HarmonyPrefix]
	public static void Patch_OnDisable(GameEntity __instance) {
		EnemySyncModule.Instance.OnEntityDisabled(__instance);
	}

	#endregion

	#region[生物死亡同步]

	/// <summary>
	/// 监听 GameEntity 死亡,立即通知注销记录
	/// </summary>
	[HarmonyPatch(nameof(GameEntity.Kill), new[] { typeof(string), typeof(Damageable.DamageInfo) })]
	[HarmonyPrefix]
	public static void Patch_Prefix_Kill(GameEntity __instance, out bool __state) {
		__state = __instance.dead;
	}
	[HarmonyPatch(nameof(GameEntity.Kill), new[] { typeof(string), typeof(Damageable.DamageInfo) })]
	[HarmonyPostfix]
	public static void Patch_Postfix_Kill(GameEntity __instance, string type, bool __state) {
		// 执行前未死亡,执行后死亡 视为第一次死亡
		if (!__state && __instance.dead) EnemySyncModule.Instance.OnEntityKill(__instance, type);
	}

	#endregion
}

[HarmonyPatch(typeof(DEN_Bloodbug))]
public class Patch_DEN_Bloodbug {
	#region[血虫死亡同步]

	/// <summary>
	/// 监听 GameEntity 死亡,立即通知注销记录
	/// </summary>
	[HarmonyPatch(nameof(DEN_Bloodbug.Kill))]
	[HarmonyPrefix]
	public static void Patch_Prefix_Kill(DEN_Bloodbug __instance, out bool __state) {
		__state = __instance.dead;
	}

	[HarmonyPatch(nameof(DEN_Bloodbug.Kill))]
	[HarmonyPostfix]
	public static void Patch_Postfix_Kill(DEN_Bloodbug __instance, string type, bool __state) {
		// 执行前未死亡,执行后死亡 视为第一次死亡
		if (!__state && __instance.dead) EnemySyncModule.Instance.OnEntityKill(__instance, type);
	}

	#endregion
}
