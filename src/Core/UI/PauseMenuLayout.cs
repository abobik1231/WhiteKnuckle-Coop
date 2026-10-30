using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace WKMPMod.UI;

public sealed class PauseMenuLayout : MonoBehaviour {
	private int previous;
	private void LateUpdate() {
		int count = transform.Cast<Transform>().Count(child => child.gameObject.activeSelf); if (count == previous) return; previous = count;
		var rect = (RectTransform)transform; var fitter = GetComponent<ContentSizeFitter>(); if (fitter != null) fitter.enabled = false;
		rect.anchorMin = new Vector2(.31f, .10f); rect.anchorMax = new Vector2(.69f, .62f); rect.offsetMin = rect.offsetMax = Vector2.zero;
		var group = GetComponent<VerticalLayoutGroup>() ?? gameObject.AddComponent<VerticalLayoutGroup>(); group.padding = new RectOffset(); group.spacing = 8; group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = true; group.childForceExpandHeight = false; group.childAlignment = TextAnchor.UpperCenter;
		float height = Mathf.Min(46, Mathf.Max(24, (rect.rect.height - Mathf.Max(0, count - 1) * group.spacing) / Mathf.Max(1, count)));
		foreach (Transform child in transform) { var element = child.GetComponent<LayoutElement>() ?? child.gameObject.AddComponent<LayoutElement>(); element.minHeight = 0; element.preferredHeight = height; element.flexibleHeight = 0; }
		LayoutRebuilder.MarkLayoutForRebuild(rect);
	}
}
