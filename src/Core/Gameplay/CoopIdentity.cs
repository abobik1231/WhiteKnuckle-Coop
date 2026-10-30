using System.Collections.Generic;
using System;
using HarmonyLib;
using UnityEngine;
using WKMPMod.Util;

namespace WKMPMod.Gameplay;

public static class CoopIdentity {
	private sealed class Cached { public Transform Transform; public M_Level Level; public int Seed; public ulong Id; }
	private static readonly Dictionary<int, Cached> cache = new Dictionary<int, Cached>();
	private static readonly Dictionary<int, int> primed = new Dictionary<int, int>();
	private static readonly Dictionary<ulong, Cached> byId = new Dictionary<ulong, Cached>();
	private static float nextCleanup;
	private static void Remember(int instance, Cached value) { cache[instance] = value; byId[value.Id] = value; }
	public static void Reset() { cache.Clear(); primed.Clear(); byId.Clear(); }
	public static UnityEngine.Component Find(ulong id, Type type) {
		if (byId.TryGetValue(id, out var found) && found.Transform != null && found.Level != null && found.Level.GetLevelSeed() == found.Seed) return found.Transform.GetComponent(type);
		foreach (var branch in WorldLayoutSync.AllBranches()) foreach (var info in branch.levelTracker) if (info?.GetLevel() != null) { Prime(info.GetLevel()); if (byId.TryGetValue(id, out found) && found.Transform != null && found.Level != null && found.Level.GetLevelSeed() == found.Seed) return found.Transform.GetComponent(type); }
		return null;
	}
	public static void Prime(M_Level level) {
		if (Time.unscaledTime >= nextCleanup) { nextCleanup = Time.unscaledTime + 30; var expired = new List<int>(); foreach (var entry in cache) if (entry.Value.Transform == null || entry.Value.Level == null) expired.Add(entry.Key); foreach (int key in expired) { var value = cache[key]; if (byId.TryGetValue(value.Id, out var indexed) && ReferenceEquals(indexed, value)) byId.Remove(value.Id); cache.Remove(key); } }
		int seed = level.GetLevelSeed();
		if (primed.TryGetValue(level.GetInstanceID(), out int previous) && previous == seed) return;
		primed[level.GetInstanceID()] = seed;
		string frame = level.levelName + ":" + seed + "/";
		void Walk(Transform parent, string path) {
			Remember(parent.GetInstanceID(), new Cached { Transform = parent, Level = level, Seed = seed, Id = MPUtil.Hash64(frame + path) });
			var counts = new Dictionary<string, int>();
			for (int i = 0; i < parent.childCount; i++) { var child = parent.GetChild(i); counts.TryGetValue(child.name, out int ordinal); counts[child.name] = ordinal + 1; Walk(child, path + (path.Length > 0 ? "/" : "") + MPUtil.CleanCloneName(child.name) + "[" + ordinal + "]"); }
		}
		Walk(level.transform, "");
	}
	public static ulong For(Transform transform) {
		int instance = transform.GetInstanceID();
		if (cache.TryGetValue(instance, out var cached) && cached.Transform == transform && cached.Level != null && cached.Level.GetLevelSeed() == cached.Seed) return cached.Id;
		var level = transform.GetComponentInParent<M_Level>(true);
		if (level != null) { Prime(level); if (cache.TryGetValue(instance, out cached) && cached.Transform == transform) return cached.Id; }
		if (level == null && WorldLoader.initialized && WorldLoader.isLoaded) level = WorldLoader.GetClosestLevelToPosition(transform.position)?.GetLevel();
		var parts = new Stack<string>();
		var current = transform;
		while (current != null && (level == null ? current.parent != null : current != level.transform)) {
			int ordinal = 0;
			if (current.parent != null) for (int i = 0; i < current.GetSiblingIndex(); i++) if (current.parent.GetChild(i).name == current.name) ordinal++;
			parts.Push(MPUtil.CleanCloneName(current.name) + "[" + ordinal + "]");
			current = current.parent;
		}
		ulong id = MPUtil.Hash64((level == null ? transform.gameObject.scene.name : level.levelName + ":" + AccessTools.Field(typeof(M_Level), "levelSeed").GetValue(level)) + "/" + string.Join("/", parts));
		if (level != null) Remember(instance, new Cached { Transform = transform, Level = level, Seed = level.GetLevelSeed(), Id = id });
		return id;
	}
}
