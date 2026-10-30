using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Splines;
using WKMPMod.NetWork;

namespace WKMPMod.Gameplay;

public static class CoopFlowers {
	private sealed class Growth { public ENV_SignalOrchid_VineFlower Flower; public float Next; public float End; }
	private static readonly Dictionary<SplineExtrude, Growth> growth = new Dictionary<SplineExtrude, Growth>();
	public static int Requested { get; private set; }
	public static int Rebuilt { get; private set; }
	public static void Reset() { growth.Clear(); Requested = Rebuilt = 0; }
	public static void Start(ENV_SignalOrchid_VineFlower flower) { if (MPSteamworks.Instance.IsLan && flower.targetSplineExtrude != null) growth[flower.targetSplineExtrude] = new Growth { Flower = flower, End = flower.growCurve.Evaluate(1) }; }
	public static bool Rebuild(SplineExtrude target) {
		if (!growth.TryGetValue(target, out var state)) return true;
		Requested++;
		if (state.Flower == null || !MPSteamworks.Instance.IsLan || target.Range.y >= state.End - 0.0001f) { growth.Remove(target); Rebuilt++; return true; }
		if (Time.unscaledTime < state.Next) return false;
		state.Next = Time.unscaledTime + 1f / 30f; Rebuilt++; return true;
	}
}

[HarmonyPatch(typeof(SplineExtrude), nameof(SplineExtrude.Rebuild))]
public static class Patch_FlowerMeshRate { private static bool Prefix(SplineExtrude __instance) => CoopFlowers.Rebuild(__instance); }
