using System;
using UnityEngine;
using UnityEngine.UI;
using WKMPMod.Core;
using WKMPMod.NetWork;

namespace WKMPMod.UI;

public sealed class LanTab : MonoBehaviour {
	private Font font;
	private Text status;
	private Text addresses;
	private Text lengthLabel;
	private InputField nameField;
	private InputField addressField;
	private InputField portField;
	private Toggle chain;
	private Toggle enemies;
	private Slider length;
	private Button connect;
	private bool built;
	private static GameObject Element(string name, Transform parent, params Type[] components) {
		var obj = new GameObject(name, components);
		obj.transform.SetParent(parent, false);
		return obj;
	}
	private void OnEnable() {
		if (!built && LanMenu.Instance != null) Build();
		if (!built) return;
		nameField.SetTextWithoutNotify(LanMenu.Instance.PlayerName);
		addressField.SetTextWithoutNotify(LanMenu.Instance.Address);
		portField.SetTextWithoutNotify(LanMenu.Instance.PortText);
	}
	private void Update() {
		if (!built) { if (LanMenu.Instance != null) OnEnable(); return; }
		var menu = LanMenu.Instance;
		if (menu == null) return;
		addresses.text = "Твой Radmin IP: " + menu.Addresses;
		status.text = menu.Busy ? "Подключение, подожди до 10 секунд..." : LanMenu.Status;
		bool room = MPCore.IsInLobby;
		connect.interactable = !menu.Busy && !room;
	}
	private void Build() {
		font = Font.CreateDynamicFontFromOSFont("Arial", 24);
		var viewport = Element("Viewport", transform, typeof(RectTransform), typeof(Image), typeof(RectMask2D));
		var viewportRect = (RectTransform)viewport.transform;
		viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one;
		viewportRect.offsetMin = new Vector2(12, 8); viewportRect.offsetMax = new Vector2(-12, -8);
		viewport.GetComponent<Image>().color = new Color(0.03f, 0.03f, 0.03f, 0.72f);
		var content = Element("Content", viewport.transform, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
		var contentRect = (RectTransform)content.transform;
		contentRect.anchorMin = new Vector2(0, 1); contentRect.anchorMax = new Vector2(1, 1); contentRect.pivot = new Vector2(0.5f, 1);
		contentRect.offsetMin = Vector2.zero; contentRect.offsetMax = Vector2.zero;
		var layout = content.GetComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset(28, 28, 16, 16); layout.spacing = 10; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
		content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		var scroller = gameObject.AddComponent<SmoothMenuScroll>();
		scroller.viewport = viewportRect; scroller.content = contentRect; scroller.horizontal = false; scroller.movementType = ScrollRect.MovementType.Clamped; scroller.decelerationRate = .08f;
		AddText(content.transform, "Radmin VPN", 27, 40);
		AddText(content.transform, "Оба игрока должны быть в одной сети Radmin VPN.", 18, 32);
		nameField = AddInput(content.transform, "Имя игрока", LanMenu.Instance.PlayerName, value => LanMenu.Instance.PlayerName = value);
		addressField = AddInput(content.transform, "IP хоста", LanMenu.Instance.Address, value => LanMenu.Instance.Address = value);
		portField = AddInput(content.transform, "Порт", LanMenu.Instance.PortText, value => LanMenu.Instance.PortText = value);
		portField.contentType = InputField.ContentType.IntegerNumber;
		connect = AddButton(content.transform, "Подключиться", () => LanMenu.Instance.Connect());
		addresses = AddText(content.transform, "", 17, 26);
		AddText(content.transform, "Хост создаёт комнату в меню паузы начатого забега.", 17, 28);
		status = AddText(content.transform, "", 17, 48);
		built = true;
	}
	private Text AddText(Transform parent, string value, int size, float height) {
		var obj = Element("Text", parent, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
		obj.GetComponent<LayoutElement>().preferredHeight = height;
		var text = obj.GetComponent<Text>(); text.font = font; text.fontSize = size; text.color = Color.white; text.text = value; text.alignment = TextAnchor.MiddleLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; text.raycastTarget = false;
		return text;
	}
	private InputField AddInput(Transform parent, string label, string value, Action<string> changed) {
		var row = Element("Input Row", parent, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
		row.GetComponent<LayoutElement>().preferredHeight = 38;
		var group = row.GetComponent<HorizontalLayoutGroup>(); group.spacing = 10; group.childControlWidth = true; group.childControlHeight = true; group.childForceExpandWidth = false;
		var title = AddText(row.transform, label, 18, 38); title.GetComponent<LayoutElement>().preferredWidth = 160;
		var field = Element(label, row.transform, typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
		field.GetComponent<LayoutElement>().flexibleWidth = 1;
		field.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.16f, 1);
		var text = AddText(field.transform, value, 18, 30);
		var rect = (RectTransform)text.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(8, 0); rect.offsetMax = new Vector2(-8, 0);
		var input = field.GetComponent<InputField>(); input.textComponent = text; input.targetGraphic = field.GetComponent<Image>(); input.text = value; input.onValueChanged.AddListener(v => changed(v));
		return input;
	}
	private Toggle AddToggle(Transform parent, string label, bool value, Action<bool> changed) {
		var row = Element(label, parent, typeof(RectTransform), typeof(Toggle), typeof(LayoutElement));
		row.GetComponent<LayoutElement>().preferredHeight = 34;
		var box = Element("Box", row.transform, typeof(RectTransform), typeof(Image));
		var boxRect = (RectTransform)box.transform; boxRect.anchorMin = new Vector2(0, 0.5f); boxRect.anchorMax = new Vector2(0, 0.5f); boxRect.sizeDelta = new Vector2(24, 24); boxRect.anchoredPosition = new Vector2(12, 0);
		box.GetComponent<Image>().color = new Color(0.22f, 0.22f, 0.22f, 1);
		var mark = Element("Mark", box.transform, typeof(RectTransform), typeof(Image));
		var markRect = (RectTransform)mark.transform; markRect.anchorMin = Vector2.zero; markRect.anchorMax = Vector2.one; markRect.offsetMin = new Vector2(5, 5); markRect.offsetMax = new Vector2(-5, -5); mark.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.17f);
		var text = AddText(row.transform, label, 18, 34); var rect = (RectTransform)text.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(36, 0); rect.offsetMax = Vector2.zero;
		var toggle = row.GetComponent<Toggle>(); toggle.targetGraphic = box.GetComponent<Image>(); toggle.graphic = mark.GetComponent<Image>(); toggle.isOn = value; toggle.onValueChanged.AddListener(v => changed(v));
		return toggle;
	}
	private Slider AddSlider(Transform parent) {
		var obj = Element("Chain Length", parent, typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
		obj.GetComponent<LayoutElement>().preferredHeight = 30;
		var background = Element("Background", obj.transform, typeof(RectTransform), typeof(Image));
		var bg = (RectTransform)background.transform; bg.anchorMin = new Vector2(0, 0.5f); bg.anchorMax = new Vector2(1, 0.5f); bg.offsetMin = new Vector2(8, -4); bg.offsetMax = new Vector2(-8, 4); background.GetComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f);
		var handleArea = Element("Handle Area", obj.transform, typeof(RectTransform));
		var area = (RectTransform)handleArea.transform; area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one; area.offsetMin = new Vector2(8, 0); area.offsetMax = new Vector2(-8, 0);
		var handle = Element("Handle", handleArea.transform, typeof(RectTransform), typeof(Image));
		((RectTransform)handle.transform).sizeDelta = new Vector2(16, 24); handle.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.17f);
		var slider = obj.GetComponent<Slider>(); slider.handleRect = (RectTransform)handle.transform; slider.targetGraphic = handle.GetComponent<Image>(); slider.minValue = 3; slider.maxValue = 12; slider.wholeNumbers = true; slider.value = LanMenu.Instance.ChainLength; slider.onValueChanged.AddListener(v => LanMenu.Instance.ChainLength = v);
		return slider;
	}
	private Button AddButton(Transform parent, string label, Action clicked) {
		var obj = Element(label, parent, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
		obj.GetComponent<LayoutElement>().preferredHeight = 38; obj.GetComponent<Image>().color = new Color(0.25f, 0.08f, 0.08f, 1);
		var text = AddText(obj.transform, label, 19, 38); var rect = (RectTransform)text.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; text.alignment = TextAnchor.MiddleCenter;
		var button = obj.GetComponent<Button>(); button.targetGraphic = obj.GetComponent<Image>(); button.onClick.AddListener(() => clicked()); return button;
	}
}
