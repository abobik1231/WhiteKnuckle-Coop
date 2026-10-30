using System;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Splines;
using System.Linq;
using WKMPMod.Core;
using WKMPMod.NetWork;
using WKMPMod.RemotePlayer;

namespace WKMPMod.Gameplay;

public static class MechanismSnapshots {
	private static HashSet<M_Level> LoadedLevels() => new HashSet<M_Level>(WorldLayoutSync.AllBranches().SelectMany(branch => branch.levelTracker).Select(info => info?.GetLevel()).Where(level => level != null));
	private static IEnumerable<T> Objects<T>(HashSet<M_Level> levels) where T : UnityEngine.Component { foreach (var level in levels ?? LoadedLevels()) if (level != null) foreach (var item in level.GetComponentsInChildren<T>(true)) yield return item; }
	[Serializable] public sealed class Snapshot { public List<Entry> entries = new List<Entry>(); public string actions; public List<string> levels; }
	[Serializable] public sealed class Entry {
		public string kind;
		public string id;
		public bool a;
		public bool b;
		public bool c;
		public int i;
		public float x;
		public float y;
		public float z;
		public float[] position;
		public float[] start;
		public float[] rotation;
		public float[] startRotation;
		public List<bool> flags;
	}
	private static object Get(object item, string name) => AccessTools.Field(item.GetType(), name).GetValue(item);
	private static void Set(object item, string name, object value) => AccessTools.Field(item.GetType(), name).SetValue(item, value);
	private static float[] Values(Vector3 value) => new[] { value.x, value.y, value.z };
	private static float[] Values(Quaternion value) => new[] { value.x, value.y, value.z, value.w };
	private static Vector3 Vector(float[] value) => new Vector3(value[0], value[1], value[2]);
	private static Quaternion Rotation(float[] value) => new Quaternion(value[0], value[1], value[2], value[3]);
	private static M_Level Level(UnityEngine.Component item) => item.GetComponentInParent<M_Level>(true);
	private static Entry New(string kind, UnityEngine.Component item) => new Entry { kind = kind, id = CoopIdentity.For(item.transform).ToString() };
	private static HashSet<M_Level> LevelsToKeep() {
		if (!WorldLoader.initialized || !WorldLoader.isLoaded || WorldLoader.instance == null || ENT_Player.GetPlayer() == null) return null;
		var levels = new HashSet<M_Level>();
		void AddNear(Vector3 position) {
			var info = WorldLoader.GetClosestLevelToPosition(position);
			if (info == null || info.GetLevel() == null) return;
			levels.Add(info.GetLevel());
			if (info.connectedLevels != null) foreach (var neighbor in info.connectedLevels) if (neighbor?.GetLevel() != null) levels.Add(neighbor.GetLevel());
			var branch = info.branch;
			if (branch?.levelTracker != null) {
				int index = branch.levelTracker.IndexOf(info);
				if (index > 0 && branch.levelTracker[index - 1]?.GetLevel() != null) levels.Add(branch.levelTracker[index - 1].GetLevel());
				if (index >= 0 && index + 1 < branch.levelTracker.Count && branch.levelTracker[index + 1]?.GetLevel() != null) levels.Add(branch.levelTracker[index + 1].GetLevel());
			}
		}
		AddNear(ENT_Player.GetPlayer().transform.position);
		if (MPCore.IsInLobby && MPSteamworks.Instance.IsLan) foreach (ulong id in MPSteamworks.Instance.ConnectedPeerIds) {
			if (!RPManager.Instance.Players.TryGetValue(id, out var peer) || peer.PlayerObject == null) return null;
			var body = peer.PlayerObject.GetComponentInChildren<WKMPMod.Component.RemotePlayer>(true);
			if (body == null || !body.HasSnapshot || Time.unscaledTime - body.LastReceivedTime > 1f) return null;
			AddNear(body.NetworkPosition);
		}
		return levels.Count > 0 ? levels : null;
	}
	public static string Capture() {
		var snapshot = new Snapshot();
		var keep = LevelsToKeep();
		snapshot.levels = (keep ?? LoadedLevels()).Select(level => CoopIdentity.For(level.transform).ToString()).ToList();
        snapshot.actions = CoopWorldActions.Capture(keep);
		bool Relevant(UnityEngine.Component item) { var level = Level(item); return level != null && (keep == null || keep.Contains(level)) && !PersonalHandholds.Contains(item); }
		foreach (var button in Objects<CL_Button>(keep)) {
			if (!Relevant(button) || WorldButtons.Personal(button)) continue;
			var e = New("button", button); e.a = button.active; e.b = button.hasBeenPressed; e.i = (int)Get(button, "presses");
			if (button is CL_ToggleButton toggle) e.c = toggle.state;
			snapshot.entries.Add(e);
		}
		foreach (var door in Objects<UT_Door>(keep)) {
			if (!Relevant(door)) continue;
			var e = New("door", door); e.a = WorldDoors.State(door); snapshot.entries.Add(e);
		}
		foreach (var passage in Objects<CoopPassageDoor>(keep)) { var e = New("passage", passage.Toggle); e.a = passage.Toggle.active; e.b = passage.Toggle.locked; snapshot.entries.Add(e); }
		foreach (var counter in Objects<UT_Counter>(keep)) {
			if (!Relevant(counter)) continue;
			var e = New("counter", counter); e.i = counter.count; e.flags = new List<bool>();
			if (counter.counterEvents != null) foreach (var item in counter.counterEvents) e.flags.Add(item.hasRun);
			snapshot.entries.Add(e);
		}
		foreach (var gate in Objects<UT_Gate_And>(keep)) {
			if (!Relevant(gate)) continue;
			var e = New("gate", gate); e.a = gate.inputA; e.b = gate.inputB; snapshot.entries.Add(e);
		}
		foreach (var platform in Objects<UT_Platform>(keep)) {
			if (!Relevant(platform)) continue;
			var e = New("platform", platform); e.x = platform.curPosition; e.y = (float)Get(platform, "moveSpeed"); snapshot.entries.Add(e);
		}
		foreach (var mover in Objects<UT_PathMover>(keep)) {
			var level = Level(mover);
			if (!Relevant(mover) || level == null || mover.moveObject == null || mover.pathPoints == null || mover.pathPoints.Count == 0) continue;
			var e = New("mover", mover); e.a = mover.active; e.b = (bool)Get(mover, "isMovingForward"); e.c = (bool)Get(mover, "hasPlayedStartSound");
			e.i = (int)Get(mover, "currentPointIndex"); e.x = (float)Get(mover, "currentLerpTime"); e.y = (float)Get(mover, "currentWaitTime"); e.z = (float)Get(mover, "currentSegmentDistance");
			e.position = Values(level.transform.InverseTransformPoint(mover.moveObject.position)); e.rotation = Values(Quaternion.Inverse(level.transform.rotation) * mover.moveObject.rotation);
			e.start = Values(level.transform.InverseTransformPoint((Vector3)Get(mover, "segmentStartPosition"))); e.startRotation = Values(Quaternion.Inverse(level.transform.rotation) * (Quaternion)Get(mover, "segmentStartRotation"));
			snapshot.entries.Add(e);
		}
		foreach (var flower in Objects<ENV_SignalOrchid_VineFlower>(keep)) { if (!Relevant(flower)) continue; var e = New("flower", flower); e.x = (float)Get(flower, "growAmount"); snapshot.entries.Add(e); }
		foreach (var rigid in Objects<Rigidbody>(keep)) {
			var level = Level(rigid); if (!Relevant(rigid) || rigid.GetComponentInParent<AIGameEntity>() != null || rigid.GetComponentInParent<Item_Object>() != null || rigid.GetComponentInParent<ENT_Player>() != null || rigid.GetComponentInParent<WKMPMod.Component.RPContainerRef>() != null) continue;
			var e = New("rigid", rigid); e.position = Values(level.transform.InverseTransformPoint(rigid.position)); e.rotation = Values(Quaternion.Inverse(level.transform.rotation) * rigid.rotation); e.a = rigid.isKinematic; e.b = rigid.gameObject.activeSelf; e.start = Values(rigid.velocity); snapshot.entries.Add(e);
		}
		return JsonConvert.SerializeObject(snapshot);
	}
	public static string Merge(IEnumerable<string> parts) {
		var merged = new Snapshot();
		var seen = new HashSet<string>();
		var actions = new Dictionary<string, CoopWorldActions.ActionState>();
        var levels = new HashSet<string>(); bool allLevels = false;
		foreach (string json in parts) {
			if (string.IsNullOrEmpty(json)) continue;
			var part = JsonConvert.DeserializeObject<Snapshot>(json);
			if (part?.entries == null || part.entries.Count > 100000) throw new InvalidOperationException("Неверное состояние механизмов.");
			if (!string.IsNullOrEmpty(part.actions)) foreach (var action in JsonConvert.DeserializeObject<List<CoopWorldActions.ActionState>>(part.actions)) if (action?.token != null) actions[action.token] = action;
			if (part.levels == null) allLevels = true; else levels.UnionWith(part.levels);
            foreach (var entry in part.entries) if (entry != null && entry.id != null && entry.kind != null && seen.Add(entry.kind + ":" + entry.id)) merged.entries.Add(entry);
		}
		merged.levels = allLevels ? null : levels.ToList(); merged.actions = JsonConvert.SerializeObject(actions.Values); return JsonConvert.SerializeObject(merged);
	}
	public static void Restore(string json) {
		if (string.IsNullOrEmpty(json)) return;
		using var scope = new RemoteWorldEffects();
		Snapshot snapshot;
		try { snapshot = JsonConvert.DeserializeObject<Snapshot>(json); } catch (Exception ex) { MPMain.LogError("[Saves] Mechanisms: " + ex.Message); return; }
		if (snapshot?.entries == null || snapshot.entries.Count > 100000) return;
		var selected = snapshot.levels != null ? new HashSet<M_Level>(LoadedLevels().Where(level => snapshot.levels.Contains(CoopIdentity.For(level.transform).ToString()))) : null;
        CoopWorldActions.Restore(snapshot.actions);
		var entries = new Dictionary<string, Entry>();
		foreach (var entry in snapshot.entries) if (entry != null && entry.id != null && entry.kind != null) entries[entry.kind + ":" + entry.id] = entry;
		Entry Find(string kind, UnityEngine.Component item) => !PersonalHandholds.Contains(item) && entries.TryGetValue(kind + ":" + CoopIdentity.For(item.transform), out var entry) ? entry : null;
		foreach (var door in Objects<UT_Door>(selected)) {
			if (Level(door) == null || Find("door", door) is not { } e) continue;
			WorldDoors.RestoreState(door, e.a);
		}
		foreach (var passage in Objects<CoopPassageDoor>(selected)) { if (Find("passage", passage.Toggle) is { } e) passage.Restore(e.a, e.b); }
		foreach (var button in Objects<CL_Button>(selected)) {
			if (Level(button) == null || Find("button", button) is not { } e) continue;
			if (WKMPMod.UI.ManualSaves.Loading) { button.StopAllCoroutines(); Set(button, "cooldown", false); Set(button, "cooldownRoutine", null); }
			button.active = e.a; button.hasBeenPressed = e.b; Set(button, "presses", e.i);
			if (button is CL_ToggleButton toggle) toggle.SetStateWithoutEvent(e.c);
			AccessTools.Method(typeof(CL_Button), "ChooseMaterial").Invoke(button, null);
		}
		foreach (var counter in Objects<UT_Counter>(selected)) {
			if (Level(counter) == null || Find("counter", counter) is not { } e) continue;
			counter.count = e.i;
			if (counter.counterEvents != null && e.flags != null) for (int i = 0; i < counter.counterEvents.Count && i < e.flags.Count; i++) counter.counterEvents[i].hasRun = e.flags[i];
		}
		foreach (var gate in Objects<UT_Gate_And>(selected)) {
			if (Level(gate) == null || Find("gate", gate) is not { } e) continue;
			gate.inputA = e.a; gate.inputB = e.b;
		}
		foreach (var platform in Objects<UT_Platform>(selected)) {
			if (Level(platform) == null || Find("platform", platform) is not { } e) continue;
			platform.curPosition = e.x; platform.SetMoveSpeed(e.y);
		}
		foreach (var mover in Objects<UT_PathMover>(selected)) {
			var level = Level(mover);
			if (level == null || Find("mover", mover) is not { } e || mover.moveObject == null || mover.pathPoints == null || e.i < 0 || e.i >= mover.pathPoints.Count || e.position?.Length != 3 || e.start?.Length != 3 || e.rotation?.Length != 4 || e.startRotation?.Length != 4) continue;
			mover.active = e.a; Set(mover, "isMovingForward", e.b); Set(mover, "hasPlayedStartSound", e.c); Set(mover, "currentPointIndex", e.i);
			Set(mover, "currentLerpTime", e.x); Set(mover, "currentWaitTime", e.y); Set(mover, "currentSegmentDistance", e.z);
			mover.moveObject.SetPositionAndRotation(level.transform.TransformPoint(Vector(e.position)), level.transform.rotation * Rotation(e.rotation));
			Set(mover, "segmentStartPosition", level.transform.TransformPoint(Vector(e.start))); Set(mover, "segmentStartRotation", level.transform.rotation * Rotation(e.startRotation));
		}
		foreach (var flower in Objects<ENV_SignalOrchid_VineFlower>(selected)) {
			if (Find("flower", flower) is not { } e || e.x < 0.999f || (float)Get(flower, "growAmount") >= 0.999f) continue;
			flower.StopAllCoroutines(); Set(flower, "growAmount", 1f);
			float t = flower.growCurve.Evaluate(1); flower.targetSplineExtrude.Range = new Vector2(0, t); flower.targetSplineExtrude.Rebuild();
			flower.headGameObject.transform.localPosition = flower.targetSplineExtrude.Spline.EvaluatePosition(t);
			flower.headGameObject.transform.localRotation = Quaternion.LookRotation(flower.targetSplineExtrude.Spline.EvaluateTangent(t));
		}
		foreach (var rigid in Objects<Rigidbody>(selected)) {
			var level = Level(rigid); if (level == null || Find("rigid", rigid) is not { } e || e.position?.Length != 3 || e.rotation?.Length != 4) continue;
			rigid.position = level.transform.TransformPoint(Vector(e.position)); rigid.rotation = level.transform.rotation * Rotation(e.rotation); rigid.isKinematic = e.a;
			if (!e.a && e.start?.Length == 3) rigid.velocity = Vector(e.start);
		}
	}
}
