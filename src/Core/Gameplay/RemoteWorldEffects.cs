using System;
using HarmonyLib;
using UnityEngine;

namespace WKMPMod.Gameplay;

public sealed class RemoteWorldEffects : IDisposable {
	private static int depth;
	public static bool Active => depth > 0;
	public RemoteWorldEffects() { depth++; }
	public void Dispose() { depth = Math.Max(0, depth - 1); }
}

public sealed class RemoteDoorMotion : MonoBehaviour { public bool Replay; }
[HarmonyPatch(typeof(UT_Door), "FixedUpdate")]
public static class Patch_RemoteDoorMotion {
	private static void Prefix(UT_Door __instance, out RemoteWorldEffects __state) { __state = __instance.GetComponent<RemoteDoorMotion>()?.Replay == true ? new RemoteWorldEffects() : null; }
	private static Exception Finalizer(Exception __exception, RemoteWorldEffects __state) { __state?.Dispose(); return __exception; }
}

[HarmonyPatch(typeof(UT_PlayerTakeOver), nameof(UT_PlayerTakeOver.SetActive))]
public static class Patch_RemoteTakeover { private static bool Prefix(bool b) => !b || !RemoteWorldEffects.Active; }
[HarmonyPatch(typeof(OS_Manager), nameof(OS_Manager.Activate))]
public static class Patch_RemoteComputer { private static bool Prefix() => !RemoteWorldEffects.Active; }
