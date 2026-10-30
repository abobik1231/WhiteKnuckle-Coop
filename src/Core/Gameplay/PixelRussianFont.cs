using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace WKMPMod.Gameplay;

public static class PixelRussianFont {
	private static TMP_FontAsset font;
	private static bool attempted;
	public static void Ensure() {
		if (attempted) return; attempted = true;
		try {
			var assembly = typeof(PixelRussianFont).Assembly;
			using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("Tiny5.ttf", StringComparison.Ordinal)));
			using var memory = new MemoryStream(); stream.CopyTo(memory);
			FontEngine.InitializeFontEngine(); if (FontEngine.LoadFontFace(memory.ToArray(), 64) != FontEngineError.Success) throw new InvalidOperationException("Font face could not be loaded");
			var glyphIds = new List<uint>(); var characters = new Dictionary<uint, uint>();
			foreach (uint unicode in Enumerable.Range(32, 95).Concat(Enumerable.Range(0x400, 112)).Concat(new[] { 0x2026, 0x2116, 0xB0 }).Select(value => (uint)value)) if (FontEngine.TryGetGlyphIndex(unicode, out uint index) && index != 0) { characters[unicode] = index; if (!glyphIds.Contains(index)) glyphIds.Add(index); }
			if (Enumerable.Range(0x410, 64).Concat(new[] { 0x401, 0x451 }).Any(value => !characters.ContainsKey((uint)value))) throw new InvalidOperationException("Cyrillic alphabet is incomplete");
			font = ScriptableObject.CreateInstance<TMP_FontAsset>(); font.name = "WK Pixel Cyrillic"; font.faceInfo = FontEngine.GetFaceInfo();
			AccessTools.Field(typeof(TMP_FontAsset), "m_Version").SetValue(font, "1.1.0");
			AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasWidth").SetValue(font, 1024); AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasHeight").SetValue(font, 1024); AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasPadding").SetValue(font, 5); AccessTools.Field(typeof(TMP_FontAsset), "m_AtlasRenderMode").SetValue(font, GlyphRenderMode.SDFAA); font.atlasPopulationMode = AtlasPopulationMode.Static;
			var atlas = new Texture2D(1024, 1024, TextureFormat.Alpha8, false); atlas.name = "WK Pixel Cyrillic Atlas"; atlas.SetPixels32(new Color32[1024 * 1024]); atlas.Apply();
			var method = AccessTools.Method(typeof(FontEngine), "TryAddGlyphsToTexture", new[] { typeof(List<uint>), typeof(int), typeof(GlyphPackingMode), typeof(List<GlyphRect>), typeof(List<GlyphRect>), typeof(GlyphRenderMode), typeof(Texture2D), typeof(Glyph[]).MakeByRefType() });
			object[] args = { glyphIds, 5, GlyphPackingMode.BestShortSideFit, new List<GlyphRect> { new GlyphRect(0, 0, 1023, 1023) }, new List<GlyphRect>(), GlyphRenderMode.SDFAA, atlas, null };
			if (!(bool)method.Invoke(null, args)) throw new InvalidOperationException("Glyph atlas is full");
			var glyphs = ((Glyph[])args[7]).Where(glyph => glyph != null).GroupBy(glyph => glyph.index).ToDictionary(group => group.Key, group => group.First());
			font.glyphTable.AddRange(glyphs.Values); font.characterTable.AddRange(characters.Select(pair => new TMP_Character(pair.Key, glyphs[pair.Value]))); font.atlasTextures = new[] { atlas };
			ShaderUtilities.GetShaderPropertyIDs();
			var material = new Material(TMP_Settings.defaultFontAsset.material); material.SetTexture(ShaderUtilities.ID_MainTex, atlas); material.SetFloat("_TextureWidth", 1024); material.SetFloat("_TextureHeight", 1024); material.SetFloat("_GradientScale", 6); material.SetFloat("_WeightNormal", 0); material.SetFloat("_WeightBold", .5f); font.material = material;
			font.ReadFontAssetDefinition(); UnityEngine.Object.DontDestroyOnLoad(font); UnityEngine.Object.DontDestroyOnLoad(atlas); UnityEngine.Object.DontDestroyOnLoad(material);
			TMP_Settings.fallbackFontAssets.Insert(0, font);
		} catch (Exception ex) { WKMPMod.Core.MPMain.LogError("[Font] " + ex); }
	}
	public static void Fit(TMP_Text text, float maximum) {
		var control = text.GetComponentInParent<Selectable>(true); if (control == null || control is TMP_InputField || control is InputField) return;
		text.enableAutoSizing = true; text.fontSizeMin = 9; text.fontSizeMax = Mathf.Max(10, maximum); text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Ellipsis;
		if (control is Button || control is Toggle) text.margin = new Vector4(8, 3, 8, 3);
	}
}
