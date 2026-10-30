using System;
using System.Collections.Generic;
using UnityEngine;
using WKMPMod.Data;

namespace WKMPMod.Gameplay;

public static class CoopCoordinates {
	private static readonly Dictionary<ulong, Quaternion> rotations = new Dictionary<ulong, Quaternion>();
	private static readonly Dictionary<string, (M_Level Level, float Until)> frames = new Dictionary<string, (M_Level, float)>();
	private static M_Level localLevel;
	private static Vector3 lastFramePosition;
	private static float nextFrame;
	public static void Reset() { rotations.Clear(); frames.Clear(); localLevel = null; nextFrame = 0; }
	private static string BranchOf(M_Level level) {
		foreach (var branch in WorldLayoutSync.AllBranches()) foreach (var entry in branch.levelTracker) if (entry?.GetLevel() == level) return branch.id;
		return "";
	}
	public static void WritePointFrame(DataWriter writer, Transform transform) {
		var player = ENT_Player.GetPlayer();
		float height = player != null && CL_GameManager.gMan != null ? CL_GameManager.gMan.GetPlayerCorrectedHeight() - player.transform.position.y : 0;
		var level = transform.GetComponentInParent<M_Level>(true);
		writer.Put(height).Put(level?.levelName ?? "").Put(level != null ? level.transform.position : Vector3.zero).Put(level != null ? level.transform.rotation : Quaternion.identity).Put(level != null ? BranchOf(level) : "").Put(level != null ? level.GetLevelSeed() : 0);
	}
	public static Vector3 ReadPointFrame(DataReader reader, Vector3 position, out Quaternion delta) {
		float height = reader.GetFloat(); string name = reader.GetString(); Vector3 origin = reader.GetVector3(); Quaternion rotation = reader.GetQuaternion();
		string branch = reader.GetString(); int seed = reader.GetInt();
		return MapPosition(position, name, origin, rotation, height, out delta, branch, seed);
	}
	public static void WriteFrame(ref PlayerData data, ENT_Player player) {
		data.CheckpointGeneration = WKMPMod.UI.ManualSaves.LoadGeneration;
		if (player == null || CL_GameManager.gMan == null) return;
		data.CoordinateLevel = null; data.CoordinateBranch = null; data.CoordinateSeed = 0; data.CoordinateOrigin = Vector3.zero; data.CoordinateRotation = Quaternion.identity;
		data.HasCoordinateFrame = true;
		data.CoordinateHeight = CL_GameManager.gMan.GetPlayerCorrectedHeight() - player.transform.position.y;
		if (!WorldLoader.initialized || !WorldLoader.isLoaded) return;
		if (localLevel == null || Time.unscaledTime >= nextFrame || (player.transform.position - lastFramePosition).sqrMagnitude > 25) {
			nextFrame = Time.unscaledTime + 0.1f; lastFramePosition = player.transform.position;
			localLevel = WorldLoader.GetClosestLevelToPosition(player.transform.position)?.GetLevel();
		}
		var level = localLevel;
		if (level == null || string.IsNullOrEmpty(level.levelName)) return;
		data.CoordinateLevel = level.levelName;
		data.CoordinateBranch = BranchOf(level);
		data.CoordinateSeed = level.GetLevelSeed();
		data.CoordinateOrigin = level.transform.position;
		data.CoordinateRotation = level.transform.rotation;
	}
	private static bool LocalFrame(string name, Vector3 origin, float height, out Vector3 localOrigin, out Quaternion localRotation, string branchId, int seed) {
		localOrigin = Vector3.zero; localRotation = Quaternion.identity;
		if (string.IsNullOrEmpty(name) || !WorldLoader.initialized || !WorldLoader.isLoaded || WorldLoader.instance == null || CL_GameManager.gMan == null || ENT_Player.GetPlayer() == null) return false;
		float localHeight = CL_GameManager.gMan.GetPlayerCorrectedHeight() - ENT_Player.GetPlayer().transform.position.y;
		string key = branchId + ":" + seed + ":" + name + ":" + Mathf.RoundToInt((origin.y + height) * 0.1f);
		if (frames.TryGetValue(key, out var cached) && cached.Level != null && Time.unscaledTime < cached.Until) { localOrigin = cached.Level.transform.position; localRotation = cached.Level.transform.rotation; return true; }
		float best = float.PositiveInfinity;
		M_Level selected = null;
		foreach (var branch in WorldLayoutSync.AllBranches()) foreach (var entry in branch.levelTracker) {
			if (!string.IsNullOrEmpty(branchId) && branch.id != branchId) continue;
			var level = entry?.GetLevel();
			if (level == null || !string.Equals(level.levelName, name, StringComparison.Ordinal)) continue;
			if (!string.IsNullOrEmpty(branchId) && level.GetLevelSeed() != seed) continue;
			float distance = Mathf.Abs(level.transform.position.y + localHeight - origin.y - height);
			if (distance < best) { best = distance; selected = level; }
		}
		if (selected == null || string.IsNullOrEmpty(branchId) && best > 200f) return false;
		if (frames.Count > 256) frames.Clear();
		frames[key] = (selected, Time.unscaledTime + 0.2f);
		localOrigin = selected.transform.position; localRotation = selected.transform.rotation;
		return true;
	}
	public static Vector3 MapPosition(Vector3 position, string name, Vector3 origin, Quaternion rotation, float height, out Quaternion delta, string branchId = null, int seed = 0) {
		delta = Quaternion.identity;
		if (LocalFrame(name, origin, height, out var localOrigin, out var localRotation, branchId, seed)) {
			delta = localRotation * Quaternion.Inverse(rotation);
			return localOrigin + delta * (position - origin);
		}
		var player = ENT_Player.GetPlayer();
		if (player != null && CL_GameManager.gMan != null) position.y += height - (CL_GameManager.gMan.GetPlayerCorrectedHeight() - player.transform.position.y);
		return position;
	}
	public static void Remap(ref PlayerData data) {
		if (!data.HasCoordinateFrame) return;
		Vector3 original = data.Position;
		Vector3 mapped = MapPosition(original, data.CoordinateLevel, data.CoordinateOrigin, data.CoordinateRotation, data.CoordinateHeight, out var delta, data.CoordinateBranch, data.CoordinateSeed);
		data.Position = mapped;
		data.Rotation = delta * data.Rotation;
		if (data.HasPhysics) { data.WorldVelocity = delta * data.WorldVelocity; data.AnchorOffset = delta * data.AnchorOffset; }
		data.LeftHand.Position = mapped + delta * (data.LeftHand.Position - original);
		data.RightHand.Position = mapped + delta * (data.RightHand.Position - original);
		if (data.LeftHand.interactState == 2) data.LeftHand.DesiredPosition = mapped + delta * (data.LeftHand.DesiredPosition - original);
		if (data.RightHand.interactState == 2) data.RightHand.DesiredPosition = mapped + delta * (data.RightHand.DesiredPosition - original);
		rotations[data.playId] = delta;
	}
	public static Quaternion RemoteRotation(ulong sender, Quaternion rotation) => rotations.TryGetValue(sender, out var delta) ? delta * rotation : rotation;
	public static Vector3 RemoteDirection(ulong sender, Vector3 direction) => rotations.TryGetValue(sender, out var delta) ? delta * direction : direction;
}
