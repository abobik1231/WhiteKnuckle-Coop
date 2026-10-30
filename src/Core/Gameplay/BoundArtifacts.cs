using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using WKMPMod.Component;
using WKMPMod.Core;
using WKMPMod.NetWork;
using WKMPMod.UI;
using WKMPMod.World;

namespace WKMPMod.Gameplay;

public sealed class BoundArtifacts : MonoBehaviour {
	public const string Tag = "wkmp-bound-artifact";
	private const string RevisionTag = "wkmp-artifact-revision";
	public sealed class Entry { public string Item; public int Revision; }
	private static readonly Dictionary<string, Entry> owned = new Dictionary<string, Entry>();
	private static readonly HashSet<string> restored = new HashSet<string>();
	private float nextScan;
	private static readonly Dictionary<string, float> missingSince = new Dictionary<string, float>();
	private void Awake() { SceneManager.sceneLoaded += SceneLoaded; }
	private void OnDestroy() { SceneManager.sceneLoaded -= SceneLoaded; owned.Clear(); restored.Clear(); }
	private static void SceneLoaded(Scene scene, LoadSceneMode mode) { if (!ManualSaves.Loading) { owned.Clear(); restored.Clear(); } }
	public static bool IsArtifact(Item item) => item != null && !string.IsNullOrEmpty(item.prefabName) && item.prefabName.StartsWith("Item_Artifact_", StringComparison.OrdinalIgnoreCase);
	public static IEnumerable<Item> Items() => Inventory.instance == null ? Enumerable.Empty<Item>() : Inventory.instance.GetItems(true, true, true).Concat(Inventory.instance.GetAllPouches().Where(pouch => pouch?.pouchItems != null).SelectMany(pouch => pouch.pouchItems)).Where(item => item != null).Distinct();
	private static int Revision(Item item) => int.TryParse(item.GetFirstDataStringByType(RevisionTag), out int revision) ? Math.Max(0, revision) : 0;
	public static void Remember(Item item) {
		if (!IsArtifact(item) || ManualSaves.Loading || item.hasBeenDestroyed) return;
		var physical = item.GetDropObject(false);
		if (physical != null) {
			if (physical.GetComponentInParent<RemoteHand>(true) != null || physical.GetComponentInParent<RPContainerRef>(true) != null) return;
			var identity = physical.GetComponent<NetworkedItem>();
			if (identity != null && identity.networkId != 0 && identity.ownerId != MPSteamworks.UserSteamId) return;
		}
		string id = item.GetFirstDataStringByType(Tag);
		if (!Guid.TryParseExact(id, "N", out _)) { id = Guid.NewGuid().ToString("N"); item.SetFirstDataStringsofType(Tag, id); }
		owned[id] = new Entry { Item = item.GetSaveInfo().data, Revision = Revision(item) };
	}
	public static void Relinquish(Item item) { if (item != null) { string id = item.GetFirstDataStringByType(Tag) ?? ""; owned.Remove(id); missingSince.Remove(id); } }
	public static void Transferred(Item item) { if (!IsArtifact(item)) return; item.SetFirstDataStringsofType(RevisionTag, (Math.Min(int.MaxValue - 1, Revision(item)) + 1).ToString()); Remember(item); }
	private void Update() {
		if (ManualSaves.Loading || Inventory.instance == null || ENT_Player.GetPlayer() == null || ENT_Player.GetPlayer().IsDead() || Time.unscaledTime < nextScan) return;
		nextScan = Time.unscaledTime + 2;
		foreach (var item in Items()) Remember(item);
		if (!ManualSaves.GroupLoading) RecoverMissing();
	}
	private static void RecoverMissing() {
		var current = new HashSet<string>(Items().Where(IsArtifact).Select(item => item.GetFirstDataStringByType(Tag)));
		foreach (var pair in owned.ToArray()) {
			if (current.Contains(pair.Key)) { missingSince.Remove(pair.Key); continue; }
			if (!missingSince.TryGetValue(pair.Key, out float since)) { missingSince[pair.Key] = Time.unscaledTime; continue; }
			if (Time.unscaledTime - since < 8 || DroppedItemModule.Instance.PendingPickups) continue;
			var item = Item.CreateFromSaveInfo(new SaveableInfo { data = pair.Value.Item }); if (item == null) continue;
			if (!DroppedItemModule.Instance.RecoverBoundArtifact(pair.Key)) continue;
			Inventory.instance.AddItemToInventoryCenter(item); missingSince.Remove(pair.Key); current.Add(pair.Key);
			WKMPMod.Core.MPMain.LogInfo("[Equipment] Recovered missing bound artifact " + item.prefabName);
		}
	}
	public static string Capture() {
		foreach (var item in Items()) Remember(item);
		foreach (var item in DroppedItemModule.Instance.LocalWorldItems()) if (IsArtifact(item)) Remember(item);
		return JsonConvert.SerializeObject(owned);
	}
	public static void Restore(string json) {
		owned.Clear();
		missingSince.Clear();
		if (!string.IsNullOrEmpty(json)) {
			var saved = JsonConvert.DeserializeObject<Dictionary<string, Entry>>(json);
			if (saved == null || saved.Count > 128) throw new InvalidOperationException("Неверные данные закреплённых легендарок.");
			foreach (var pair in saved) {
				if (!Guid.TryParseExact(pair.Key, "N", out _) || pair.Value?.Item == null || pair.Value.Item.Length > 100000) throw new InvalidOperationException("Повреждено закрепление легендарки.");
				var item = JsonUtility.FromJson<Item>(pair.Value.Item);
				if (!IsArtifact(item) || item.GetFirstDataStringByType(Tag) != pair.Key) throw new InvalidOperationException("Повреждены данные легендарки.");
				owned[pair.Key] = pair.Value;
			}
		}
		var existing = new HashSet<string>(Items().Where(IsArtifact).Select(item => item.GetFirstDataStringByType(Tag)));
		foreach (var pair in owned.ToArray()) {
			if (existing.Contains(pair.Key)) continue;
			var item = Item.CreateFromSaveInfo(new SaveableInfo { data = pair.Value.Item });
			if (item == null) throw new InvalidOperationException("Не найден предмет легендарки.");
			Inventory.instance.AddItemToInventoryCenter(item);
			existing.Add(pair.Key);
		}
		foreach (var item in UnityEngine.Object.FindObjectsByType<Item_Object>(FindObjectsInactive.Include, FindObjectsSortMode.None)) SuppressSavedWorld(item);
	}
	public static void BeginRestore(ManualPlayerSave player) {
		restored.Clear();
		if (!string.IsNullOrEmpty(player.Artifacts)) foreach (string id in JsonConvert.DeserializeObject<Dictionary<string, Entry>>(player.Artifacts).Keys) restored.Add(id);
		if (player.OtherArtifacts != null) foreach (string id in player.OtherArtifacts) restored.Add(id);
	}
	public static void SuppressSavedWorld(Item_Object item) {
		if (item?.itemData == null || !IsArtifact(item.itemData) || !restored.Contains(item.itemData.GetFirstDataStringByType(Tag) ?? "")) return;
		if (item.GetComponentInParent<RemoteHand>(true) != null || item.GetComponentInParent<RPContainerRef>(true) != null || Items().Contains(item.itemData)) return;
		if (item.GetComponent<NetworkedItem>() is { networkId: not 0 }) return;
		item.gameObject.SetActive(false);
		UnityEngine.Object.Destroy(item.gameObject);
	}
	public static void Normalize(ManualRunSave save) {
		var bindings = save.Players.Select(player => string.IsNullOrEmpty(player.Artifacts) ? new Dictionary<string, Entry>() : JsonConvert.DeserializeObject<Dictionary<string, Entry>>(player.Artifacts)).ToArray();
		var winners = new Dictionary<string, (int Player, int Revision)>();
		for (int i = 0; i < bindings.Length; i++) foreach (var pair in bindings[i]) if (!winners.TryGetValue(pair.Key, out var previous) || pair.Value.Revision > previous.Revision) winners[pair.Key] = (i, pair.Value.Revision);
		for (int i = 0; i < bindings.Length; i++) {
			int owner = i;
			var rejected = new HashSet<string>(bindings[i].Keys.Where(id => winners[id].Player != owner));
			foreach (string id in rejected) bindings[i].Remove(id);
			save.Players[i].Artifacts = JsonConvert.SerializeObject(bindings[i]);
			Exclude(save.Players[i], rejected);
		}
	}
	public static List<string> ReservedByOthers(ManualRunSave save, string profile) => save.Players.Where(player => player.Profile != profile && !string.IsNullOrEmpty(player.Artifacts)).SelectMany(player => JsonConvert.DeserializeObject<Dictionary<string, Entry>>(player.Artifacts).Keys).Distinct().ToList();
	public static void Exclude(ManualPlayerSave player, IEnumerable<string> excluded) {
		if (excluded == null) return;
		var rejected = new HashSet<string>(excluded);
		if (rejected.Count == 0) return;
		if (!string.IsNullOrEmpty(player.Artifacts)) {
			var bindings = JsonConvert.DeserializeObject<Dictionary<string, Entry>>(player.Artifacts);
			foreach (string id in rejected) bindings.Remove(id);
			player.Artifacts = JsonConvert.SerializeObject(bindings);
		}
		var session = JsonUtility.FromJson<CL_SaveManager.SessionSave>(player.Session);
		bool Keep(Item item) => item == null || !rejected.Contains(item.GetFirstDataStringByType(Tag));
		void Clean(ENT_Player.SaveData data) {
			if (data == null) return;
			if (data.inventory != null) data.inventory = data.inventory.Where(Keep).ToArray();
			if (data.handInventory != null) for (int hand = 0; hand < data.handInventory.Length; hand++) if (!Keep(data.handInventory[hand])) data.handInventory[hand] = null;
			foreach (var pouch in (data.pouchInventory ?? Array.Empty<Inventory.Pouch.PouchSaveData>()).Concat(data.pocketInventory ?? Array.Empty<Inventory.Pouch.PouchSaveData>())) pouch?.items?.RemoveAll(item => !Keep(item));
		}
		Clean(session.playerSave.playerData);
		foreach (var state in session.saveStates) Clean(state.playerData);
		Clean(session.checkpointState?.playerData);
		player.Session = JsonUtility.ToJson(session);
	}
}

[HarmonyPatch(typeof(Item_Object), nameof(Item_Object.SetSaveInfo))]
public static class Patch_BoundArtifactWorldSave {
	private static void Postfix(Item_Object __instance) { BoundArtifacts.SuppressSavedWorld(__instance); }
}

[HarmonyPatch]
public static class Patch_BindArtifacts {
	private static IEnumerable<MethodBase> TargetMethods() {
		yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.AddItemToHand), new[] { typeof(Item), typeof(ENT_Player.Hand) });
		yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.AddItemToInventoryScreen));
	}
	private static void Postfix(object[] __args) { foreach (var arg in __args) if (arg is Item item) BoundArtifacts.Remember(item); }
}
