using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;

namespace WKMPMod.Gameplay;

public sealed class VentCoop : MonoBehaviour {
	private static readonly Dictionary<ulong, VentCoop> entries = new Dictionary<ulong, VentCoop>();
	private static readonly AccessTools.FieldRef<DEN_VentThing, bool> Grabbed = AccessTools.FieldRefAccess<DEN_VentThing, bool>("hasGrabbedTarget");
	private static readonly AccessTools.FieldRef<DEN_VentThing, bool> Retreating = AccessTools.FieldRefAccess<DEN_VentThing, bool>("retreating");
	private static readonly MethodInfo Arms = AccessTools.Method(typeof(DEN_VentThing), "CalculateArms");
	private DEN_VentThing enemy;
	private Rigidbody rigid;
	private bool originalKinematic;
	private ulong id;
	private ulong owner;
	private bool simulated;
	private bool grabbed;
	private bool retreat;
	private bool applying;
	private float nextOwner;
	private float nextPose;
	private float lastPose;
	private Vector3 hand;
	private Quaternion rotation;
	public ulong Key => id;
	public static bool HasOwner(ulong key, ulong player) => entries.TryGetValue(key, out var net) && net != null && net.owner == player;
	public static bool Enabled => MPSteamworks.Instance.IsLan && MPCore.IsInLobby && !UI.ManualSaves.Loading;
	public static VentCoop Get(DEN_VentThing enemy) {
		var net = enemy.GetComponent<VentCoop>() ?? enemy.gameObject.AddComponent<VentCoop>();
		if (net.enemy == null) { net.enemy = enemy; net.id = CoopIdentity.For(enemy.transform); net.rigid = enemy.hand.GetComponent<Rigidbody>(); if (net.rigid != null) net.originalKinematic = net.rigid.isKinematic; entries[net.id] = net; }
		return net;
	}
	public bool Owns() {
		if (!Enabled) { if (rigid != null) rigid.isKinematic = originalKinematic; return true; }
		if (MPSteamworks.IsHost && Time.unscaledTime >= nextOwner) {
			nextOwner = Time.unscaledTime + 0.5f;
			if (CoopGrabAuthority.ActiveMob == id) owner = CoopGrabAuthority.ActiveVictim;
			else if (!grabbed || Time.unscaledTime - lastPose > 2 || !CoopPresence.Alive(owner)) owner = CoopEnemyTargets.Choose(transform.position, enemy.activationDistance, id);
			var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.VentState);
			writer.Put((byte)0).Put(id).Put(owner); MPSteamworks.Instance.Broadcast(writer);
		}
		bool own = owner == MPSteamworks.UserSteamId && (CoopGrabAuthority.ActiveMob == 0 || CoopGrabAuthority.ActiveMob == id);
		if (simulated && !own && Grabbed(enemy)) enemy.DropTarget(false);
		if (own && !simulated && ENT_Player.GetPlayer() != null) enemy.SetTarget(ENT_Player.GetPlayer().transform);
		simulated = own;
		if (rigid != null) rigid.isKinematic = !own || originalKinematic;
		return own;
	}
	private void LateUpdate() {
		if (!Enabled || enemy == null || enemy.dead) return;
		if (Owns()) {
			if (Time.unscaledTime < nextPose) return;
			nextPose = Time.unscaledTime + 0.08f; grabbed = Grabbed(enemy); lastPose = Time.unscaledTime;
			var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.VentState);
			writer.Put((byte)1).Put(id).Put(owner).Put(transform.InverseTransformPoint(enemy.hand.position)).Put(Quaternion.Inverse(transform.rotation) * enemy.hand.rotation).Put(grabbed).Put(Retreating(enemy));
			MPSteamworks.Instance.Broadcast(writer, Steamworks.Data.SendType.Unreliable | Steamworks.Data.SendType.NoNagle);
		} else if (Time.unscaledTime - lastPose < 2f) {
			enemy.hand.position = Vector3.Lerp(enemy.hand.position, transform.TransformPoint(hand), 1 - Mathf.Exp(-25 * Time.unscaledDeltaTime));
			enemy.hand.rotation = Quaternion.Slerp(enemy.hand.rotation, transform.rotation * rotation, 1 - Mathf.Exp(-25 * Time.unscaledDeltaTime));
			enemy.handRenderer.SetActive(true); enemy.handSpriteRenderer.gameObject.SetActive(false);
			var animator = enemy.GetComponent<Animator>(); if (animator != null) animator.SetBool("chomping", grabbed);
			enemy.handAnimator.SetBool("retreating", retreat);
			Arms.Invoke(enemy, null);
		}
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (!Enabled || !MPSteamworks.Instance.HasConnection(sender)) return;
		byte action = reader.GetByte(); ulong id = reader.GetULong();
		if (!entries.TryGetValue(id, out var net) || net == null) return;
		if (action == 0) { ulong owner = reader.GetULong(); if (sender == MPSteamworks.Instance.HostSteamId) net.owner = owner; }
		else if (action == 1) {
			ulong owner = reader.GetULong(); if (sender != owner || owner != net.owner) return;
			net.hand = reader.GetVector3(); net.rotation = reader.GetQuaternion(); net.grabbed = reader.GetBool(); net.retreat = reader.GetBool(); net.lastPose = Time.unscaledTime;
		} else if (action == 2 && net.owner == MPSteamworks.UserSteamId) {
			float amount = reader.GetFloat(); string type = reader.GetString();
			if (!float.IsFinite(amount) || amount < 0 || amount > 1000) return;
			net.applying = true; try { net.enemy.Damage(Damageable.DamageInfo.CreateDamageInfo(amount, type)); } finally { net.applying = false; }
		}
	}
	public static bool Damage(DEN_VentThing enemy, Damageable.DamageInfo info) {
		if (!Enabled || info == null) return true;
		var net = Get(enemy); if (net.applying || net.owner == MPSteamworks.UserSteamId || net.owner == 0) return true;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, net.owner, PacketType.VentState);
		writer.Put((byte)2).Put(net.id).Put(info.amount).Put(info.type); MPSteamworks.Instance.SendToPeer(net.owner, writer); return false;
	}
	private void OnDestroy() { if (entries.TryGetValue(id, out var current) && current == this) entries.Remove(id); }
	private void OnDisable() { if (enemy != null && simulated && Grabbed(enemy)) enemy.DropTarget(false); if (rigid != null) rigid.isKinematic = originalKinematic; simulated = false; }
}

[HarmonyPatch]
public static class Patch_VentCoop {
	private static IEnumerable<MethodBase> TargetMethods() {
		foreach (string name in new[] { "Update", "LateUpdate", "TickUpdate" }) yield return AccessTools.Method(typeof(DEN_VentThing), name);
	}
	private static bool Prefix(DEN_VentThing __instance) => !VentCoop.Enabled || VentCoop.Get(__instance).Owns();
}
[HarmonyPatch(typeof(DEN_VentThing), nameof(DEN_VentThing.Damage))]
public static class Patch_VentCoopDamage { private static bool Prefix(DEN_VentThing __instance, Damageable.DamageInfo info) => VentCoop.Damage(__instance, info); }
