using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.UI;

namespace WKMPMod.Gameplay;

public sealed class WorldPing : MonoBehaviour {
	private struct Marker { public Vector3 Position; public float Until; }
	private static readonly Dictionary<ulong, Marker> markers = new Dictionary<ulong, Marker>();
	private float nextPing;
	private bool holding;
	private float heldAt;
	private float previewDistance = 20f;
	private Vector3 preview;
	private Texture2D markerIcon;
	private GUIStyle markerStyle;
	private void Awake() {
		markerIcon = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
		var pixels = new Color32[32 * 32];
		for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) {
			int d = Mathf.Abs(x - 15) + Mathf.Abs(y - 19);
			bool diamond = d <= 10 && d >= 6;
			bool stem = Mathf.Abs(x - 15) <= 1 && y <= 12 && y >= 2;
			pixels[y * 32 + x] = diamond || stem ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
		}
		markerIcon.SetPixels32(pixels); markerIcon.Apply();
	}
	private void OnEnable() { DarkMachineFunctions.offsetAction += OffsetMarkers; }
	private void OnDisable() { DarkMachineFunctions.offsetAction -= OffsetMarkers; }
	private static void OffsetMarkers(float amount) {
		var ids = new List<ulong>(markers.Keys);
		foreach (var id in ids) { var marker = markers[id]; marker.Position.y += amount; markers[id] = marker; }
	}
	private void Update() {
		foreach (var id in new List<ulong>(markers.Keys)) if (markers[id].Until <= Time.unscaledTime) markers.Remove(id);
		if (!MPCore.CanSync || !MPSteamworks.Instance.IsLan || LanMenu.BlocksGameplayInput || CL_GameManager.gMan == null || CL_GameManager.gMan.isPaused) { holding = false; return; }
		var player = ENT_Player.GetPlayer();
		if (player == null || player.IsDead() || player.cam == null || Mouse.current == null) { holding = false; return; }
		var mouse = Mouse.current;
		if (mouse.middleButton.wasPressedThisFrame && Time.unscaledTime >= nextPing) { holding = true; heldAt = Time.unscaledTime; previewDistance = 20f; }
		if (!holding) return;
		var ray = new Ray(player.cam.transform.position, player.cam.transform.forward);
		if (mouse.middleButton.isPressed) {
			float scroll = mouse.scroll.ReadValue().y;
			if (Mathf.Abs(scroll) > 0.01f) previewDistance = Mathf.Clamp(previewDistance * Mathf.Exp(Mathf.Sign(scroll) * 0.18f), 1f, 150f);
			preview = ray.GetPoint(previewDistance);
			return;
		}
		if (!mouse.middleButton.wasReleasedThisFrame) { holding = false; return; }
		holding = false;
		Vector3 point = ray.GetPoint(150f);
		float distance = 150f;
		foreach (var hit in Physics.RaycastAll(ray, 150f, ~0, QueryTriggerInteraction.Ignore)) {
			if (hit.collider == null || hit.collider.transform.IsChildOf(player.transform) || hit.collider.GetComponentInParent<ChainGrip>() != null || hit.distance >= distance) continue;
			distance = hit.distance;
			point = hit.point;
		}
		if (Time.unscaledTime - heldAt >= 0.25f) point = ray.GetPoint(previewDistance);
		nextPing = Time.unscaledTime + 0.4f;
		markers[MPSteamworks.UserSteamId] = new Marker { Position = point, Until = Time.unscaledTime + 8f };
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.WorldPing);
		var frame = new PlayerData();
		CoopCoordinates.WriteFrame(ref frame, player);
		writer.Put(point).Put(frame.HasCoordinateFrame);
		if (frame.HasCoordinateFrame) { writer.Put(frame.CoordinateHeight).Put(!string.IsNullOrEmpty(frame.CoordinateLevel)); if (!string.IsNullOrEmpty(frame.CoordinateLevel)) writer.Put(frame.CoordinateLevel).Put(frame.CoordinateOrigin).Put(frame.CoordinateRotation); }
		MPSteamworks.Instance.Broadcast(writer);
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (!MPCore.CanSync || !MPSteamworks.Instance.IsLan || !MPSteamworks.Instance.HasConnection(sender)) return;
		var p = reader.GetVector3();
		if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z)) return;
		var player = ENT_Player.GetPlayer();
		if (player == null || CL_GameManager.gMan == null) return;
		if (reader.GetBool()) {
			float height = reader.GetFloat();
			string level = null; Vector3 origin = Vector3.zero; Quaternion rotation = Quaternion.identity;
			if (reader.GetBool()) { level = reader.GetString(); origin = reader.GetVector3(); rotation = reader.GetQuaternion(); }
			p = CoopCoordinates.MapPosition(p, level, origin, rotation, height, out _);
		}
		markers[sender] = new Marker { Position = p, Until = Time.unscaledTime + 8f };
	}
	private void OnGUI() {
		if (!MPCore.CanSync || CL_GameManager.gMan == null || CL_GameManager.gMan.isPaused) return;
		var camera = ENT_Player.GetPlayer()?.cam;
		if (camera == null) return;
		markerStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 55f, 16, 30)), fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
		if (holding && Time.unscaledTime - heldAt >= 0.25f) DrawMarker(camera, preview, "Метка", 0);
		foreach (var pair in markers) {
			if (pair.Value.Until < Time.unscaledTime) continue;
			DrawMarker(camera, pair.Value.Position, MPSteamworks.Instance.GetMemberName(pair.Key), Mathf.CeilToInt(pair.Value.Until - Time.unscaledTime));
		}
	}
	private void DrawMarker(Camera camera, Vector3 position, string name, int seconds) {
			Vector3 screen = camera.WorldToScreenPoint(position);
			if (screen.z <= 0) { screen.x = Screen.width - screen.x; screen.y = Screen.height - screen.y; }
			screen.x = Mathf.Clamp(screen.x, 100, Screen.width - 100);
			screen.y = Mathf.Clamp(screen.y, 65, Screen.height - 65);
			float distance = Vector3.Distance(camera.transform.position, position);
			float size = Mathf.Clamp(Screen.height / 28f, 28, 54);
			float y = Screen.height - screen.y;
			GUI.DrawTexture(new Rect(screen.x - size / 2, y - size, size, size), markerIcon);
			GUI.Label(new Rect(screen.x - 130, y - size - 36, 260, 32), name + "  " + Mathf.RoundToInt(distance) + " м" + (seconds > 0 ? "  " + seconds + " с" : ""), markerStyle);
	}
	private void OnDestroy() { markers.Clear(); if (markerIcon != null) Destroy(markerIcon); }
}
