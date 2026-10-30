using HarmonyLib;
using WKMPMod.Gameplay;

namespace WKMPMod.Patch;

[HarmonyPatch(typeof(DEN_Barnacle), "LateUpdate")]
public static class Patch_BarnacleAuthority {
	private static bool Prefix(DEN_Barnacle __instance) => !BarnacleNet.Enabled || BarnacleNet.Get(__instance).OwnsUpdate();
}

[HarmonyPatch(typeof(UT_Damage), nameof(UT_Damage.Damage), new[] { typeof(Damageable.DamageInfo) })]
public static class Patch_BarnacleDamage {
	private static bool Prefix(UT_Damage __instance, Damageable.DamageInfo info) => BarnacleNet.Damage(__instance, info);
}
