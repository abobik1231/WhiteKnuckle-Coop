using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using WKMPMod.Component;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.RemotePlayer;
using WKMPMod.UI;

namespace WKMPMod.Gameplay;

public sealed class CoopPassageDoor : MonoBehaviour {
	private static readonly List<CoopPassageDoor> guards = new List<CoopPassageDoor>();
	private UT_Toggle toggle;
	private Transform frame;
	private Vector3 localCenter;
	private Vector3 localNormal;
	private float halfThickness;
	private bool pending;
	private bool applying;
	private bool directionKnown;
	private float next;
	public bool CloseState { get; private set; }
	public Vector3 Center => frame.TransformPoint(localCenter);
	public Vector3 Forward => frame.TransformDirection(localNormal).normalized;
	public UT_Toggle Toggle => toggle;
	private static bool Enabled => MPCore.IsInLobby && MPSteamworks.Instance.IsLan;
	public static void Register(M_Level level) {
		foreach (var trigger in level.GetComponentsInChildren<UT_TriggerZone>(true)) {
			if (trigger.onEnter == null || trigger.name.IndexOf("close", StringComparison.OrdinalIgnoreCase) < 0 || trigger.entityTags?.Contains("Player") != true) continue;
			for (int i = 0; i < trigger.onEnter.GetPersistentEventCount(); i++) {
				if (trigger.onEnter.GetPersistentTarget(i) is not UT_Toggle target || target.GetComponent<CoopPassageDoor>() != null) continue;
				string method = trigger.onEnter.GetPersistentMethodName(i); bool close;
				if (method == nameof(UT_Toggle.Deactivate)) close = false;
				else if (method == nameof(UT_Toggle.Activate)) close = true;
				else if (method == nameof(UT_Toggle.SetState)) close = JObject.Parse(JsonUtility.ToJson(trigger))["onEnter"]?["m_PersistentCalls"]?["m_Calls"]?[i]?["m_Arguments"]?["m_BoolArgument"]?.Value<bool>() ?? false;
				else continue;
				var guard = target.gameObject.AddComponent<CoopPassageDoor>(); guard.Initialize(target, trigger, level, close);
			}
		}
		foreach (var target in level.GetComponentsInChildren<UT_Toggle>(true)) {
			if (target.GetComponent<CoopPassageDoor>() != null || target.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) < 0 || target.GetComponentInChildren<Animator>(true) == null) continue;
			var data = JObject.Parse(JsonUtility.ToJson(target)); bool? close = null;
			foreach (var pair in new[] { ("activateEvent", true), ("deactivateEvent", false) }) foreach (var call in data[pair.Item1]?["m_PersistentCalls"]?["m_Calls"] ?? new JArray()) {
				string value = (string)call["m_Arguments"]?["m_StringArgument"]; if (value == null) continue; var parts = value.Split(':');
				if (parts.Length != 2 || !bool.TryParse(parts[1], out bool state)) continue;
				if (parts[0].IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0) close = state ? !pair.Item2 : pair.Item2;
				else if (parts[0].IndexOf("clos", StringComparison.OrdinalIgnoreCase) >= 0) close = state ? pair.Item2 : !pair.Item2;
			}
			if (close.HasValue) target.gameObject.AddComponent<CoopPassageDoor>().Initialize(target, null, level, close.Value);
		}
	}
	private void Initialize(UT_Toggle target, UT_TriggerZone trigger, M_Level level, bool close) {
		guards.Add(this);
		toggle = target; frame = level.transform; CloseState = close;
		Vector3 center = target.transform.position; Vector3 normal = target.transform.forward;
		var box = target.GetComponentsInChildren<BoxCollider>(true).Where(collider => !collider.isTrigger).OrderByDescending(collider => collider.size.sqrMagnitude).FirstOrDefault();
		if (box != null) {
			Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale); size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
			Vector3 axis = size.y <= size.x && size.y <= size.z ? Vector3.up : size.x <= size.z ? Vector3.right : Vector3.forward;
			normal = box.transform.TransformDirection(axis).normalized; center = box.transform.TransformPoint(box.center); halfThickness = Mathf.Min(size.x, size.y, size.z) * .5f;
		}
		var triggerBox = trigger != null ? trigger.GetComponent<BoxCollider>() : null; Vector3 triggerPoint = triggerBox != null ? triggerBox.transform.TransformPoint(triggerBox.center) : trigger != null ? trigger.transform.position : level.GetLevelExit().position;
		float goal = Vector3.Dot(triggerPoint - center, normal);
		if (Mathf.Abs(goal) < .2f) goal = Vector3.Dot(level.GetLevelExit().position - level.GetLevelEntrance().position, normal);
		directionKnown = Mathf.Abs(goal) >= .2f;
		if (goal < 0) normal = -normal;
		localCenter = frame.InverseTransformPoint(center); localNormal = frame.InverseTransformDirection(normal);
	}
	public bool AllPassed() {
		if (!directionKnown) return false;
		var local = ENT_Player.GetPlayer(); if (local == null || local.IsDead() || !Clear(PlayerChain.LocalAnchor(local), local.cCon != null ? local.cCon.height * .5f : 1.1f, .45f)) return false;
		foreach (ulong peer in MPSteamworks.Instance.ConnectedPeerIds) {
			if (!CoopPresence.Alive(peer) || !RPManager.Instance.Players.TryGetValue(peer, out var container) || container.PlayerObject == null || !container.PlayerObject.activeInHierarchy) return false;
			var body = container.PlayerObject.GetComponentInChildren<WKMPMod.Component.RemotePlayer>(true);
			if (body == null || !body.HasSnapshot || Time.unscaledTime - body.LastReceivedTime > .75f || !Clear(body.NetworkPosition + body.AnchorOffset, 1.1f, .45f)) return false;
		}
		return true;
	}
	private bool Clear(Vector3 position, float halfHeight, float radius) { var axis = Forward; float vertical = Mathf.Abs(axis.y); return Vector3.Dot(position - Center, axis) > halfThickness + .2f + vertical * halfHeight + (1 - vertical) * radius; }
	public bool Before(bool state) {
		if (applying || !Enabled || ManualSaves.Loading || state != CloseState || toggle.active == state) return true;
		if (!ManualSaves.GroupLoading && AllPassed()) { pending = false; return true; }
		pending = true; return false;
	}
	public void Changed(bool before) {
		if (before == toggle.active || applying || !Enabled || ManualSaves.Loading || ManualSaves.GroupLoading || RemoteWorldEffects.Active) return;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.PassageDoor); writer.Put(ManualSaves.LoadGeneration).Put(CoopIdentity.For(toggle.transform)).Put(toggle.active); MPSteamworks.Instance.Broadcast(writer);
	}
	public void Restore(bool state, bool locked) {
		if (Enabled && !ManualSaves.Loading && state == CloseState && !AllPassed()) { pending = true; return; }
		applying = true; using var effects = new RemoteWorldEffects();
		try { toggle.locked = false; toggle.SetState(state); toggle.locked = locked; pending = false; }
		finally { applying = false; }
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (!Enabled || ManualSaves.Loading || ManualSaves.GroupLoading || !MPSteamworks.Instance.HasConnection(sender) || reader.GetULong() != ManualSaves.LoadGeneration) return;
		ulong id = reader.GetULong(); bool state = reader.GetBool(); var target = CoopIdentity.Find(id, typeof(UT_Toggle)) as UT_Toggle;
		var guard = target != null ? target.GetComponent<CoopPassageDoor>() : null;
		if (guard == null) return;
		if (state == guard.CloseState && !guard.AllPassed()) { guard.pending = true; return; }
		guard.Restore(state, target.locked);
	}
	public static void BroadcastAll() { foreach (var guard in FindObjectsOfType<CoopPassageDoor>(true)) guard.Changed(!guard.toggle.active); }
	public static void RepairLegacy(string mechanisms) {
		var saved = string.IsNullOrEmpty(mechanisms) ? null : JObject.Parse(mechanisms)["entries"];
		var known = new HashSet<string>(saved?.Where(entry => (string)entry["kind"] == "passage").Select(entry => (string)entry["id"]) ?? Enumerable.Empty<string>());
		var player = ENT_Player.GetPlayer(); if (player == null) return;
		foreach (var guard in FindObjectsOfType<CoopPassageDoor>(true)) {
			if (known.Contains(CoopIdentity.For(guard.toggle.transform).ToString()) || Vector3.Distance(PlayerChain.LocalAnchor(player), guard.Center) > 16 || guard.Clear(PlayerChain.LocalAnchor(player), 1.1f, .45f)) continue;
			guard.Restore(!guard.CloseState, false); guard.pending = true;
		}
	}
	private void OnDestroy() { guards.Remove(this); }
	public static void Flush() { for (int i = guards.Count - 1; i >= 0; i--) { var guard = guards[i]; if (guard != null) guard.Tick(); } }
	public static void ResetPending() { foreach (var guard in guards) if (guard != null) guard.pending = false; }
	private void Tick() { if (!pending || !Enabled || ManualSaves.Loading || ManualSaves.GroupLoading || Time.unscaledTime < next) return; next = Time.unscaledTime + .1f; if (!toggle.locked && AllPassed()) { toggle.SetState(CloseState); pending = toggle.active != CloseState; } }
}

[HarmonyPatch(typeof(M_Level), nameof(M_Level.OnSpawn))]
public static class Patch_RegisterPassageDoors { private static void Postfix(M_Level __instance) { CoopPassageDoor.Register(__instance); } }

[HarmonyPatch(typeof(UT_Toggle), nameof(UT_Toggle.SetState))]
public static class Patch_PassageState {
	private static bool Prefix(UT_Toggle __instance, bool b, out bool __state) { __state = __instance.active; return __instance.GetComponent<CoopPassageDoor>()?.Before(b) != false; }
	private static void Postfix(UT_Toggle __instance, bool __state) { __instance.GetComponent<CoopPassageDoor>()?.Changed(__state); }
}

[HarmonyPatch(typeof(UT_Toggle), nameof(UT_Toggle.Toggle))]
public static class Patch_PassageToggle {
	private static bool Prefix(UT_Toggle __instance, out bool __state) { __state = __instance.active; return __instance.GetComponent<CoopPassageDoor>()?.Before(!__instance.active) != false; }
	private static void Postfix(UT_Toggle __instance, bool __state) { __instance.GetComponent<CoopPassageDoor>()?.Changed(__state); }
}
