using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.RemotePlayer;

namespace WKMPMod.Gameplay;

public static class CoopBranches {
	private static readonly AccessTools.FieldRef<UT_BranchGenerator, M_GenerationBranch> Selected = AccessTools.FieldRefAccess<UT_BranchGenerator, M_GenerationBranch>("selectedBranch");
	private static readonly HashSet<string> generating = new HashSet<string>();
	private static WorldLoader loader;
	private static readonly HashSet<WorldLoader.LevelInfo> retained = new HashSet<WorldLoader.LevelInfo>();
	private static float nextRetention;
	public static bool Lan => MPCore.IsInLobby && MPSteamworks.Instance.IsLan;
	public static void Generate(UT_BranchGenerator trigger, bool remote) {
		if (WorldLoader.instance == null || trigger.branch == null || trigger.branch.Count == 0 || trigger.currentLevel == null) return;
		if (loader != WorldLoader.instance) { loader = WorldLoader.instance; generating.Clear(); }
		ulong key = CoopIdentity.For(trigger.transform);
		string id = "wkmp-branch-" + key.ToString("x16");
		var existing = WorldLayoutSync.AllBranches().FirstOrDefault(branch => branch.id == id);
		if (existing != null) { if (!remote) trigger.StartCoroutine(Enter(trigger, existing, true)); return; }
		if (generating.Contains(id) && !WorldLoader.isLoaded) return;
		generating.Add(id);
		if (!remote) {
			var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.WorldLayout);
			writer.Put((byte)3).Put(WKMPMod.UI.ManualSaves.LoadGeneration).Put(key); MPSteamworks.Instance.Broadcast(writer);
		}
		int index = (int)(key % (ulong)trigger.branch.Count);
		var branchAsset = UnityEngine.Object.Instantiate(trigger.branch[index]);
		branchAsset.id = id; branchAsset.currentLevel = trigger.currentLevel; Selected(trigger) = branchAsset;
		var previousBranch = loader.currentBranch;
		var parentBranch = WorldLayoutSync.AllBranches().FirstOrDefault(item => item.levelTracker.Any(level => level.GetLevel() == trigger.currentLevel));
		if (parentBranch != null) loader.currentBranch = parentBranch;
		try { loader.GenerateBranchFromLevel(trigger.currentLevel, trigger.startTransform, branchAsset, branchAsset); } finally { loader.currentBranch = previousBranch; }
		var branch = WorldLayoutSync.AllBranches().FirstOrDefault(item => item.id == id);
		if (branch != null && !remote) trigger.StartCoroutine(Enter(trigger, branch, false));
	}
	private static IEnumerator Enter(UT_BranchGenerator trigger, WorldLoader.BranchInfo branch, bool existing) {
		float deadline = Time.unscaledTime + 65f;
		if (!existing) yield return new WaitForSecondsRealtime(0.3f);
		while (loader != null && (!WorldLoader.isLoaded || branch.levelTracker.Count == 0) && Time.unscaledTime < deadline) yield return null;
		if (loader == null || branch.levelTracker.Count == 0 || !WorldLoader.isLoaded) yield break;
		Selected(trigger) = branch.generationBranch;
		if (trigger.teleportPlayerIntoBranch && loader.currentBranch != branch) {
			loader.ChangeBranch(branch);
			var start = branch.levelTracker[0].GetLevel(); start.SetAsCurrentLevel();
			if (start.spawnPosition != null) ENT_Player.GetPlayer()?.Teleport(start.spawnPosition.position, start.spawnPosition.rotation);
		}
		if (trigger.switchToBranchAfterGenerating) {
			loader.ChangeBranch(branch);
			if (trigger.deleteRootBranch) loader.DeleteOtherBranches(branch.id);
		}
	}
	public static bool Occupied(WorldLoader.BranchInfo branch) {
		if (!Lan || branch?.levelTracker == null) return false;
		if (branch.levelTracker.Any(KeepLoaded)) return true;
		foreach (var peer in RPManager.Instance.Players.Values) {
			var player = peer.PlayerObject;
			if (player == null || !player.activeInHierarchy) continue;
			foreach (var info in branch.levelTracker) {
				var level = info?.GetLevel();
				if (level != null && level.GetLocalBounds().Contains(level.transform.InverseTransformPoint(player.transform.position))) return true;
			}
		}
		return false;
	}
	public static bool KeepLoaded(WorldLoader.LevelInfo info) {
		if (!Lan || !WorldLoader.isLoaded) return false;
		if (Time.unscaledTime >= nextRetention) { nextRetention = Time.unscaledTime + 0.1f; PreservePlayers(); }
		return retained.Contains(info);
	}
	private static void PreservePlayers() {
		retained.Clear();
		if (!Lan || !WorldLoader.isLoaded) return;
		var branches = WorldLayoutSync.AllBranches().ToArray();
		foreach (var peer in RPManager.Instance.Players.Values) {
			if (peer.PlayerObject == null || !peer.PlayerObject.activeInHierarchy) continue;
			WorldLoader.LevelInfo closest = null; float best = float.PositiveInfinity;
			Vector3 position = peer.PlayerObject.transform.position;
			foreach (var branch in branches) foreach (var info in branch.levelTracker) {
				var level = info?.GetLevel(); if (level == null) continue;
				float distance = (level.transform.TransformPoint(level.GetLocalBounds().ClosestPoint(level.transform.InverseTransformPoint(position))) - position).sqrMagnitude;
				if (distance < best) { best = distance; closest = info; }
			}
			if (closest == null || best > 100f) continue;
			retained.Add(closest);
			if (closest.connectedLevels != null) foreach (var adjacent in closest.connectedLevels) if (adjacent != null) retained.Add(adjacent);
		}
	}
	public static void Activate(WorldLoader.LevelInfo info) {
		var level = info?.GetLevel(); if (level == null) return;
		if (!level.gameObject.activeSelf) level.gameObject.SetActive(true);
		if (!info.loaded) { info.loaded = true; level.OnLevelActivate(); }
		if (info.spawnedLOD != null) info.spawnedLOD.SetActive(false);
	}
	public static void ActivateRetained() { if (Lan && WorldLoader.isLoaded) foreach (var info in retained) Activate(info); }
}

