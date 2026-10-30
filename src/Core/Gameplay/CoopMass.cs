using HarmonyLib;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;

namespace WKMPMod.Gameplay;

public sealed class CoopMass : MonoBehaviour {
	private static float height;
	private static float received = -100;
	private static bool active;
	private static float speed;
	private float nextSend;
	public static void Reset() { received = -100; }
	private static readonly AccessTools.FieldRef<DEN_DeathFloor, bool> Active = AccessTools.FieldRefAccess<DEN_DeathFloor, bool>("active");
	private static readonly AccessTools.FieldRef<DEN_DeathFloor, float> FrameSpeed = AccessTools.FieldRefAccess<DEN_DeathFloor, float>("speedMultFrameStorage");
	private void LateUpdate() {
		if (!WorldLayoutSync.Enabled || !MPCore.CanSync || !MPSteamworks.IsHost || Time.unscaledTime < nextSend || DEN_DeathFloor.instance == null) return;
		nextSend = Time.unscaledTime + 0.1f;
		var floor = DEN_DeathFloor.instance;
		var player = ENT_Player.GetPlayer(); if (player == null) return;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.WorldStateSync);
		writer.Put(floor.transform.position.y + CL_GameManager.gMan.GetPlayerCorrectedHeight() - player.transform.position.y).Put(floor.IsActive()).Put(floor.speed);
		MPSteamworks.Instance.Broadcast(writer, Steamworks.Data.SendType.Unreliable | Steamworks.Data.SendType.NoNagle);
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (!WorldLayoutSync.Enabled || MPSteamworks.IsHost || sender != MPSteamworks.Instance.HostSteamId) return;
		float value = reader.GetFloat(); bool enabled = reader.GetBool(); float movement = reader.GetFloat();
		if (!float.IsFinite(value) || !float.IsFinite(movement) || Mathf.Abs(value) > 10000000 || movement < 0 || movement > 10000) return;
		height = value; active = enabled; speed = movement; received = Time.unscaledTime;
	}
	public static bool Before(DEN_DeathFloor floor, out float original) {
		original = float.NaN;
		if (!WorldLayoutSync.Enabled || MPSteamworks.IsHost) return true;
		if (UI.ManualSaves.Loading) return false;
		var player = ENT_Player.GetPlayer();
		if (player == null || Time.unscaledTime - received > 3) return false;
		var position = player.transform.position;
		position.y = height - (CL_GameManager.gMan.GetPlayerCorrectedHeight() - position.y);
		floor.transform.position = position; floor.speed = speed; Active(floor) = active;
		original = FrameSpeed(floor); FrameSpeed(floor) = 0;
		return true;
	}
	public static void After(DEN_DeathFloor floor, float original) { if (!float.IsNaN(original)) FrameSpeed(floor) = original; }
}

[HarmonyPatch(typeof(DEN_DeathFloor), nameof(DEN_DeathFloor.Update))]
public static class Patch_CoopMass {
	private static bool Prefix(DEN_DeathFloor __instance, out float __state) => CoopMass.Before(__instance, out __state);
	private static void Postfix(DEN_DeathFloor __instance, float __state) => CoopMass.After(__instance, __state);
}
