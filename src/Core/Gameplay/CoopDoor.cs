using System.Linq;
using HarmonyLib;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.NetWork;
using WKMPMod.RemotePlayer;
using WKMPMod.UI;

namespace WKMPMod.Gameplay;

public sealed class CoopDoor : MonoBehaviour {
	private UT_Door door;
	private Vector3 center;
	private Vector3 normal;
	private float thickness;
	private int destination;
	private bool initialized;
	private bool waiting;
	private bool restored;
	private bool traversal;
	private float nextCheck;
	private Transform frame;
	private Vector3 localCenter;
	private Vector3 localNormal;
	public static bool Cleared(float center, float plane, float halfHeight) => center - halfHeight >= plane + .12f;
	private static bool Enabled => MPCore.IsInLobby && MPSteamworks.Instance.IsLan;
	private static CoopDoor Guard(UT_Door target) { var guard = target.GetComponent<CoopDoor>() ?? target.gameObject.AddComponent<CoopDoor>(); guard.Initialize(target); return guard; }
	public static void Restored(UT_Door target) { if (target.GetComponentInParent<M_Level>(true) != null) Guard(target).restored = true; }
	public static bool BeforeClose(UT_Door target) {
		if (!Enabled || ManualSaves.Loading || !WorldDoors.State(target) || target.GetComponentInParent<M_Level>(true) == null) return true;
		var guard = Guard(target);
		if (!ManualSaves.GroupLoading && guard.AllForward()) { guard.waiting = false; return true; }
		guard.waiting = true; return false;
	}
	private void Initialize(UT_Door target) {
		if (initialized) return;
		initialized = true; door = target;
		Vector3 start = (Vector3)AccessTools.Field(typeof(UT_Door), "startPos").GetValue(target);
		Vector3 root = target.space == UT_Door.UseSpace.World || target.transform.parent == null ? start : target.transform.parent.TransformPoint(start);
		var collider = target.GetComponentsInChildren<BoxCollider>(true).Where(item => !item.isTrigger).OrderByDescending(item => item.size.sqrMagnitude).FirstOrDefault();
		if (collider != null) {
			Vector3 size = Vector3.Scale(collider.size, collider.transform.lossyScale); size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
			Vector3 axis = size.y <= size.x && size.y <= size.z ? Vector3.up : size.x <= size.z ? Vector3.right : Vector3.forward;
			normal = collider.transform.TransformDirection(axis).normalized; thickness = Mathf.Min(size.x, size.y, size.z) * .5f;
			center = collider.transform.TransformPoint(collider.center) + root - target.transform.position;
		} else {
			var renderer = target.GetComponentInChildren<Renderer>(true); var bounds = renderer != null ? renderer.bounds : new Bounds(root, Vector3.one);
			normal = bounds.size.y <= bounds.size.x && bounds.size.y <= bounds.size.z ? Vector3.up : bounds.size.x <= bounds.size.z ? Vector3.right : Vector3.forward; center = bounds.center + root - target.transform.position;
		}
		var level = target.GetComponentInParent<M_Level>(true); if (level == null) return;
		var entrance = level.GetLevelEntrance(); var exit = level.GetLevelExit();
		float progress = Vector3.Dot(exit.position - entrance.position, normal);
		if (Mathf.Abs(progress) < .3f) progress = Vector3.Dot(exit.position - center, normal);
		if (Mathf.Abs(progress) >= .3f) destination = progress > 0 ? 1 : -1;
		traversal = Vector3.Distance(center, entrance.position) < 18 || Vector3.Distance(center, exit.position) < 18;
		foreach (var zone in level.GetComponentsInChildren<UT_TriggerZone>(true)) {
			if (zone.onEnter == null) continue;
			bool closes = false;
			for (int i = 0; i < zone.onEnter.GetPersistentEventCount(); i++) if (zone.onEnter.GetPersistentTarget(i) == target && zone.onEnter.GetPersistentMethodName(i) == nameof(UT_Door.Close)) closes = true;
			if (!closes) continue;
			float side = Vector3.Dot(zone.transform.position - center, normal); if (Mathf.Abs(side) > .4f) { destination = side > 0 ? 1 : -1; traversal = true; break; }
		}
		frame = level.transform; localCenter = frame.InverseTransformPoint(center); localNormal = frame.InverseTransformDirection(normal);
	}
	public bool ClearAt(Vector3 position, float halfHeight, float radius) {
		if (frame != null) { center = frame.TransformPoint(localCenter); normal = frame.TransformDirection(localNormal).normalized; }
		if (destination == 0) return false;
		float vertical = Mathf.Abs(normal.y); float extent = vertical * halfHeight + radius * (1 - vertical) + thickness + .2f;
		return destination * Vector3.Dot(position - center, normal) > extent;
	}
	private bool AllForward() {
		var player = ENT_Player.GetPlayer();
		if (player == null || player.IsDead() || !ClearAt(PlayerChain.LocalAnchor(player), player.cCon != null ? player.cCon.height * .5f : 1.1f, .45f)) return false;
		foreach (ulong id in MPSteamworks.Instance.ConnectedPeerIds) {
			if (!CoopPresence.Alive(id)) return false;
			if (!RPManager.Instance.Players.TryGetValue(id, out var peer) || peer.PlayerObject == null || !peer.PlayerObject.activeInHierarchy) return false;
			var body = peer.PlayerObject.GetComponentInChildren<WKMPMod.Component.RemotePlayer>(true);
			if (body == null || !body.HasSnapshot || Time.unscaledTime - body.LastReceivedTime > .75f || !ClearAt(body.NetworkPosition + body.AnchorOffset, 1.1f, .45f)) return false;
		}
		return true;
	}
	private void Update() {
		if (!Enabled || door == null || ManualSaves.Loading || ManualSaves.GroupLoading || Time.unscaledTime < nextCheck) return;
		if (frame != null) { center = frame.TransformPoint(localCenter); normal = frame.TransformDirection(localNormal).normalized; }
		nextCheck = Time.unscaledTime + .1f;
		if (waiting) { if (AllForward()) { waiting = false; restored = false; door.Close(); } return; }
		if (!restored || !traversal || WorldDoors.State(door)) return;
		var player = ENT_Player.GetPlayer(); if (player == null || player.IsDead() || Vector3.Distance(player.transform.position, center) > 25) return;
		if (!AllForward()) { waiting = true; door.Open(); }
		restored = false;
	}
}
