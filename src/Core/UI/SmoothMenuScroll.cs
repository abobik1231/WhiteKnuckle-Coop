using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WKMPMod.UI;

public sealed class SmoothMenuScroll : ScrollRect {
	private float target;
	private float speed;
	private bool wheeling;
	public override void OnScroll(PointerEventData data) {
		if (!IsActive() || content == null || viewport == null) return;
		float maximum = Mathf.Max(0, content.rect.height - viewport.rect.height);
		if (maximum < 1) return;
		if (!wheeling) target = content.anchoredPosition.y;
		target = Mathf.Clamp(target - data.scrollDelta.y * 42, 0, maximum);
		wheeling = true; velocity = Vector2.zero;
	}
	public override void OnBeginDrag(PointerEventData data) { wheeling = false; base.OnBeginDrag(data); }
	protected override void LateUpdate() {
		base.LateUpdate();
		if (!wheeling || content == null || viewport == null) return;
		target = Mathf.Clamp(target, 0, Mathf.Max(0, content.rect.height - viewport.rect.height));
		var position = content.anchoredPosition;
		position.y = Mathf.SmoothDamp(position.y, target, ref speed, .09f, float.PositiveInfinity, Time.unscaledDeltaTime);
		content.anchoredPosition = position;
		if (Mathf.Abs(position.y - target) < .1f) { position.y = target; content.anchoredPosition = position; wheeling = false; }
	}
}
