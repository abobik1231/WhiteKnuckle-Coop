using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;

namespace WKMPMod.UI;

[DefaultExecutionOrder(-10000)]
public sealed class MultiplayerChat : MonoBehaviour {
	private sealed class Entry { public string Text; public string Name; public string Message; public bool Local; public float Time; }
	private const float MessageLifetime = 12f;
	private static readonly List<Entry> history = new List<Entry>();
	private static readonly Dictionary<ulong, float> receivedAt = new Dictionary<ulong, float>();
	public static bool IsOpen { get; private set; }
	private static float blockUntil;
	public static bool BlocksGameplayInput => IsOpen || Time.unscaledTime < blockUntil;
	public static int MessageCount => history.Count;
	public static string LastMessage => history.Count == 0 ? "" : history[history.Count - 1].Text;
	private string input = "";
	private float lastKey = -1;
	private float lastSent = -1;
	private Keyboard textKeyboard;
	private int caret;
	private bool selectAll;
	private float nextErase;
	private bool previousCursor;
	private CursorLockMode previousLock;
	private bool wasInRoom;
	private GUIStyle nameStyle;
	private GUIStyle bubbleStyle;
	private GUIStyle inputStyle;
	private GUIStyle textStyle;
	private Texture2D localBubble;
	private Texture2D remoteBubble;
	private Texture2D inputBackground;
	private Font chatFont;

