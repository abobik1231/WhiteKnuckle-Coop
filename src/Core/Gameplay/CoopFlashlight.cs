using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.RemotePlayer;

namespace WKMPMod.Gameplay;

public sealed class CoopFlashlight : MonoBehaviour {
	private sealed class State { public CL_Lamp Lamp; public float Until; public float Intensity; }
	private static readonly Dictionary<ulong, State> lamps = new Dictionary<ulong, State>();
	private float nextSend;
	private void Update() {
		if (!MPCore.CanSync || !MPSteamworks.Instance.IsLan) { Clear(); return; }
		if (Time.unscaledTime >= nextSend) {
			nextSend = Time.unscaledTime + 0.25f;
			float intensity = 0;
			if (Inventory.instance != null) foreach (var item in Inventory.instance.GetItemsByPrefab("Item_Flashlight")) {
				var flashlight = item?.GetDropObject(false) as Item_Object_Flashlight;
				if (flashlight?.lamp != null && flashlight.lamp.gameObject.activeInHierarchy) intensity = Mathf.Max(intensity, flashlight.lamp.intensity);
			}
			var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.PlayerFlashlight);
			writer.Put(Mathf.Clamp(intensity, 0, 10));
			MPSteamworks.Instance.Broadcast(writer, Steamworks.Data.SendType.Unreliable | Steamworks.Data.SendType.NoNagle);
		}
		foreach (var pair in lamps.ToArray()) {
			if (pair.Value.Until < Time.unscaledTime || !MPSteamworks.Instance.HasConnection(pair.Key)) { if (pair.Value.Lamp != null) Destroy(pair.Value.Lamp.gameObject); lamps.Remove(pair.Key); continue; }
			var remote = RPManager.Instance.GetPlayerObject(pair.Key);
			if (remote == null || !remote.activeInHierarchy || !CoopPresence.Views.TryGetValue(pair.Key, out var view) || !view.Alive) { pair.Value.Lamp.gameObject.SetActive(false); continue; }
			pair.Value.Lamp.gameObject.SetActive(pair.Value.Intensity > 0.01f);
			pair.Value.Lamp.transform.position = remote.transform.position + view.CameraOffset;
			pair.Value.Lamp.transform.rotation = view.CameraRotation;
			pair.Value.Lamp.intensity = pair.Value.Intensity;
		}
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (!MPCore.CanSync || !MPSteamworks.Instance.HasConnection(sender)) return;
		float intensity = reader.GetFloat();
		if (float.IsNaN(intensity) || float.IsInfinity(intensity) || intensity < 0 || intensity > 10) return;
		if (!lamps.TryGetValue(sender, out var state)) {
			var go = new GameObject("Remote Flashlight");
			go.SetActive(false);
			var lamp = go.AddComponent<CL_Lamp>();
			var template = (CL_AssetManager.GetItemObjectPrefab("Item_Flashlight") as Item_Object_Flashlight)?.lamp;
			if (template != null) { lamp.range = template.range; lamp.angle = template.angle; lamp.startColor = template.startColor; lamp.attentuation = template.attentuation; lamp.bypassLightmap = template.bypassLightmap; lamp.emissiveMult = template.emissiveMult; }
			else { lamp.range = 20; lamp.angle = 60; lamp.startColor = Color.white; }
			lamps[sender] = state = new State { Lamp = lamp };
		}
		state.Intensity = intensity; state.Until = Time.unscaledTime + 2;
	}
	private static void Clear() { foreach (var state in lamps.Values) if (state.Lamp != null) Destroy(state.Lamp.gameObject); lamps.Clear(); }
	private void OnDestroy() { Clear(); }
}
