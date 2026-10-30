using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WKMPMod.Core;

namespace WKMPMod.Gameplay;

public sealed partial class RussianGame {
	private sealed class TextSource { public WeakReference<UnityEngine.Component> Component; public string Original; public string Rendered; public Font Font; public float Maximum; }
	private static ConfigEntry<string> language;
	private static Dictionary<string, string> english;
	private static readonly Dictionary<int, TextSource> textSources = new Dictionary<int, TextSource>();
	private static readonly Dictionary<Perk, string[]> perkSources = new Dictionary<Perk, string[]>();
	private static readonly Dictionary<Trinket, string[]> trinketSources = new Dictionary<Trinket, string[]>();
	private static readonly Dictionary<ProgressionUnlock, string> hintSources = new Dictionary<ProgressionUnlock, string>();
	private static bool refreshing;
	private static int userTextDepth;
	public static bool PreservingUserText => userTextDepth > 0;
	public readonly struct UserTextScope : IDisposable { public UserTextScope(bool active) { userTextDepth++; } public void Dispose() { userTextDepth = Math.Max(0, userTextDepth - 1); } }
	private static readonly Dictionary<string, string> englishFragments = new Dictionary<string, string> {
		{ "Высота:", "Height:" }, { "Игроков:", "Players:" }, { "Цепь:", "Chain:" }, { "Без цепи", "No chain" }, { "С цепью", "With chain" }, { "Синхрон врагов", "Enemy sync" }, { "Карта хоста", "Host map" },
		{ "Еда. Восстанавливает здоровье:", "Food. Restores health:" }, { "Восстанавливает выносливость:", "Restores stamina:" }, { "Восстанавливает здоровье:", "Restores health:" }, { "Расходуемый предмет.", "Consumable item." },
		{ "Скорость перемещения:", "Movement speed:" }, { "Скорость лазания:", "Climbing speed:" }, { "Высота прыжка:", "Jump height:" }, { "Восстановление выносливости:", "Stamina regeneration:" }, { "Максимальная выносливость:", "Maximum stamina:" }, { "Гравитация:", "Gravity:" }, { "Сопротивление урону:", "Damage resistance:" }, { "Получаемый урон:", "Incoming damage:" }, { "Дальность захвата:", "Reach:" },
		{ "Диск для терминалов. Вместимость:", "Disk for terminals. Capacity:" }, { "Стрелковое оружие.", "Firearm." }, { "Боеприпасов:", "Ammo:" }
	};
	public static event Action LanguageChanged;
	public static bool IsRussian => (language ??= MPMain.Instance.Config.Bind("Interface", "Language", "en")).Value != "en";
	private static void InitializeEnglish(Dictionary<string, string> data) { english = new Dictionary<string, string>(); foreach (var pair in data) if (!english.ContainsKey(pair.Value)) english[pair.Value] = pair.Key; }
	private static string English(string text) {
		if (!text.Any(character => character >= 'А' && character <= 'я' || character == 'ё' || character == 'Ё')) return text;
		_ = Catalog;
		if (english.TryGetValue(text, out string exact)) return exact;
		string result = string.Join("", markup.Split(text).Select(part => {
			if (part.StartsWith("<", StringComparison.Ordinal)) return part;
			string trimmed = part.Trim(); if (trimmed.Length > 0 && english.TryGetValue(trimmed, out var original)) return part.Replace(trimmed, original);
			foreach (var pair in prefixes) if (trimmed.StartsWith(pair.Value, StringComparison.Ordinal)) return part.Replace(pair.Value, pair.Key).Replace(" м/с", " m/s");
			return part;
		}));
		foreach (var pair in englishFragments) result = result.Replace(pair.Key, pair.Value);
		return result;
	}
	public static void LocalizeText(UnityEngine.Component component, ref string text) {
		if (component == null || component.GetComponentInParent<TMP_InputField>(true) != null || component.GetComponentInParent<InputField>(true) != null) return;
		if (PreservingUserText || component.GetComponentInParent<WKMPMod.Component.RPContainerRef>(true) != null) { textSources.Remove(component.GetInstanceID()); return; }
		int id = component.GetInstanceID();
		if (!textSources.TryGetValue(id, out var source) || !source.Component.TryGetTarget(out var previous) || previous != component) textSources[id] = source = new TextSource { Component = new WeakReference<UnityEngine.Component>(component), Original = English(text ?? ""), Font = (component as Text)?.font, Maximum = component is TMP_Text size ? size.enableAutoSizing ? size.fontSizeMax : size.fontSize : 20 };
		else if (!refreshing && text != source.Rendered) source.Original = English(text ?? "");
		text = Translate(source.Original); source.Rendered = text;
		if (component is TMP_Text styled) PixelRussianFont.Fit(styled, source.Maximum);
		if (component is Text legacy) legacy.font = IsRussian && text.Any(character => character >= 'А' && character <= 'я' || character == 'ё' || character == 'Ё') ? LegacyFont : source.Font;
	}
	private static void LocalizePerk(Perk perk) {
		if (perk == null) return;
		if (!perkSources.TryGetValue(perk, out var original)) perkSources[perk] = original = new[] { perk.title, perk.description, perk.flavorText };
		perk.title = Translate(original[0]); perk.description = Translate(original[1]); perk.flavorText = Translate(original[2]);
	}
	private static void LocalizeTrinket(Trinket trinket) {
		if (trinket == null) return;
		if (!trinketSources.TryGetValue(trinket, out var original)) trinketSources[trinket] = original = new[] { trinket.title, trinket.description, trinket.flavorText };
		trinket.title = Translate(original[0]); trinket.description = Translate(original[1]); trinket.flavorText = Translate(original[2]);
		if (trinket.progressionUnlock != null) { if (!hintSources.ContainsKey(trinket.progressionUnlock)) hintSources[trinket.progressionUnlock] = trinket.progressionUnlock.unlockHint; trinket.progressionUnlock.unlockHint = Translate(hintSources[trinket.progressionUnlock]); }
	}
	public static void SetRussian(bool value) {
		_ = IsRussian;
		language.Value = value ? "ru" : "en"; cache.Clear();
		foreach (var perk in perkSources.Keys.ToArray()) if (perk != null) LocalizePerk(perk);
		foreach (var trinket in trinketSources.Keys.ToArray()) if (trinket != null) LocalizeTrinket(trinket);
		refreshing = true;
		try {
			foreach (var pair in textSources.ToArray()) {
				if (!pair.Value.Component.TryGetTarget(out var component) || component == null) { textSources.Remove(pair.Key); continue; }
				string text = Translate(pair.Value.Original); pair.Value.Rendered = text;
				if (component is TMP_Text tmp) tmp.text = text;
				if (component is Text legacy) { legacy.text = text; legacy.font = IsRussian ? LegacyFont : pair.Value.Font; }
			}
		} finally { refreshing = false; }
		LanguageChanged?.Invoke();
	}
}

[HarmonyPatch]
public static class Patch_GameGuiLanguage {
	private static IEnumerable<MethodBase> TargetMethods() => new[] { typeof(GUI), typeof(GUILayout) }.SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public)).Where(method => (method.Name == "Label" || method.Name == "Button" || method.Name == "Box" || method.Name == "Window") && method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string) && (parameter.Name == "text" || parameter.Name == "title")));
	private static void Prefix(MethodBase __originalMethod, object[] __args) { if (RussianGame.PreservingUserText) return; var parameters = __originalMethod.GetParameters(); for (int i = 0; i < parameters.Length; i++) if (parameters[i].ParameterType == typeof(string) && (parameters[i].Name == "text" || parameters[i].Name == "title")) __args[i] = RussianGame.Translate((string)__args[i]); }
}

[HarmonyPatch(typeof(WKMPMod.UI.MultiplayerChat), "OnGUI")]
public static class Patch_ChatOriginalLanguage {
	private static void Prefix(out RussianGame.UserTextScope __state) { __state = new RussianGame.UserTextScope(true); }
	private static Exception Finalizer(Exception __exception, RussianGame.UserTextScope __state) { __state.Dispose(); return __exception; }
}
