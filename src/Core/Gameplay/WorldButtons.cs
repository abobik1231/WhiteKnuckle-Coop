using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.UI;

namespace WKMPMod.Gameplay;

public static class WorldButtons {
	private static bool applying;
	public static int InteractionDepth;
	private static readonly Dictionary<ulong, (int count, float expires)> pending = new Dictionary<ulong, (int, float)>();
	public static bool Enabled => MPCore.CanSync && MPSteamworks.Instance.IsLan && !ManualSaves.Loading && !ManualSaves.GroupLoading;
	public static bool Personal(CL_Button button) {
		if (button.GetComponentInParent<ENV_VendingMachine>(true) != null || button.GetComponentInParent<OS_Manager>(true) != null) return true;
		for (int i = 0; button.activeEvent != null && i < button.activeEvent.GetPersistentEventCount(); i++) { var target = button.activeEvent.GetPersistentTarget(i); if (target is UT_PlayerTakeOver || target is OS_Manager) return true; }
		return false;
	}
	public static void Send(CL_Button button, bool accepted) {
		if (!Enabled || applying || WorldLayoutSync.ApplyingEvent || !accepted || button == null || Personal(button)) return;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.WorldButton);
		writer.Put(ManualSaves.LoadGeneration).Put(CoopIdentity.For(button.transform));
		MPSteamworks.Instance.Broadcast(writer);
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (!Enabled || !MPSteamworks.Instance.HasConnection(sender)) return;
		if (reader.GetULong() != ManualSaves.LoadGeneration) return;
		ulong id = reader.GetULong();
		if (Apply(id)) return;
		if (pending.Count >= 256 && !pending.ContainsKey(id)) return;
		pending.TryGetValue(id, out var previous);
		pending[id] = (Math.Min(previous.count + 1, 8), Time.unscaledTime + 300f);
	}
	private static bool Apply(ulong id) {
		foreach (var button in UnityEngine.Object.FindObjectsOfType<CL_Button>()) {
			if (CoopIdentity.For(button.transform) != id || !button.CanInteract()) continue;
			ApplyButton(button);
			return true;
		}
		return false;
	}
	private static void ApplyButton(CL_Button button) { applying = true; using var scope = new RemoteWorldEffects(); try { button.Interact(); } finally { applying = false; } }
	public static void Flush() {
		if (!Enabled) { pending.Clear(); return; }
		if (pending.Count == 0) return;
		foreach (var button in UnityEngine.Object.FindObjectsOfType<CL_Button>()) {
			ulong id = CoopIdentity.For(button.transform);
			if (!pending.TryGetValue(id, out var state) || !button.CanInteract()) continue;
			ApplyButton(button);
			if (state.count <= 1) pending.Remove(id); else pending[id] = (state.count - 1, state.expires);
		}
		foreach (var pair in new List<KeyValuePair<ulong, (int count, float expires)>>(pending)) if (pair.Value.expires < Time.unscaledTime) pending.Remove(pair.Key);
	}
}

public sealed class WorldMechanismQueue : MonoBehaviour {
	private float nextFlush;
	private void Update() {
		CoopPassageDoor.Flush();
		if (!WorldButtons.Enabled) { WorldButtons.Flush(); WorldDoors.Flush(); return; }
		if (Time.unscaledTime < nextFlush) return;
		nextFlush = Time.unscaledTime + 0.5f;
		CoopWorldActions.Flush();
		WorldButtons.Flush(); WorldDoors.Flush();
	}
}

[HarmonyPatch(typeof(CL_Button), nameof(CL_Button.Interact), new Type[] { })]
public static class Patch_WorldButton {
	private static void Prefix(CL_Button __instance, out bool __state) { __state = __instance.CanInteract(); WorldButtons.InteractionDepth++; }
	private static void Postfix(CL_Button __instance, bool __state) { WorldButtons.Send(__instance, __state); }
	private static System.Exception Finalizer(System.Exception __exception) { WorldButtons.InteractionDepth = System.Math.Max(0, WorldButtons.InteractionDepth - 1); return __exception; }
}

