using System;
using System.Collections;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace WKMPMod.Gameplay;

public static class CoopEnding {
	public static bool Running;
	public static bool ReachedMenu;
	private static TMP_Text title;
	private static string previousTitle;
	public static IEnumerator Finish(IEnumerator original) {
		Running = true; ReachedMenu = false;
		try {
			while (true) {
				bool more;
				try { more = original.MoveNext(); }
				catch (Exception ex) { WKMPMod.Core.MPMain.LogError("[Ending] Native completion failed: " + ex); ReachedMenu = true; break; }
				if (!more || ReachedMenu) break;
				yield return original.Current;
			}
		}
		finally { (original as IDisposable)?.Dispose(); Running = false; }
		if (ReachedMenu) Open();
	}
	public static void Open() {
		var game = CL_GameManager.gMan; if (game == null) return;
		game.isPaused = true; CL_GameManager.SetTimescale(1); AudioListener.pause = false;
		var player = ENT_Player.GetPlayer(); player?.SetCutsceneMode(false); player?.UnlockInteraction();
		if (CL_UIManager.instance != null) { CL_UIManager.instance.SetFadeAmount(0); CL_UIManager.instance.SetFadeTarget(0); CL_UIManager.instance.SetVignette("fade", 0); CL_UIManager.instance.SetVignetteTarget("fade", 0); CL_UIManager.instance.canvas.gameObject.SetActive(true); }
		game.pauseMenu.SetActive(true); CL_GameManager.showCursor = true; Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
		foreach (var text in game.pauseMenu.GetComponentsInChildren<TMP_Text>(true)) if (string.Equals(RussianGame.Translate(text.text), RussianGame.Translate("PAUSED"), StringComparison.OrdinalIgnoreCase)) { title = text; previousTitle = text.text; text.text = "RUN COMPLETE"; break; }
	}
	public static void Reset() { Running = false; ReachedMenu = false; if (title != null) title.text = previousTitle; title = null; }
}

[HarmonyPatch(typeof(CL_GameManager), "EndGameSequence")]
public static class Patch_EndingMenu {
	private static void Postfix(bool win, ref IEnumerator __result) { if (win) __result = CoopEnding.Finish(__result); }
}
