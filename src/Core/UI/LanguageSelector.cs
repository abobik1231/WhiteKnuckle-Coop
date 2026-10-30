using System.Collections.Generic;
using TMPro;
using UnityEngine;
using WKMPMod.Gameplay;

namespace WKMPMod.UI;

public sealed class LanguageSelector : MonoBehaviour {
	private TMP_Dropdown dropdown;
	private void Start() {
		var rect = (RectTransform)transform; rect.anchorMin = rect.anchorMax = Vector2.one; rect.pivot = Vector2.one; rect.anchoredPosition = new Vector2(-38, -15); rect.sizeDelta = new Vector2(270, 40);
		var template = GetComponentInParent<UI_SettingsMenu>().GetComponentInChildren<Settings_Resolution>(true)?.dropdown; if (template == null) return;
		dropdown = Instantiate(template, transform); dropdown.name = "Language Choice"; dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent(); dropdown.ClearOptions();
		using (new RussianGame.UserTextScope(true)) dropdown.AddOptions(new List<string> { "English", "Русский" });
		var area = (RectTransform)dropdown.transform; area.anchorMin = new Vector2(.34f, 0); area.anchorMax = Vector2.one; area.offsetMin = area.offsetMax = Vector2.zero;
		var caption = Instantiate(dropdown.captionText, transform); caption.name = "Language Caption"; caption.text = "Language"; caption.alignment = TextAlignmentOptions.MidlineLeft; caption.raycastTarget = false;
		var title = caption.rectTransform; title.anchorMin = Vector2.zero; title.anchorMax = new Vector2(.31f, 1); title.offsetMin = title.offsetMax = Vector2.zero;
		dropdown.SetValueWithoutNotify(RussianGame.IsRussian ? 1 : 0); dropdown.onValueChanged.AddListener(value => RussianGame.SetRussian(value == 1));
		RussianGame.LanguageChanged += Refresh;
	}
	private void OnDestroy() { RussianGame.LanguageChanged -= Refresh; }
	private void Refresh() { if (dropdown != null) dropdown.SetValueWithoutNotify(RussianGame.IsRussian ? 1 : 0); }
}
