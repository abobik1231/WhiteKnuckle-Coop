using HarmonyLib;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;

namespace WKMPMod.Gameplay;

public sealed class CoopGrabAuthority : MonoBehaviour {
	public enum Decision { Waiting, Granted, Denied }
	private static readonly GrabLease lease = new GrabLease();
	private static ulong session;
	private static ulong knownMob;
	private static ulong knownVictim;
	private static float knownUntil = -100;
	private static float nextRequest;
	private static float nextBroadcast;
	private static ulong requestedMob;
	private static readonly AccessTools.FieldRef<Barnacle_Tongue_Core, Barnacle_Tongue_Interface_Module_Basic> Module = AccessTools.FieldRefAccess<Barnacle_Tongue_Core, Barnacle_Tongue_Interface_Module_Basic>("barnacleInterface");
	public static bool Enabled => MPCore.IsInLobby && MPSteamworks.Instance.IsLan && !UI.ManualSaves.Loading;
	public static ulong ActiveMob => !Enabled ? 0 : MPSteamworks.IsHost ? lease.Mob : Time.unscaledTime < knownUntil ? knownMob : 0;
	public static ulong ActiveVictim => ActiveMob == 0 ? 0 : MPSteamworks.IsHost ? lease.Victim : knownVictim;
	public static bool LocalPulled => ActiveVictim != 0 && ActiveVictim == MPSteamworks.UserSteamId;
	public static void Reset() { lease.Clear(); knownMob = 0; knownVictim = 0; knownUntil = -100; nextRequest = 0; nextBroadcast = 0; requestedMob = 0; }
	private static void Prepare() { ulong id = MPSteamworks.Instance.IsLan ? MPSteamworks.Instance.HostSteamId : 0; if (id != session) { session = id; Reset(); } }
	private void Update() {
		Prepare();
		if (!Enabled || !MPSteamworks.IsHost) return;
		bool lost = lease.Victim != 0 && (lease.Victim == MPSteamworks.UserSteamId ? ENT_Player.GetPlayer() == null || ENT_Player.GetPlayer().IsDead() : !MPSteamworks.Instance.HasConnection(lease.Victim) || !CoopPresence.Alive(lease.Victim));
		if (lost) lease.Clear();
		lease.Expire(Time.unscaledTime);
		if (Time.unscaledTime >= nextBroadcast) Broadcast();
	}
	private static void Broadcast() {
		if (!Enabled || !MPSteamworks.IsHost) return;
		nextBroadcast = Time.unscaledTime + 0.4f;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.GrabAuthority);
		writer.Put((byte)1).Put(lease.Mob).Put(lease.Victim).Put((float)System.Math.Max(0, lease.Expires - Time.unscaledTime)); MPSteamworks.Instance.Broadcast(writer);
	}
	public static Decision Request(ulong mob) {
		if (!Enabled) return Decision.Granted;
		Prepare();
		if (MPSteamworks.IsHost) {
			bool changed = lease.Mob == 0;
			if (!lease.Acquire(mob, MPSteamworks.UserSteamId, Time.unscaledTime)) return Decision.Denied;
			if (changed) Broadcast(); return Decision.Granted;
		}
		if (ActiveMob != 0 && (ActiveMob != mob || ActiveVictim != MPSteamworks.UserSteamId)) return Decision.Denied;
		if (Time.unscaledTime >= nextRequest || requestedMob != mob) {
			nextRequest = Time.unscaledTime + 0.25f; requestedMob = mob;
			var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPSteamworks.Instance.HostSteamId, PacketType.GrabAuthority);
			writer.Put((byte)0).Put(mob); MPSteamworks.Instance.SendToHost(writer);
		}
		return ActiveMob == mob && ActiveVictim == MPSteamworks.UserSteamId ? Decision.Granted : Decision.Waiting;
	}
	public static void Release(ulong mob) {
		if (!Enabled || mob == 0) return;
		if (MPSteamworks.IsHost) { if (lease.Release(mob, MPSteamworks.UserSteamId)) Broadcast(); }
		else {
			bool owns = knownMob == mob && knownVictim == MPSteamworks.UserSteamId;
			if (!owns && requestedMob != mob) return;
			if (requestedMob == mob) requestedMob = 0;
			if (owns) { knownMob = 0; knownVictim = 0; knownUntil = -100; }
			var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPSteamworks.Instance.HostSteamId, PacketType.GrabAuthority);
			writer.Put((byte)2).Put(mob); MPSteamworks.Instance.SendToHost(writer);
		}
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (!Enabled || !MPSteamworks.Instance.HasConnection(sender)) return;
		Prepare(); byte action = reader.GetByte(); ulong mob = reader.GetULong();
		if (action == 0 && MPSteamworks.IsHost) {
			if (VentCoop.HasOwner(mob, sender) || BarnacleNet.HasOwner(mob, sender)) lease.Acquire(mob, sender, Time.unscaledTime);
			Broadcast();
		} else if (action == 1 && !MPSteamworks.IsHost && sender == MPSteamworks.Instance.HostSteamId) {
			knownMob = mob; knownVictim = reader.GetULong();
			float remaining = reader.GetFloat();
			knownUntil = Time.unscaledTime + Mathf.Clamp(remaining - MPSteamworks.Instance.LanOneWayLatency(sender) * 2f - 0.05f, 0, 1.2f);
		}
		else if (action == 2 && MPSteamworks.IsHost && lease.Release(mob, sender)) Broadcast();
	}
	public static bool VentPull(DEN_VentThing enemy) {
		if (!Enabled) return true;
		ulong key = VentCoop.Get(enemy).Key;
		var decision = Request(key);
		if (decision == Decision.Denied) enemy.DropTarget(false);
		else if (decision == Decision.Waiting) { var player = ENT_Player.GetPlayer(); if (player != null) { player.SetGrappled(false, enemy); player.SetGravity(true); player.SetGravityMult(1); } }
		return decision == Decision.Granted;
	}
	public static bool BarnaclePull(Barnacle_Tongue_Basic tongue) {
		if (!Enabled || tongue.GetGrabbed() != ENT_Player.GetPlayer()) return true;
		var barnacle = Module(tongue)?.GetBarnacle(); if (barnacle == null) return true;
		var decision = Request(BarnacleNet.Get(barnacle).Key);
		if (decision == Decision.Denied) tongue.Retract();
		else if (decision == Decision.Waiting) ENT_Player.GetPlayer()?.SetGrappled(false, barnacle);
		return decision == Decision.Granted;
	}
	public static void BarnacleReleased(Barnacle_Tongue_Basic tongue) {
		if (!Enabled) return;
		var module = Module(tongue); var barnacle = module?.GetBarnacle();
		if (barnacle != null && !module.IsPulling()) Release(BarnacleNet.Get(barnacle).Key);
	}
}

