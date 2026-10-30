using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using WKMPMod.Data;
using WKMPMod.NetWork;

namespace WKMPMod.Gameplay;

public static class WorldDoors {
	private static bool applying;
	private static readonly Dictionary<ulong, (bool open, float expires)> pending = new Dictionary<ulong, (bool, float)>();
	private static readonly AccessTools.FieldRef<UT_Door, bool> IsOpen = AccessTools.FieldRefAccess<UT_Door, bool>("open");
	public static bool State(UT_Door door) => IsOpen(door);
	public static void ResetPending() { pending.Clear(); CoopPassageDoor.ResetPending(); }
	public static void Send(UT_Door door, bool before) {
		if (door != null && IsOpen(door) != before) { var motion = door.GetComponent<RemoteDoorMotion>(); if (RemoteWorldEffects.Active) { if (motion == null) motion = door.gameObject.AddComponent<RemoteDoorMotion>(); motion.Replay = true; } else if (motion != null) motion.Replay = false; }
		if (!WorldButtons.Enabled || applying || WorldLayoutSync.ApplyingEvent || WorldButtons.InteractionDepth > 0 || door == null || IsOpen(door) == before) return;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.WorldDoor);
		writer.Put(WKMPMod.UI.ManualSaves.LoadGeneration).Put(CoopIdentity.For(door.transform)).Put(IsOpen(door));
		MPSteamworks.Instance.Broadcast(writer);
	}
	public static void RestoreState(UT_Door door, bool open) {
		CoopDoor.Restored(door);
		if (IsOpen(door) == open) return;
		bool previous = applying; applying = true;
		try { if (open) door.Open(); else door.Close(); } finally { applying = previous; }
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (!WorldButtons.Enabled || WKMPMod.UI.ManualSaves.GroupLoading || !MPSteamworks.Instance.HasConnection(sender)) return;
		if (reader.GetULong() != WKMPMod.UI.ManualSaves.LoadGeneration) return;
		ulong id = reader.GetULong();
		bool open = reader.GetBool();
		if (Apply(id, open)) return;
		if (pending.Count < 256 || pending.ContainsKey(id)) pending[id] = (open, Time.unscaledTime + 300f);
	}
	private static bool Apply(ulong id, bool open) {
		foreach (var door in Object.FindObjectsOfType<UT_Door>()) {
			if (CoopIdentity.For(door.transform) != id) continue;
			if (IsOpen(door) == open) return true;
			applying = true;
			try { if (open) door.Open(); else door.Close(); } finally { applying = false; }
			return true;
		}
		return false;
	}
	public static void Flush() {
		if (!WorldButtons.Enabled) { pending.Clear(); return; }
		if (pending.Count == 0) return;
		foreach (var door in Object.FindObjectsOfType<UT_Door>()) {
			ulong id = CoopIdentity.For(door.transform);
			if (!pending.TryGetValue(id, out var state)) continue;
			applying = true;
			try { if (IsOpen(door) != state.open) { if (state.open) door.Open(); else door.Close(); } } finally { applying = false; }
			pending.Remove(id);
		}
		foreach (var pair in new List<KeyValuePair<ulong, (bool open, float expires)>>(pending)) if (pair.Value.expires < Time.unscaledTime) pending.Remove(pair.Key);
	}
}

[HarmonyPatch(typeof(UT_Door), nameof(UT_Door.Open))]
public static class Patch_SyncDoorOpen {
	[HarmonyPriority(Priority.First)]
	private static void Prefix(UT_Door __instance, out bool __state) { __state = WorldDoors.State(__instance); }
	private static void Postfix(UT_Door __instance, bool __state) { WorldDoors.Send(__instance, __state); }
}

[HarmonyPatch(typeof(UT_Door), nameof(UT_Door.Close))]
public static class Patch_SyncDoorClose {
	[HarmonyPriority(Priority.First)]
	private static void Prefix(UT_Door __instance, out bool __state) { __state = WorldDoors.State(__instance); }
	private static void Postfix(UT_Door __instance, bool __state) { WorldDoors.Send(__instance, __state); }
}
