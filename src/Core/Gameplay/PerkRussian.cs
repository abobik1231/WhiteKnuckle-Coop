using System.Collections.Generic;
using HarmonyLib;
using System.IO;
using TMPro;
using UnityEngine;

namespace WKMPMod.Gameplay;

public static class PerkRussian {
	private static AssetBundle fontBundle;
	private static TMP_FontAsset font;
	public static void Apply(Perk perk) { EnsureFont(); RussianGame.Apply(perk); }
	public static void EnsureFont() {
		try {
			var fonts = TMP_Settings.fallbackFontAssets;
			if (fonts == null) return;
			if (font == null) {
				string path = Path.Combine(BepInEx.Paths.PluginPath, "SimpleMultiPlayer", "oswald_ru_sdf");
				if (!File.Exists(path)) return;
				fontBundle = AssetBundle.LoadFromFile(path);
				if (fontBundle == null) return;
				var assets = fontBundle.LoadAllAssets<TMP_FontAsset>();
				if (assets.Length == 0) return;
				font = assets[0];
				Object.DontDestroyOnLoad(font);
			}
			if (!fonts.Contains(font)) fonts.Add(font);
		} catch (System.Exception ex) { WKMPMod.Core.MPMain.LogWarning("Perk font: " + ex.Message); }
	}
}

[HarmonyPatch(typeof(Perk), nameof(Perk.GetTitle))]
public static class Patch_PerkTitleRussian {
	private static void Prefix(Perk __instance) { PerkRussian.Apply(__instance); }
}

[HarmonyPatch(typeof(Perk), nameof(Perk.GetDescription))]
public static class Patch_PerkDescriptionRussian {
	private static void Prefix(Perk __instance) { PerkRussian.Apply(__instance); }
}

[HarmonyPatch(typeof(CL_AssetManager), nameof(CL_AssetManager.InitializeDatabases))]
public static class Patch_PerkDatabaseRussian {
	private static void Postfix() {
		if (CL_AssetManager.baseDatabase?.perkAssets == null) return;
		foreach (var perk in CL_AssetManager.baseDatabase.perkAssets) PerkRussian.Apply(perk);
		if (CL_AssetManager.baseDatabase.trinketAssets != null) foreach (var trinket in CL_AssetManager.baseDatabase.trinketAssets) RussianGame.Apply(trinket);
	}
}
