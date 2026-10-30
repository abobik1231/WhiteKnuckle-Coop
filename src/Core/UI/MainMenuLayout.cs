using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WKMPMod.UI;

public sealed class MainMenuLayout : MonoBehaviour {
	private Vector2 previous;
	private void LateUpdate() {
		var rect = (RectTransform)transform;
		if (previous == rect.rect.size) return;
		var fitter = GetComponent<ContentSizeFitter>(); if (fitter != null) fitter.enabled = false;
		rect.anchorMin = new Vector2(.115f, .035f); rect.anchorMax = new Vector2(.46f, .50f); rect.offsetMin = rect.offsetMax = Vector2.zero;
		var buttons = transform.Cast<Transform>().Where(child => child.GetComponent<Button>() != null).ToArray();
		var group = GetComponent<VerticalLayoutGroup>() ?? gameObject.AddComponent<VerticalLayoutGroup>();
		group.spacing = 5; group.padding = new RectOffset(0, 0, 0, 0); group.childControlHeight = true; group.childForceExpandHeight = false; group.childControlWidth = true; group.childForceExpandWidth = true;
		float height = Mathf.Max(22, (rect.rect.height - group.spacing * (buttons.Length - 1)) / buttons.Length);
		foreach (Transform child in transform) {
			var element = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>();
			if (child.GetComponent<Button>() == null) { element.ignoreLayout = true; child.gameObject.SetActive(false); continue; }
			element.preferredHeight = height; element.minHeight = 20; element.flexibleHeight = 0;
			var text = child.GetComponentInChildren<TMP_Text>(true); if (text != null) { text.enableAutoSizing = true; text.fontSizeMin = 15; text.fontSizeMax = Mathf.Min(34, height * .8f); text.alignment = TextAlignmentOptions.MidlineLeft; text.margin = new Vector4(0, 0, 0, 0); }
		}
		previous = rect.rect.size;
		LayoutRebuilder.MarkLayoutForRebuild(rect);
	}
}