	private void Update() {
		if (textKeyboard != Keyboard.current) {
			if (textKeyboard != null) textKeyboard.onTextInput -= TextInput;
			textKeyboard = Keyboard.current;
			if (textKeyboard != null) textKeyboard.onTextInput += TextInput;
		}
		if (!MPCore.IsInLobby) {
			if (IsOpen) SetOpen(false);
			if (wasInRoom) { history.Clear(); receivedAt.Clear(); }
			wasInRoom = false;
			return;
		}
		wasInRoom = true;
		history.RemoveAll(entry => Time.unscaledTime - entry.Time >= MessageLifetime);
		var keyboard = Keyboard.current;
		if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) Enter();
		if (IsOpen && keyboard?.escapeKey.wasPressedThisFrame == true) SetOpen(false);
		if (IsOpen) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; EditInput(keyboard); }
	}
	private void TextInput(char value) {
		if (!IsOpen || char.IsControl(value) || textKeyboard == null || textKeyboard.ctrlKey.isPressed || textKeyboard.altKey.isPressed) return;
		Insert(value.ToString());
	}
	private void Insert(string value) {
		if (selectAll) { input = ""; caret = 0; selectAll = false; }
		value = (value ?? "").Replace("\r", "").Replace("\n", " ");
		if (value.Length > 256 - input.Length) value = value.Substring(0, 256 - input.Length);
		input = input.Insert(caret, value); caret += value.Length;
	}
	private void EditInput(Keyboard keyboard) {
		if (keyboard == null) return;
		bool control = keyboard.ctrlKey.isPressed;
		if (control && keyboard.aKey.wasPressedThisFrame) selectAll = true;
		if (control && keyboard.cKey.wasPressedThisFrame && selectAll) GUIUtility.systemCopyBuffer = input;
		if (control && keyboard.xKey.wasPressedThisFrame && selectAll) { GUIUtility.systemCopyBuffer = input; input = ""; caret = 0; selectAll = false; }
		if (control && keyboard.vKey.wasPressedThisFrame) Insert(GUIUtility.systemCopyBuffer);
		if (keyboard.leftArrowKey.wasPressedThisFrame) { caret = Mathf.Max(0, caret - 1); selectAll = false; }
		if (keyboard.rightArrowKey.wasPressedThisFrame) { caret = Mathf.Min(input.Length, caret + 1); selectAll = false; }
		if (keyboard.homeKey.wasPressedThisFrame) { caret = 0; selectAll = false; }
		if (keyboard.endKey.wasPressedThisFrame) { caret = input.Length; selectAll = false; }
		bool pressed = keyboard.backspaceKey.wasPressedThisFrame || keyboard.deleteKey.wasPressedThisFrame;
		if (pressed || Time.unscaledTime >= nextErase && (keyboard.backspaceKey.isPressed || keyboard.deleteKey.isPressed)) {
			nextErase = Time.unscaledTime + (pressed ? 0.4f : 0.05f);
			if (selectAll) { input = ""; caret = 0; selectAll = false; }
			else if (keyboard.backspaceKey.isPressed && caret > 0) { input = input.Remove(caret - 1, 1); caret--; }
			else if (keyboard.deleteKey.isPressed && caret < input.Length) input = input.Remove(caret, 1);
		}
	}

	private void Enter() {
		if (!MPCore.IsInLobby || LanMenu.IsOpen || ManualSaves.IsOpen || ManualSaves.Loading || CommandConsole.IsConsoleVisible() || Time.unscaledTime - lastKey < 0.18f) return;
		lastKey = Time.unscaledTime;
		if (!IsOpen) SetOpen(true);
		else if (string.IsNullOrWhiteSpace(input)) SetOpen(false);
		else Send();
	}

	private void SetOpen(bool value) {
		if (IsOpen == value) return;
		IsOpen = value;
		blockUntil = Time.unscaledTime + 0.15f;
		if (value) { previousCursor = Cursor.visible; previousLock = Cursor.lockState; caret = input.Length; selectAll = false; }
		else { Cursor.lockState = previousLock; Cursor.visible = previousCursor; }
	}

	private void Send() {
		if (Time.unscaledTime - lastSent < 0.3f) return;
		string message = Clean(input, 256);
		if (message.Length == 0) return;
		lastSent = Time.unscaledTime;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.ChatMessage);
		writer.Put(message);
		MPSteamworks.Instance.Broadcast(writer);
		Append(MPSteamworks.Instance.LocalDisplayName, message, true);
		input = ""; caret = 0;
		SetOpen(false);
	}

	public static void Receive(ulong sender, string message) {
		if (receivedAt.TryGetValue(sender, out var last) && Time.unscaledTime - last < 0.25f) return;
		message = Clean(message, 256);
		if (message.Length == 0) return;
		receivedAt[sender] = Time.unscaledTime;
		Append(MPSteamworks.Instance.GetMemberName(sender), message, false);
	}

	private static string Clean(string value, int length) {
		value = (value ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
		return value.Length > length ? value.Substring(0, length) : value;
	}
	private static void Append(string name, string message, bool local) {
		name = Clean(name, 40);
		history.Add(new Entry { Name = name, Message = message, Local = local, Text = name + ": " + message, Time = Time.unscaledTime });
		if (history.Count > 80) history.RemoveAt(0);
	}
	private static Texture2D MakeBubble(Color32 color, int radius) {
		const int size = 32;
		var image = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
		var pixels = new Color32[size * size];
		for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) {
			float dx = Mathf.Max(radius - x - 0.5f, x - (size - radius) + 0.5f, 0f);
			float dy = Mathf.Max(radius - y - 0.5f, y - (size - radius) + 0.5f, 0f);
			float coverage = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
			pixels[y * size + x] = new Color32(color.r, color.g, color.b, (byte)(color.a * coverage));
		}
		image.SetPixels32(pixels); image.Apply(); return image;
	}
	private void PrepareStyles() {
		if (nameStyle != null) return;
		chatFont = Font.CreateDynamicFontFromOSFont("Arial", 20);
		localBubble = MakeBubble(new Color32(255, 239, 217, 225), 9);
		remoteBubble = MakeBubble(new Color32(255, 225, 224, 225), 9);
		inputBackground = MakeBubble(new Color32(6, 6, 6, 235), 2);
		nameStyle = new GUIStyle(GUI.skin.label) { font = chatFont, fontSize = 17, fontStyle = FontStyle.Bold, richText = false, alignment = TextAnchor.MiddleLeft };
		nameStyle.normal.textColor = new Color(0.98f, 0.91f, 0.81f);
		bubbleStyle = new GUIStyle(GUI.skin.box) { font = chatFont, fontSize = 18, fontStyle = FontStyle.Bold, richText = false, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 12, 6, 6), border = new RectOffset(10, 10, 10, 10) };
		bubbleStyle.normal.textColor = new Color(0.06f, 0.04f, 0.04f);
		inputStyle = new GUIStyle(GUI.skin.textField) { font = chatFont, fontSize = 19, richText = false, padding = new RectOffset(12, 12, 9, 9), border = new RectOffset(4, 4, 4, 4) };
		inputStyle.normal.background = inputBackground; inputStyle.focused.background = inputBackground; inputStyle.hover.background = inputBackground; inputStyle.normal.textColor = Color.white; inputStyle.focused.textColor = Color.white;
		textStyle = new GUIStyle(GUI.skin.label) { font = chatFont, fontSize = 19, richText = false, alignment = TextAnchor.MiddleLeft, padding = new RectOffset() };
		textStyle.normal.textColor = Color.white;
	}

	private void OnGUI() {
		if (!MPCore.IsInLobby) return;
		if (Event.current.type == EventType.KeyDown) {
			if (IsOpen) Event.current.Use();
		}
		PrepareStyles();
		var previous = GUI.matrix;
		float scale = Mathf.Clamp(Screen.height / 900f, 0.7f, 2f);
		GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
		float screenHeight = Screen.height / scale;
		float screenWidth = Screen.width / scale;
		float width = Mathf.Min(520, screenWidth - 32);
		float left = (screenWidth - width) / 2;
		float inputY = screenHeight - 105;
		float bottom = IsOpen ? inputY - 8 : screenHeight - 110;
		var previousColor = GUI.color;
		GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * 0.92f);
		for (int i = history.Count - 1, drawn = 0; i >= 0 && drawn < 6; i--) {
			var entry = history[i];
			if (Time.unscaledTime - entry.Time >= MessageLifetime) continue;
			var content = new GUIContent(entry.Message);
			float bubbleWidth = Mathf.Clamp(bubbleStyle.CalcSize(content).x, 44, width - 12);
			float bubbleHeight = Mathf.Max(32, bubbleStyle.CalcHeight(content, bubbleWidth));
			float top = bottom - bubbleHeight - 23;
			if (top < 15) break;
			GUI.Label(new Rect(left + 2, top, width - 4, 22), entry.Name, nameStyle);
			bubbleStyle.normal.background = entry.Local ? localBubble : remoteBubble;
			GUI.Box(new Rect(left, top + 23, bubbleWidth, bubbleHeight), content, bubbleStyle);
			bottom = top - 9;
			drawn++;
		}
		GUI.color = previousColor;
		if (IsOpen) {
			GUI.Box(new Rect(left, inputY, width, 42), "", inputStyle);
			float cursorX = textStyle.CalcSize(new GUIContent(input.Substring(0, caret))).x;
			float offset = Mathf.Max(0, cursorX - width + 32);
			GUI.BeginGroup(new Rect(left + 12, inputY, width - 24, 42));
			if (selectAll) GUI.DrawTexture(new Rect(-offset, 9, textStyle.CalcSize(new GUIContent(input)).x, 24), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.3f, 0.4f, 0.6f, 0.5f), 0, 0);
			GUI.Label(new Rect(-offset, 0, Mathf.Max(width, cursorX + width), 42), input, textStyle);
			if ((int)(Time.unscaledTime * 2) % 2 == 0) GUI.DrawTexture(new Rect(cursorX - offset, 10, 1, 22), Texture2D.whiteTexture);
			GUI.EndGroup();
		}
		GUI.matrix = previous;
	}
	private void OnDisable() { if (IsOpen) SetOpen(false); blockUntil = 0; }
	private void OnDestroy() { if (textKeyboard != null) textKeyboard.onTextInput -= TextInput; if (localBubble != null) Destroy(localBubble); if (remoteBubble != null) Destroy(remoteBubble); if (inputBackground != null) Destroy(inputBackground); if (chatFont != null) Destroy(chatFont); }
}
