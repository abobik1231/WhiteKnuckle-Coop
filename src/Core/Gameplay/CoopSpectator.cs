using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using WKMPMod.Core;
using WKMPMod.NetWork;
using WKMPMod.RemotePlayer;
using WKMPMod.UI;

namespace WKMPMod.Gameplay;

[DefaultExecutionOrder(3000)]
public sealed class CoopSpectator : MonoBehaviour {
	private static CoopSpectator instance;
	public static bool Active => instance != null && instance.view != null;
	private Camera view;
	private readonly Dictionary<Camera, bool> cameras = new Dictionary<Camera, bool>();
	private readonly Dictionary<AudioListener, bool> listeners = new Dictionary<AudioListener, bool>();
	private bool follow;
	private ulong target;
	private Vector2 angles;
	public static void Enter() {
		if (Active) return;
		if (instance == null) instance = MPCore.Instance.gameObject.AddComponent<CoopSpectator>();
		var player = ENT_Player.GetPlayer();
		if (player?.cam == null) return;
		var cameraObject = new GameObject("Coop Spectator Camera");
		instance.view = cameraObject.AddComponent<Camera>();
		instance.view.CopyFrom(player.cam);
		instance.view.targetTexture = null;
		instance.view.transform.SetPositionAndRotation(player.cam.transform.position, player.cam.transform.rotation);
		instance.angles = new Vector2(player.cam.transform.eulerAngles.x, player.cam.transform.eulerAngles.y);
		if (instance.angles.x > 180) instance.angles.x -= 360;
		foreach (var camera in new[] { player.cam, player.overlayCam, player.inventoryCam }) {
			if (camera == null) continue;
			instance.cameras[camera] = camera.enabled;
			camera.enabled = false;
		}
		foreach (var listener in FindObjectsOfType<AudioListener>()) { instance.listeners[listener] = listener.enabled; listener.enabled = false; }
		cameraObject.AddComponent<AudioListener>();
		CL_GameManager.SetTimescale(1);
		CL_GameManager.gMan.isPaused = false;
		AudioListener.pause = false;
	}
	public static void Exit() {
		if (instance == null) return;
		foreach (var camera in instance.cameras) if (camera.Key != null) camera.Key.enabled = camera.Value;
		foreach (var listener in instance.listeners) if (listener.Key != null) listener.Key.enabled = listener.Value;
		instance.cameras.Clear();
		instance.listeners.Clear();
		if (instance.view != null) Destroy(instance.view.gameObject);
		instance.view = null;
		instance.follow = false;
	}
	private List<ulong> Targets() => RPManager.Instance.Players.Where(pair => pair.Value.PlayerObject != null && pair.Value.PlayerObject.activeInHierarchy && CoopPresence.Alive(pair.Key)).Select(pair => pair.Key).OrderBy(id => id).ToList();
	private void LateUpdate() {
		if (!Active) return;
		var player = ENT_Player.GetPlayer();
		if (!MPCore.IsInLobby || player == null || !player.IsDead() || ManualSaves.Loading) { Exit(); return; }
		var keyboard = Keyboard.current;
		var game = CL_GameManager.gMan;
		if (keyboard?.escapeKey.wasPressedThisFrame == true && !ManualSaves.IsOpen && !MultiplayerChat.IsOpen && !LanMenu.IsOpen) {
			if (game.isPaused) { game.isPaused = false; game.UnPause(); CL_GameManager.SetTimescale(1); }
			else game.Pause();
		}
		if (game.isPaused && !game.pauseMenu.activeSelf && !ManualSaves.IsOpen) { game.isPaused = false; CL_GameManager.SetTimescale(1); }
		if (game.isPaused || ManualSaves.IsOpen || MultiplayerChat.IsOpen || LanMenu.IsOpen) return;
		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
		var targets = Targets();
		if (keyboard?.tabKey.wasPressedThisFrame == true) { follow = !follow; if (!follow) { angles = new Vector2(view.transform.eulerAngles.x, view.transform.eulerAngles.y); if (angles.x > 180) angles.x -= 360; } }
		if (follow && targets.Count > 0) {
			int index = targets.IndexOf(target);
			if (index < 0) index = 0;
			if (keyboard?.rightArrowKey.wasPressedThisFrame == true) index = (index + 1) % targets.Count;
			if (keyboard?.leftArrowKey.wasPressedThisFrame == true) index = (index + targets.Count - 1) % targets.Count;
			target = targets[index];
			var remote = RPManager.Instance.Players[target].PlayerObject;
			if (CoopPresence.Views.TryGetValue(target, out var state)) view.transform.SetPositionAndRotation(remote.transform.position + state.CameraOffset, state.CameraRotation);
			else view.transform.SetPositionAndRotation(remote.transform.position + Vector3.up * 0.8f, remote.transform.rotation);
			return;
		}
		if (follow) follow = false;
		if (keyboard == null) return;
		Vector2 look = Mouse.current?.delta.ReadValue() ?? Vector2.zero;
		angles.x = Mathf.Clamp(angles.x - look.y * 0.12f, -89, 89);
		angles.y += look.x * 0.12f;
		view.transform.rotation = Quaternion.Euler(angles.x, angles.y, 0);
		Vector3 movement = Vector3.zero;
		if (keyboard.wKey.isPressed) movement += view.transform.forward;
		if (keyboard.sKey.isPressed) movement -= view.transform.forward;
		if (keyboard.aKey.isPressed) movement -= view.transform.right;
		if (keyboard.dKey.isPressed) movement += view.transform.right;
		if (keyboard.spaceKey.isPressed) movement += Vector3.up;
		if (keyboard.leftCtrlKey.isPressed) movement -= Vector3.up;
		view.transform.position += Vector3.ClampMagnitude(movement, 1) * (keyboard.leftShiftKey.isPressed ? 14 : 5) * Time.unscaledDeltaTime;
	}
	private void OnGUI() {
		if (!Active || CL_GameManager.gMan.isPaused) return;
		GUI.Box(new Rect(15, 15, 660, 65), follow ? "Наблюдение: " + MPSteamworks.Instance.GetMemberName(target) + "\nTab: свободный полёт. Клавиши влево и вправо: другой игрок. Esc: меню." : "Спектр. WASD: движение, мышь: обзор, пробел: вверх, Ctrl: вниз.\nShift: быстрее. Tab: вид напарника. Esc: меню и сохранения.");
	}
	private void OnDestroy() { Exit(); if (instance == this) instance = null; }
}
