using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WKMPMod.UI;

public sealed class SettingsLayout : MonoBehaviour {
	private bool built;
	private readonly List<Button> tabs = new List<Button>();
	private IEnumerator Start() { yield return null; Build(); }
	private void Build() {
		if (built) return;
		var pane = transform.Find("SettingsParent/Settings Pane"); if (pane == null) return;
		var selector = pane.Find("Tab Selection Hor"); var video = pane.Find("Video Settings");
		var innerSelector = video?.Find("Controls Page Tab Selector");
		if (selector == null || innerSelector == null) return;
		var outer = selector.GetComponent<UI_TabGroup>(); var inner = innerSelector.GetComponent<UI_TabGroup>();
		if (outer == null || inner == null || inner.tabs.Count < 2 || outer.tabs.Count < 3) return;
		built = true;
		var language = new GameObject("WK Language", typeof(RectTransform)); language.transform.SetParent(pane, false); language.AddComponent<LanguageSelector>();
		var navigation = new GameObject("WK Settings Navigation", typeof(RectTransform), typeof(HorizontalLayoutGroup)); navigation.transform.SetParent(pane, false);
		var nav = (RectTransform)navigation.transform; nav.anchorMin = new Vector2(0, 1); nav.anchorMax = Vector2.one; nav.pivot = new Vector2(.5f, 1); nav.offsetMin = new Vector2(38, -122); nav.offsetMax = new Vector2(-38, -74);
		var row = navigation.GetComponent<HorizontalLayoutGroup>(); row.spacing = 10; row.childControlWidth = row.childControlHeight = true; row.childForceExpandWidth = true;
		var template = inner.tabs[0].button;
		void Add(string label, string outerName, string innerName) {
			var copy = Instantiate(template, navigation.transform); copy.name = "WK " + label;
			foreach (var script in copy.GetComponents<MonoBehaviour>()) if (script is UI_FirstSelect || script is UI_PressButtonOnInput || script is UI_AnimateOnSelect) Destroy(script);
			copy.onClick = new Button.ButtonClickedEvent(); int index = tabs.Count;
			copy.onClick.AddListener(() => { outer.SelectTab(outerName); if (innerName != null) inner.SelectTab(innerName); for (int i = 0; i < tabs.Count; i++) tabs[i].image.color = i == index ? new Color(.8f, .18f, .18f, 1) : new Color(.65f, .65f, .65f, 1); });
			copy.image.color = index == 0 ? new Color(.8f, .18f, .18f, 1) : new Color(.65f, .65f, .65f, 1);
			var text = copy.GetComponentInChildren<TMP_Text>(true); if (text != null) { text.text = label; text.enableAutoSizing = true; text.fontSizeMin = 13; text.fontSizeMax = 23; }
			tabs.Add(copy);
		}
		Add("ГРАФИКА", outer.tabs[0].name, inner.tabs[0].name); Add("ЗВУК", outer.tabs[0].name, inner.tabs[1].name); Add("УПРАВЛЕНИЕ", outer.tabs[1].name, null); Add("ДОСТУПНОСТЬ", outer.tabs[2].name, null);
		foreach (Transform child in selector) child.gameObject.SetActive(false);
		var heading = pane.Find("Overview Titles") as RectTransform;
		if (heading != null) { heading.anchorMin = new Vector2(0, 1); heading.anchorMax = Vector2.one; heading.offsetMin = new Vector2(38, -64); heading.offsetMax = new Vector2(-38, -8); foreach (var text in heading.GetComponentsInChildren<TMP_Text>(true)) { text.enableAutoSizing = true; text.fontSizeMin = 22; text.fontSizeMax = 38; } }
		foreach (Transform child in innerSelector) child.gameObject.SetActive(false);
		((RectTransform)innerSelector).sizeDelta = Vector2.zero;
		foreach (var page in new[] { pane.Find("Video Settings"), pane.Find("Controls Page"), pane.Find("Accessibility Settings") }) {
			if (page == null) continue;
			var rect = (RectTransform)page; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(38, 78); rect.offsetMax = new Vector2(-38, -142);
			var title = page.Find("Tab Title"); if (title != null) title.gameObject.SetActive(false);
		}
		var main = (RectTransform)video.Find("Main Panel"); main.anchorMin = Vector2.zero; main.anchorMax = Vector2.one; main.offsetMin = main.offsetMax = Vector2.zero;
		foreach (var tabName in new[] { "Tab - Video", "Tab - Audio" }) {
			var page = main.Find(tabName); if (page == null) continue;
			var rect = (RectTransform)page; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
			var horizontal = page.GetComponent<HorizontalLayoutGroup>() ?? page.gameObject.AddComponent<HorizontalLayoutGroup>(); horizontal.spacing = 16; horizontal.childControlWidth = horizontal.childControlHeight = true; horizontal.childForceExpandWidth = true; horizontal.childForceExpandHeight = true;
			foreach (Transform column in page) {
				if (column.GetComponent<VerticalLayoutGroup>() == null) continue;
				var background = column.GetComponent<Image>() ?? column.gameObject.AddComponent<Image>(); background.color = new Color(.035f, .035f, .035f, .88f); background.raycastTarget = false;
				var layout = column.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(15, 15, 14, 14); layout.spacing = 10; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
				var preferred = column.GetComponent<LayoutElement>() ?? column.gameObject.AddComponent<LayoutElement>(); preferred.minWidth = 0; preferred.preferredWidth = 0; preferred.flexibleWidth = 1;
				foreach (Transform control in column) {
					if (control.name.StartsWith("---")) { control.gameObject.SetActive(false); continue; }
					var item = control.GetComponent<LayoutElement>() ?? control.gameObject.AddComponent<LayoutElement>(); item.minHeight = 0; item.flexibleHeight = 0;
					bool toggle = control.GetComponent<Toggle>() != null;
					bool slider = control.GetComponentInChildren<SliderSettingBinder>(true) != null;
					item.preferredHeight = toggle ? 34 : slider ? 64 : control.GetComponent<Settings_Resolution>() != null ? 72 : 30;
					foreach (var text in control.GetComponentsInChildren<TMP_Text>(true)) { text.enableAutoSizing = true; text.fontSizeMin = 11; text.fontSizeMax = 20; text.enableWordWrapping = true; }
				}
			}
		}
		var close = (RectTransform)pane.Find("Save And Close"); if (close != null) { close.anchorMin = close.anchorMax = new Vector2(1, 0); close.pivot = new Vector2(1, 0); close.anchoredPosition = new Vector2(-38, 16); close.sizeDelta = new Vector2(300, 46); }
		foreach (string path in new[] { "Controls Page/Options Tab", "Accessibility Settings/Options Tab" }) {
			var options = pane.Find(path) as RectTransform; if (options == null) continue;
			options.anchorMin = Vector2.zero; options.anchorMax = Vector2.one; options.offsetMin = Vector2.zero; options.offsetMax = new Vector2(0, path.StartsWith("Controls") ? -54 : 0);
			var columns = options.GetComponent<HorizontalLayoutGroup>(); if (columns != null) { columns.spacing = 16; columns.childControlWidth = columns.childControlHeight = true; columns.childForceExpandWidth = columns.childForceExpandHeight = true; }
			foreach (Transform column in options) {
				var layout = column.GetComponent<VerticalLayoutGroup>(); if (layout == null) continue;
				layout.padding = new RectOffset(15, 15, 12, 12); layout.spacing = 8; layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = false;
				var background = column.GetComponent<Image>(); if (background != null) { background.color = new Color(.035f, .035f, .035f, .88f); background.raycastTarget = false; }
				var width = column.GetComponent<LayoutElement>() ?? column.gameObject.AddComponent<LayoutElement>(); width.minWidth = 0; width.preferredWidth = 0; width.flexibleWidth = 1;
				foreach (Transform control in column) {
					if (control.name.StartsWith("---")) { control.gameObject.SetActive(false); continue; }
					var height = control.GetComponent<LayoutElement>() ?? control.gameObject.AddComponent<LayoutElement>(); height.minHeight = 0; height.preferredHeight = control.GetComponent<Toggle>() != null ? 34 : control.GetComponentInChildren<SliderSettingBinder>(true) != null ? 64 : 30; height.flexibleHeight = 0;
					foreach (var text in control.GetComponentsInChildren<TMP_Text>(true)) { text.enableAutoSizing = true; text.fontSizeMin = 11; text.fontSizeMax = 20; text.enableWordWrapping = true; }
				}
			}
		}
		var controlsSelector = pane.Find("Controls Page/Controls Page Tab Selector") as RectTransform;
		if (controlsSelector != null) { controlsSelector.anchorMin = new Vector2(0, 1); controlsSelector.anchorMax = Vector2.one; controlsSelector.offsetMin = new Vector2(0, -44); controlsSelector.offsetMax = Vector2.zero; }
		var bindings = pane.Find("Controls Page/Binding Tab Root") as RectTransform;
		if (bindings != null) { bindings.anchorMin = Vector2.zero; bindings.anchorMax = Vector2.one; bindings.offsetMin = Vector2.zero; bindings.offsetMax = new Vector2(0, -54); }
		if (bindings != null && bindings.Find("KBM Bindings Tab") is RectTransform keyboardBindings) SaveBindings.Build(keyboardBindings, template);
		navigation.transform.SetAsLastSibling();
		LayoutRebuilder.MarkLayoutForRebuild((RectTransform)pane);
	}
}

[HarmonyPatch(typeof(UI_SettingsMenu), "Start")]
public static class Patch_SettingsLayout { private static void Postfix(UI_SettingsMenu __instance) { if (__instance.GetComponent<SettingsLayout>() == null) __instance.gameObject.AddComponent<SettingsLayout>(); } }
