using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WKMPMod.Gameplay;

public sealed partial class RussianGame : MonoBehaviour {
	private static Dictionary<string, string> catalog;
	private static Dictionary<string, string> normalized;
	private static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
	private static readonly Regex markup = new Regex("(<[^>]+>)", RegexOptions.Compiled);
	private static readonly Dictionary<string, string> prefixes = new Dictionary<string, string> {
		{ "Score:", "Счёт:" }, { "High Score:", "Рекорд:" }, { "Climb Distance:", "Пройдено:" }, { "Climb Speed:", "Скорость подъёма:" },
		{ "Total Climbed:", "Всего пройдено:" }, { "Highest Ascent:", "Максимальная высота:" }, { "Best Distance:", "Лучший результат:" },
		{ "Runs:", "Забеги:" }, { "Deaths:", "Смерти:" }, { "Total Deaths:", "Всего смертей:" }, { "Total Items Used:", "Использовано предметов:" },
		{ "Rate of Ascent:", "Скорость подъёма:" }, { "Average Rate of Ascent:", "Средняя скорость подъёма:" }, { "Average Ascent Rate:", "Средняя скорость подъёма:" },
		{ "Fastest Rate of Ascent:", "Максимальная скорость подъёма:" }, { "Average Speed:", "Средняя скорость:" }, { "Lifetime Roaches Stored:", "Всего сдано тараканов:" },
		{ "Ascent Rate:", "Скорость подъёма:" }, { "Total Playtime:", "Общее время игры:" },
		{ "Food Eaten:", "Съедено еды:" }, { "Pills Taken:", "Принято таблеток:" }, { "Injectors Used:", "Использовано инъекторов:" },
		{ "Roaches Crushed:", "Раздавлено тараканов:" }, { "Pitons Placed:", "Установлено крюков:" }, { "Rebar Thrown:", "Брошено арматуры:" },
		{ "Rope Placed:", "Установлено верёвок:" }, { "Flares Shot:", "Выпущено сигнальных ракет:" }, { "Explosives Detonated:", "Произведено взрывов:" },
		{ "Trinket:", "Талисман:" }, { "Binding:", "Ограничение:" }, { "Challenge:", "Испытание:" }, { "Unlock Requirement:", "Условие открытия:" }
	};
	private static Dictionary<string, string> Catalog {
		get {
			if (catalog != null) return catalog;
			var assembly = typeof(RussianGame).Assembly;
			using var reader = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("game_ru.json", StringComparison.Ordinal))));
			catalog = JsonConvert.DeserializeObject<Dictionary<string, string>>(reader.ReadToEnd());
			normalized = new Dictionary<string, string>(); foreach (var pair in catalog) normalized[Normalize(pair.Key)] = pair.Value;
			InitializeEnglish(catalog);
			return catalog;
		}
	}
	private static string Normalize(string text) => text.Replace("\r", "").Replace("’", "'").Replace("‘", "'").Replace("…", "...").Replace("\uFFFD", "...").Trim();
	public static string Translate(string text) {
		if (string.IsNullOrEmpty(text)) return text;
		if (!IsRussian) return English(text);
		if (Catalog.TryGetValue(text, out string direct)) return direct;
		if (normalized.TryGetValue(Normalize(text), out direct)) return direct;
		if (cache.TryGetValue(text, out var cached)) return cached;
		string clean = text.Replace("\r", "");
		string trimmed = clean.Trim();
		if (Catalog.TryGetValue(trimmed, out direct)) return clean.Substring(0, clean.IndexOf(trimmed, StringComparison.Ordinal)) + direct + clean.Substring(clean.IndexOf(trimmed, StringComparison.Ordinal) + trimmed.Length);
		string result = string.Join("", markup.Split(clean).Select(part => part.StartsWith("<", StringComparison.Ordinal) ? part : Segment(part)));
		if (cache.Count >= 8192) cache.Clear();
		cache[text] = result;
		return result;
	}
	private static string Segment(string text) {
		string value = text.Trim();
		if (Catalog.TryGetValue(value, out string translated)) return text.Replace(value, translated);
		foreach (var pair in prefixes) if (value.StartsWith(pair.Key, StringComparison.Ordinal)) return text.Replace(pair.Key, pair.Value).Replace(" Meters", " м").Replace(" m/s", " м/с");
		if (text.Contains('\n')) return string.Join("\n", text.Split('\n').Select(line => Catalog.TryGetValue(line.Trim(), out var ru) && line.Trim().Length > 0 ? line.Replace(line.Trim(), ru) : line));
		return text;
	}
	public static void Apply(Perk perk) { LocalizePerk(perk); }
	public static void Apply(Trinket item) { LocalizeTrinket(item); }
	public static void TextComponent(UnityEngine.Component component) {
		if (component == null || component.GetComponentInParent<TMP_InputField>(true) != null || component.GetComponentInParent<InputField>(true) != null) return;
		PerkRussian.EnsureFont();
		if (component is TMP_Text tmp) { string value = tmp.text; LocalizeText(component, ref value); if (value != tmp.text) tmp.text = value; }
		if (component is Text legacy) { string value = legacy.text; LocalizeText(component, ref value); if (value != legacy.text) legacy.text = value; }
	}
	private static Font legacyFont;
	public static Font LegacyFont => legacyFont ??= Font.CreateDynamicFontFromOSFont("Consolas", 20);
	private void Awake() { SceneManager.sceneLoaded += Loaded; }
	private void OnDestroy() { SceneManager.sceneLoaded -= Loaded; }
	private void Loaded(Scene scene, LoadSceneMode mode) { StartCoroutine(RefreshScene()); }
	private System.Collections.IEnumerator RefreshScene() {
		yield return null;
		PerkRussian.EnsureFont();
		foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)) TextComponent(text);
		foreach (var text in UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None)) TextComponent(text);
	}
}