[HarmonyPatch(typeof(UT_BranchGenerator), nameof(UT_BranchGenerator.Generate))]
public static class Patch_CoopBranchGeneration {
	private static bool Prefix(UT_BranchGenerator __instance) { if (!CoopBranches.Lan) return true; CoopBranches.Generate(__instance, WorldLayoutSync.ApplyingEvent); return false; }
}
[HarmonyPatch(typeof(WorldLoader), nameof(WorldLoader.DeleteBranch))]
public static class Patch_CoopBranchRetention { private static bool Prefix(WorldLoader.BranchInfo b) => !CoopBranches.Occupied(b); }
[HarmonyPatch]
public static class Patch_CoopVisibility {
	private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => AccessTools.GetDeclaredMethods(typeof(WorldLoader)).Where(method => method.Name.Contains("g__CheckLevelLoad"));
	private static bool Prefix(WorldLoader.LevelInfo info, WorldLoader.LevelInfo currentLevel, ref bool __result) {
		if (ReferenceEquals(info, currentLevel) || !CoopBranches.KeepLoaded(info)) return true;
		CoopBranches.Activate(info); info.adjacent = true; __result = true; return false;
	}
}
[HarmonyPatch(typeof(WorldLoader), "LoadUnload")]
public static class Patch_CoopRetainedVisibility { private static void Postfix() => CoopBranches.ActivateRetained(); }
[HarmonyPatch(typeof(WorldLoader), nameof(WorldLoader.UnloadPreviousLevels))]
public static class Patch_CoopPreviousLevels {
	private static void Prefix(WorldLoader __instance, ref int offset) {
		if (!CoopBranches.Lan) return;
		var branch = __instance.GetCurrentBranch(); if (branch?.levelTracker == null) return;
		int current = branch.levelTracker.IndexOf(branch.currentLevel);
		for (int i = 0; i < current; i++) if (CoopBranches.KeepLoaded(branch.levelTracker[i])) { offset = Mathf.Max(offset, current - i); break; }
	}
}
