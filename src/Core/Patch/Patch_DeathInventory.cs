using System;
using HarmonyLib;
using WKMPMod.NetWork;

namespace WKMPMod.Patch;

[HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.Kill))]
public static class Patch_DeathInventory {
	private static int depth;
	public static bool Preserving => depth > 0;
	[HarmonyPriority(Priority.First)]
	private static void Prefix(out bool __state) { __state = MPSteamworks.Instance.IsLan; if (__state) { WKMPMod.Gameplay.PersonalEquipment.Capture(); WKMPMod.Gameplay.BoundArtifacts.Capture(); depth++; } }
	private static Exception Finalizer(Exception __exception, bool __state) { if (__state) depth = Math.Max(0, depth - 1); return __exception; }
}

[HarmonyPatch(typeof(ENT_Player), "StashItemsInHand")]
public static class Patch_DeathStash { private static bool Prefix() => !Patch_DeathInventory.Preserving; }
