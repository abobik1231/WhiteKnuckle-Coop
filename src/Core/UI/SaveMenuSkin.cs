using System.Collections.Generic;
using UnityEngine;

namespace WKMPMod.UI;

public sealed class SaveMenuSkin {
	private readonly List<Texture2D> textures = new List<Texture2D>();
	public GUISkin Skin { get; }
	public GUIStyle Title { get; }
	public GUIStyle Subtitle { get; }
	public Texture2D Backdrop { get; }
	private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
	private Texture2D Frame(Color32 fill, Color32 line, int thickness = 2) {
		const int size = 12;
		var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
		var pixels = new Color32[size * size];
		for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) pixels[y * size + x] = x < thickness || y < thickness || x >= size - thickness || y >= size - thickness ? line : fill;
		texture.SetPixels32(pixels); texture.Apply(); textures.Add(texture); return texture;
	}
	private static void State(GUIStyleState state, Texture2D texture, Color color) { state.background = texture; state.textColor = color; }
	private static void Border(GUIStyle style) { style.border = new RectOffset(3, 3, 3, 3); }
	public SaveMenuSkin() {
		Skin = Object.Instantiate(GUI.skin);
		Skin.font = Font.CreateDynamicFontFromOSFont("Consolas", 20);
		Backdrop = Frame(new Color32(0, 0, 0, 170), new Color32(0, 0, 0, 170));
		var panel = Frame(new Color32(8, 8, 8, 245), new Color32(91, 91, 91, 255));
		var card = Frame(new Color32(12, 12, 12, 235), new Color32(72, 72, 72, 255));
		var idle = Frame(new Color32(8, 8, 8, 245), new Color32(155, 155, 155, 255));
		var hover = Frame(new Color32(37, 10, 10, 250), new Color32(198, 55, 55, 255));
		var pressed = Frame(new Color32(87, 20, 20, 255), new Color32(233, 96, 96, 255));
		var disabled = Frame(new Color32(14, 14, 14, 225), new Color32(60, 60, 60, 255));
		State(Skin.box.normal, panel, Color.white); Border(Skin.box); Skin.box.padding = new RectOffset(12, 12, 10, 10); Skin.box.fontSize = 18;
		State(Skin.button.normal, idle, Color.white); State(Skin.button.hover, hover, Color.white); State(Skin.button.active, pressed, Color.white); State(Skin.button.focused, hover, Color.white); Border(Skin.button); Skin.button.fontSize = 19; Skin.button.fontStyle = FontStyle.Bold; Skin.button.alignment = TextAnchor.MiddleCenter; Skin.button.padding = new RectOffset(10, 10, 4, 4);
		State(Skin.button.onNormal, idle, Color.white); State(Skin.button.onHover, hover, Color.white); State(Skin.button.onActive, pressed, Color.white);
		Skin.label.fontSize = 17; Skin.label.normal.textColor = new Color(0.9f, 0.9f, 0.9f); Skin.label.wordWrap = true; Skin.label.padding = new RectOffset(2, 2, 2, 2);
		State(Skin.textField.normal, card, Color.white); State(Skin.textField.focused, hover, Color.white); Border(Skin.textField); Skin.textField.fontSize = 18; Skin.textField.padding = new RectOffset(9, 9, 7, 7);
		State(Skin.toggle.normal, idle, Color.white); State(Skin.toggle.hover, hover, Color.white); State(Skin.toggle.active, pressed, Color.white); State(Skin.toggle.onNormal, hover, Color.white); State(Skin.toggle.onHover, pressed, Color.white); State(Skin.toggle.onActive, pressed, Color.white); Border(Skin.toggle); Skin.toggle.fontSize = 17; Skin.toggle.padding = new RectOffset(12, 12, 6, 6);
		State(Skin.verticalScrollbar.normal, card, Color.white); State(Skin.verticalScrollbarThumb.normal, idle, Color.white); State(Skin.verticalScrollbarThumb.hover, hover, Color.white); Border(Skin.verticalScrollbar); Border(Skin.verticalScrollbarThumb);
		Skin.verticalScrollbar.fixedWidth = 16; Skin.verticalScrollbarThumb.fixedWidth = 12; Skin.verticalScrollbarThumb.fixedHeight = 0; Skin.verticalScrollbarThumb.stretchHeight = true;
		State(Skin.horizontalSlider.normal, card, Color.white); State(Skin.horizontalSliderThumb.normal, idle, Color.white); State(Skin.horizontalSliderThumb.hover, hover, Color.white); Border(Skin.horizontalSlider); Border(Skin.horizontalSliderThumb); Skin.horizontalSliderThumb.fixedWidth = 18; Skin.horizontalSliderThumb.fixedHeight = 22;
		Title = new GUIStyle(Skin.label) { fontSize = 29, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }; Title.normal.textColor = new Color(0.76f, 0.19f, 0.19f);
		Subtitle = new GUIStyle(Skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter }; Subtitle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
		Skin.button.onFocused.background = disabled;
	}
	public void Dispose() { foreach (var texture in textures) if (texture != null) Object.Destroy(texture); if (Skin != null) { if (Skin.font != null) Object.Destroy(Skin.font); Object.Destroy(Skin); } }
}
