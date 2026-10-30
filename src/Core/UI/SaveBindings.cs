using System;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using WKMPMod.Core;
using WKMPMod.Gameplay;

namespace WKMPMod.UI;

public sealed class SaveBindings : MonoBehaviour {
	private static ConfigEntry<Key> save;
	private static ConfigEntry<Key> load;
	private static SaveBindings active;
	private Button saveButton;
	private Button loadButton;
	private int capture;
	private int startedFrame;
	private static float blockedUntil;
	public static bool Capturing => active != null && active.capture != 0;
	public static bool NativeKeyDown(KeyCode code) { Initialize(); return (!Enum.TryParse<Key>(code.ToString(), out var key) || key != save.Value && key != load.Value) && Input.GetKeyDown(code); }
	private static void Initialize() { save ??= MPMain.Instance.Config.Bind("Controls", "SaveGame", Key.F1); load ??= MPMain.Instance.Config.Bind("Controls", "LoadLastSave", Key.F4); }
	public static bool Pressed(bool saving) { Initialize(); var key = saving ? save.Value : load.Value; return !Capturing && Time.unscaledTime >= blockedUntil && Keyboard.current != null && key != Key.None && Keyboard.current[key].wasPressedThisFrame; }
	public static void Build(RectTransform root, Button template) {
		var panel = new GameObject("WK Save Bindings", typeof(RectTransform), typeof(HorizontalLayoutGroup)); panel.transform.SetParent(root, false);
		var rect = (RectTransform)panel.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.right; rect.offsetMin = Vector2.zero; rect.offsetMax = new Vector2(0, 42);
		foreach (Transform child in root) if (child != panel.transform && child is RectTransform area) { if (Mathf.Approximately(area.anchorMin.y, area.anchorMax.y)) area.anchoredPosition += Vector2.up * 52; else area.offsetMin += Vector2.up * 52; }
		var group = panel.GetComponent<HorizontalLayoutGroup>(); group.spacing = 12; group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = group.childForceExpandHeight = true;
		var controls = panel.AddComponent<SaveBindings>();
		controls.saveButton = controls.Make(template, true); controls.loadButton = controls.Make(template, false);
	}
	private Button Make(Button template, bool saving) {
		var button = Instantiate(template, transform); button.name = saving ? "WK Save Game Binding" : "WK Load Save Binding";
		button.gameObject.SetActive(true);
		foreach (var script in button.GetComponents<MonoBehaviour>()) if (script is UI_FirstSelect || script is UI_PressButtonOnInput || script is UI_AnimateOnSelect) Destroy(script);
		button.onClick = new Button.ButtonClickedEvent(); button.onClick.AddListener(() => { active = this; capture = saving ? 1 : 2; startedFrame = Time.frameCount; }); return button;
	}
	private void OnEnable() { Initialize(); Refresh(); }
	private void OnDisable() { capture = 0; if (active == this) active = null; }
	private void Update() {
		if (capture != 0 && Time.frameCount > startedFrame && Keyboard.current != null) foreach (var key in Keyboard.current.allKeys) if (key.wasPressedThisFrame) {
			if (key.keyCode != Key.Escape) { var changed = capture == 1 ? save : load; var other = capture == 1 ? load : save; if (other.Value == key.keyCode) other.Value = changed.Value; changed.Value = key.keyCode; }
			capture = 0; blockedUntil = Time.unscaledTime + .3f; break;
		}
		Refresh();
	}
	private void Refresh() {
		if (saveButton == null || loadButton == null) return;
		string Caption(bool saving) => RussianGame.Translate(saving ? "Сохранить игру" : "Загрузить последнее") + ": " + (capture == (saving ? 1 : 2) ? RussianGame.Translate("Нажмите клавишу") : (saving ? save.Value : load.Value).ToString());
		saveButton.GetComponentInChildren<TMP_Text>(true).text = Caption(true); loadButton.GetComponentInChildren<TMP_Text>(true).text = Caption(false);
	}
}