[HarmonyPatch(typeof(DEN_VentThing), nameof(DEN_VentThing.PullTarget))]
public static class Patch_VentGrabLease { private static bool Prefix(DEN_VentThing __instance) => CoopGrabAuthority.VentPull(__instance); }
[HarmonyPatch(typeof(DEN_VentThing), nameof(DEN_VentThing.DropTarget))]
public static class Patch_VentGrabRelease { private static void Postfix(DEN_VentThing __instance) { if (CoopGrabAuthority.Enabled) CoopGrabAuthority.Release(VentCoop.Get(__instance).Key); } }
[HarmonyPatch(typeof(Barnacle_Tongue_Basic), "PullTarget")]
public static class Patch_BarnacleGrabLease { private static bool Prefix(Barnacle_Tongue_Basic __instance) => CoopGrabAuthority.BarnaclePull(__instance); }
[HarmonyPatch(typeof(Barnacle_Tongue_Basic), nameof(Barnacle_Tongue_Basic.Retract))]
public static class Patch_BarnacleGrabRelease { private static void Postfix(Barnacle_Tongue_Basic __instance) => CoopGrabAuthority.BarnacleReleased(__instance); }
[HarmonyPatch(typeof(Barnacle_Tongue_Basic), nameof(Barnacle_Tongue_Basic.KillTongue))]
public static class Patch_BarnacleGrabBreak { private static void Postfix(Barnacle_Tongue_Basic __instance) => CoopGrabAuthority.BarnacleReleased(__instance); }
