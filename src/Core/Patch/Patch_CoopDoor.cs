using HarmonyLib;
using WKMPMod.Gameplay;

namespace WKMPMod.Patch;

[HarmonyPatch(typeof(UT_Door), nameof(UT_Door.Close))]
public static class Patch_CoopDoor {
	private static bool Prefix(UT_Door __instance) => CoopDoor.BeforeClose(__instance);
}