[HarmonyPatch]
public static class Patch_GameTextRussian {
	private static IEnumerable<MethodBase> TargetMethods() {
		return new[] { typeof(TMP_Text), typeof(TextMeshProUGUI), typeof(TextMeshPro), typeof(Text) }.Select(type => AccessTools.PropertySetter(type, "text")).Where(method => method != null).Concat(typeof(TMP_Text).GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(method => method.Name == "SetText" && method.GetParameters().FirstOrDefault()?.ParameterType == typeof(string))).Distinct();
	}
	private static void Prefix(UnityEngine.Component __instance, ref string __0) {
		RussianGame.LocalizeText(__instance, ref __0);
	}
}

[HarmonyPatch]
public static class Patch_EnabledRussianText {
	private static IEnumerable<MethodBase> TargetMethods() => new[] { typeof(TextMeshProUGUI), typeof(TextMeshPro), typeof(Text) }.Select(type => AccessTools.Method(type, "OnEnable")).Where(method => method != null).Distinct();
	private static void Postfix(UnityEngine.Component __instance) { RussianGame.TextComponent(__instance); }
}

[HarmonyPatch(typeof(CL_LocalizationManager), nameof(CL_LocalizationManager.GetLocalization))]
public static class Patch_GameLocalizationRussian { private static void Postfix(ref string __result) { __result = RussianGame.Translate(__result); } }

[HarmonyPatch]
public static class Patch_TrinketRussian {
	private static IEnumerable<MethodBase> TargetMethods() { yield return AccessTools.Method(typeof(Trinket), nameof(Trinket.GetDescription)); yield return AccessTools.Method(typeof(Trinket), nameof(Trinket.GetLockedDescription)); }
	private static void Prefix(Trinket __instance) { RussianGame.Apply(__instance); }
	private static void Postfix(ref string __result) { __result = RussianGame.Translate(__result); }
}

[HarmonyPatch(typeof(Item), nameof(Item.GetFullItemName))]
public static class Patch_ItemNameRussian { private static void Postfix(ref string __result) { __result = RussianGame.Translate(__result); } }
