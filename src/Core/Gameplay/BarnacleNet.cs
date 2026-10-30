using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.RemotePlayer;
using WKMPMod.UI;

namespace WKMPMod.Gameplay;

public sealed class BarnacleNet : MonoBehaviour {
	private static readonly Dictionary<ulong, BarnacleNet> entries = new Dictionary<ulong, BarnacleNet>();
	public static bool Applying;
	public static int ReceivedDamageCount { get; private set; }
	public ulong Key => key;
	public static bool HasOwner(ulong id, ulong player) => entries.TryGetValue(id, out var net) && net != null && net.owner == player;
	private DEN_Barnacle barnacle;
	private Barnacle_Tongue_Interface_Module_Basic module;
	private ulong key;
	private ulong owner;
	private float nextSend;
	private float nextOwner;
	private float lastPose;
	private bool grabbed;
	private bool wasOwner;
	private float nextRetarget;
	private readonly List<bool> originalAim = new List<bool>();
	private readonly Dictionary<int, Vector3> endpoints = new Dictionary<int, Vector3>();
	public static bool Enabled => MPCore.IsInLobby && MPSteamworks.Instance.IsLan && !ManualSaves.Loading;
	public static BarnacleNet Get(DEN_Barnacle entity) {
		var net = entity.GetComponent<BarnacleNet>() ?? entity.gameObject.AddComponent<BarnacleNet>();
		if (net.barnacle == null) {
			net.barnacle = entity;
			net.module = entity.tongueInterface as Barnacle_Tongue_Interface_Module_Basic;
			if (net.module?.tongues != null) foreach (var tongue in net.module.tongues) net.originalAim.Add(tongue.aimForPlayer);
			net.key = CoopIdentity.For(entity.transform);
			entries[net.key] = net;
		}
		return net;
	}
	public bool OwnsUpdate() {
		if (module?.tongues == null) return true;
		if (MPSteamworks.IsHost && Time.unscaledTime >= nextOwner) {
			nextOwner = Time.unscaledTime + 0.5f;
			bool ownerAlive = owner == MPSteamworks.UserSteamId ? ENT_Player.GetPlayer() != null && !ENT_Player.GetPlayer().IsDead() : MPSteamworks.Instance.HasConnection(owner) && CoopPresence.Alive(owner);
			if (!ownerAlive || (!grabbed && Time.unscaledTime >= nextRetarget)) { owner = CoopEnemyTargets.Choose(transform.position, module.scanDistance, key); nextRetarget = Time.unscaledTime + 3; }
			var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.BarnacleState);
			writer.Put((byte)0).Put(key).Put(owner);
			MPSteamworks.Instance.Broadcast(writer);
		}
		bool currentOwner = owner != 0 && owner == MPSteamworks.UserSteamId && (CoopGrabAuthority.ActiveMob == 0 || CoopGrabAuthority.ActiveMob == key);
		if (wasOwner && !currentOwner) module.ReleaseGrapple();
		wasOwner = currentOwner;
		if (currentOwner) foreach (var tongue in module.tongues) tongue.aimForPlayer = true;
		return currentOwner;
	}
	private ulong Closest() {
		ulong best = MPSteamworks.Instance.HostSteamId;
		float distance = float.MaxValue;
		var player = ENT_Player.GetPlayer();
		if (player != null && !player.IsDead()) { best = MPSteamworks.UserSteamId; distance = (player.transform.position - transform.position).sqrMagnitude; }
		foreach (var pair in RPManager.Instance.Players) {
			var remote = pair.Value.PlayerObject;
			if (remote == null || !remote.activeInHierarchy || !CoopPresence.Alive(pair.Key)) continue;
			float candidate = (remote.transform.position - transform.position).sqrMagnitude;
			if (candidate < distance - 0.25f || Mathf.Abs(candidate - distance) < 0.25f && pair.Key < best) { best = pair.Key; distance = candidate; }
		}
		return best;
	}
	private void LateUpdate() {
		if (module?.tongues == null) return;
		if (!Enabled) { for (int i = 0; i < originalAim.Count && i < module.tongues.Count; i++) module.tongues[i].aimForPlayer = originalAim[i]; return; }
		if (barnacle.dead) return;
		if (owner == MPSteamworks.UserSteamId) {
			if (Time.unscaledTime < nextSend) return;
			nextSend = Time.unscaledTime + 0.08f;
			grabbed = module.tongues.Any(tongue => tongue.IsAiming() || tongue.GetState() == Barnacle_Tongue_Core.TongueState.grabbing || tongue.GetState() == Barnacle_Tongue_Core.TongueState.reaching);
			lastPose = Time.unscaledTime;
			var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.BarnacleState);
			writer.Put((byte)1).Put(key).Put(owner).Put((byte)Mathf.Min(module.tongues.Count, 64));
			for (int i = 0; i < module.tongues.Count && i < 64; i++) {
				var tongue = module.tongues[i];
				writer.Put((byte)tongue.GetState());
				writer.Put(transform.InverseTransformPoint((Vector3)AccessTools.Field(typeof(Barnacle_Tongue_Basic), "reachPoint").GetValue(tongue)));
				writer.Put(tongue.tongue.gameObject.activeSelf);
				writer.Put(tongue.IsAiming());
			}
			MPSteamworks.Instance.Broadcast(writer, Steamworks.Data.SendType.Unreliable | Steamworks.Data.SendType.NoNagle);
		} else {
			foreach (var endpoint in endpoints) {
				if (endpoint.Key >= module.tongues.Count) continue;
				var tongue = module.tongues[endpoint.Key];
				var visual = tongue.tongue.GetChild(0);
				Vector3 point = Vector3.Lerp(visual.position + visual.forward * visual.localScale.z, endpoint.Value, 1 - Mathf.Exp(-25 * Time.unscaledDeltaTime));
				Vector3 difference = point - visual.position;
				if (difference.sqrMagnitude > 0.0001f) visual.rotation = Quaternion.LookRotation(difference);
				visual.localScale = new Vector3(1, 1, difference.magnitude);
			}
		}
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (!Enabled || !MPSteamworks.Instance.HasConnection(sender)) return;
		byte action = reader.GetByte();
		ulong id = reader.GetULong();
		ulong authority = reader.GetULong();
		if (!entries.TryGetValue(id, out var net) || net == null) return;
		if (action == 0) { if (sender == MPSteamworks.Instance.HostSteamId) net.owner = authority; return; }
		if (sender != authority || authority != net.owner || net.module == null) return;
		net.lastPose = Time.unscaledTime;
		net.grabbed = false;
		byte count = reader.GetByte();
		for (int i = 0; i < count; i++) {
			var state = (Barnacle_Tongue_Core.TongueState)reader.GetByte();
			Vector3 point = net.transform.TransformPoint(reader.GetVector3());
			bool active = reader.GetBool();
			bool aiming = reader.GetBool();
			if (i >= net.module.tongues.Count) continue;
			net.grabbed |= aiming || state == Barnacle_Tongue_Core.TongueState.grabbing || state == Barnacle_Tongue_Core.TongueState.reaching;
			net.endpoints[i] = point;
			net.module.tongues[i].tongue.gameObject.SetActive(active);
		}
	}
	public static bool Damage(UT_Damage damage, Damageable.DamageInfo info) {
		if (!Enabled || Applying || info == null) return true;
		var net = damage.GetComponentInParent<BarnacleNet>();
		if (net?.module?.tongues == null || net.owner == 0 || net.owner == MPSteamworks.UserSteamId) return true;
		for (int i = 0; i < net.module.tongues.Count; i++) {
			var tongue = net.module.tongues[i];
			int index = tongue.tongueDamage.IndexOf(damage);
			if (index < 0) continue;
			var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, net.owner, PacketType.BarnacleDamage);
			writer.Put(net.key).Put(i).Put(index).Put(info.amount).Put(info.type);
			MPSteamworks.Instance.SendToPeer(net.owner, writer);
			return false;
		}
		return true;
	}
	public static void ReceiveDamage(ulong sender, DataReader reader) {
		if (!Enabled || !MPSteamworks.Instance.HasConnection(sender)) return;
		ulong id = reader.GetULong();
		int tongue = reader.GetInt();
		int part = reader.GetInt();
		float amount = reader.GetFloat();
		string type = reader.GetString();
		if (!entries.TryGetValue(id, out var net) || net == null || net.owner != MPSteamworks.UserSteamId || net.module == null || tongue < 0 || tongue >= net.module.tongues.Count || float.IsNaN(amount) || amount <= 0 || amount > 1000) return;
		var parts = net.module.tongues[tongue].tongueDamage;
		if (part < 0 || part >= parts.Count) return;
		Applying = true;
		ReceivedDamageCount++;
		try { parts[part].Damage(Damageable.DamageInfo.CreateDamageInfo(amount, type, new List<string>())); }
		finally { Applying = false; }
	}
	private void OnDestroy() { if (entries.TryGetValue(key, out var current) && current == this) entries.Remove(key); }
}
