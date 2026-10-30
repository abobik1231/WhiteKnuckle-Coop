using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WKMPMod.Component;
using WKMPMod.NetWork;

namespace WKMPMod.Patch;

[HarmonyPatch]
public static class Patch_RemotePlayerTriggers {
	private static IEnumerable<MethodBase> TargetMethods() { yield return AccessTools.Method(typeof(UT_TriggerZone), "OnTriggerEnter"); yield return AccessTools.Method(typeof(UT_TriggerZone), "OnTriggerExit"); }
	private static bool Prefix(Collider other) => !MPSteamworks.Instance.IsLan || other == null || other.GetComponentInParent<RPContainerRef>(true) == null;
}
