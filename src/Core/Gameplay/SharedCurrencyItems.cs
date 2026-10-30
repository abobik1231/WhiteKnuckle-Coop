using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.UI;

namespace WKMPMod.Gameplay;

public sealed class SharedCurrencyItems : MonoBehaviour {
	private sealed class Message {
		public string Kind;
		public ulong Generation;
		public int Sequence;
		public Dictionary<string, string> Add = new Dictionary<string, string>();
		public List<string> Remove = new List<string>();
	}
	private const string Tag = "wkmp-shared-currency";
	private static SharedCurrencyItems instance;
	private readonly Dictionary<string, string> ledger = new Dictionary<string, string>();
	private readonly HashSet<string> spent = new HashSet<string>();
	private readonly Dictionary<string, string> observed = new Dictionary<string, string>();
	private readonly Dictionary<ulong, int> acknowledged = new Dictionary<ulong, int>();
	private readonly List<Message> pending = new List<Message>();
	private Dictionary<string, string> initial;
	private ulong room;
	private int sequence;
	private float nextScan;
	private float nextSend;
	private bool initialized;
	private bool ready;
	private bool lastLoading;
	private void Awake() { instance = this; }
	private void OnDestroy() { if (instance == this) instance = null; }
	private void ResetState() { ledger.Clear(); spent.Clear(); observed.Clear(); acknowledged.Clear(); pending.Clear(); initial = null; sequence = 0; initialized = false; ready = false; }
	internal static void IdentifyWorldCurrency(Item_Object item) {
		if (!MPCore.IsInLobby || !MPSteamworks.Instance.IsLan || item?.itemData == null || !item.itemData.HasTag("currency") || item.GetComponentInParent<M_Level>(true) == null) return;
		if (Guid.TryParseExact(item.itemData.GetFirstDataStringByType(Tag), "N", out _)) return;
		ulong key = CoopIdentity.For(item.transform);
		byte[] bytes = new byte[16];
		Array.Copy(BitConverter.GetBytes(key), 0, bytes, 0, 8);
		Array.Copy(BitConverter.GetBytes(WKMPMod.Util.MPUtil.Hash64("currency:" + key)), 0, bytes, 8, 8);
		item.itemData.SetFirstDataStringsofType(Tag, new Guid(bytes).ToString("N"));
	}
	private static Inventory GetInventory() => Inventory.instance;
	private static IEnumerable<Item> CurrencyItems(Inventory inventory) => inventory.GetItems(true, true, true).Concat(inventory.GetAllPouches().Where(pouch => pouch != null && pouch.pouchItems != null).SelectMany(pouch => pouch.pouchItems)).Where(item => item != null && item.HasTag("currency")).Distinct();
	public static string CaptureSaved() => JsonConvert.SerializeObject(ReadItems());
	public static void RestoreSaved(string json) {
		if (instance == null) return;
		instance.ResetState();
		if (string.IsNullOrEmpty(json)) return;
		var desired = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
		if (!Valid(desired) || !ValidCurrencyAssets(desired)) throw new InvalidOperationException("Неверное сохранение общей валюты.");
		instance.ApplyInventory(desired);
	}
	private static Dictionary<string, string> ReadItems() {
		var result = new Dictionary<string, string>();
		var inventory = GetInventory();
		if (inventory == null) return result;
		foreach (var item in CurrencyItems(inventory)) {
			if (item == null || string.IsNullOrEmpty(item.prefabName)) continue;
			string id = item.GetFirstDataStringByType(Tag);
			if (!Guid.TryParseExact(id, "N", out _)) { id = Guid.NewGuid().ToString("N"); item.SetFirstDataStringsofType(Tag, id); }
			result[id] = item.prefabName;
		}
		return result;
	}
	private static bool Valid(Dictionary<string, string> items) => items != null && items.Count <= 500 && items.All(pair => Guid.TryParseExact(pair.Key, "N", out _) && !string.IsNullOrEmpty(pair.Value) && pair.Value.Length <= 128);
	private static bool ValidCurrencyAssets(Dictionary<string, string> items) {
		foreach (string name in items.Values.Distinct()) {
			try { if (CL_AssetManager.GetItemObjectPrefab(name)?.itemData?.HasTag("currency") != true) return false; }
			catch { return false; }
		}
		return true;
	}
	private void Update() {
		if (!MPCore.IsInLobby || !MPSteamworks.Instance.IsLan) { if (room != 0) { ResetState(); room = 0; } return; }
		if (ManualSaves.Loading) { if (!lastLoading) ResetState(); lastLoading = true; return; }
		lastLoading = false;
		if (ManualSaves.GroupLoading) return;
		if (MPSteamworks.IsHost) foreach (ulong peer in acknowledged.Keys.Where(peer => !MPSteamworks.Instance.HasConnection(peer)).ToArray()) acknowledged.Remove(peer);
		if (!MPCore.CanSync) { if (initialized) ResetState(); return; }
		if (GetInventory() == null || ENT_Player.GetPlayer() == null || ENT_Player.GetPlayer().IsDead()) return;
		ulong currentRoom = MPSteamworks.Instance.HostSteamId;
		if (room != currentRoom) { ResetState(); room = currentRoom; }
		if (!initialized) {
			initial = ReadItems(); Copy(initial, observed);
			if (MPSteamworks.IsHost) { Copy(initial, ledger); ready = true; }
			initialized = true;
		}
		if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 0.3f; Scan(); }
		if (Time.unscaledTime >= nextSend) {
			nextSend = Time.unscaledTime + 1f;
			if (MPSteamworks.IsHost) BroadcastSnapshots();
			else {
				if (!ready) Send(MPSteamworks.Instance.HostSteamId, new Message { Kind = "init", Add = initial });
				foreach (var change in pending) Send(MPSteamworks.Instance.HostSteamId, change);
			}
		}
	}
	private static void Copy(Dictionary<string, string> source, Dictionary<string, string> target) { target.Clear(); foreach (var pair in source) target[pair.Key] = pair.Value; }
	private void Scan() {
		var now = ReadItems();
		if (!Valid(now)) return;
		var delta = new Message { Kind = "delta", Add = now.Where(pair => !observed.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value), Remove = observed.Keys.Where(id => !now.ContainsKey(id)).ToList() };
		if (delta.Add.Count == 0 && delta.Remove.Count == 0) return;
		Copy(now, observed);
		if (MPSteamworks.IsHost) { ApplyHostDelta(delta); BroadcastSnapshots(); }
		else { delta.Sequence = ++sequence; pending.Add(delta); Send(MPSteamworks.Instance.HostSteamId, delta); }
	}
	private static void ApplyDelta(Dictionary<string, string> target, Message delta) { foreach (var pair in delta.Add) target[pair.Key] = pair.Value; foreach (var id in delta.Remove) target.Remove(id); }
	private void ApplyHostDelta(Message delta) {
		foreach (var id in delta.Remove) { ledger.Remove(id); spent.Add(id); }
		foreach (var pair in delta.Add) if (!spent.Contains(pair.Key)) ledger[pair.Key] = pair.Value;
	}
	private void BroadcastSnapshots() {
		foreach (ulong peer in MPSteamworks.Instance.ConnectedPeerIds) if (acknowledged.TryGetValue(peer, out int ack)) Send(peer, new Message { Kind = "snapshot", Sequence = ack, Add = new Dictionary<string, string>(ledger) });
	}
	private static void Send(ulong target, Message message) {
		message.Generation = ManualSaves.LoadGeneration;
		string json = JsonConvert.SerializeObject(message);
		if (json.Length > 100000) return;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, target, PacketType.SharedCurrencyItems);
		writer.Put(json); MPSteamworks.Instance.SendToPeer(target, writer);
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (instance == null || !instance.initialized || !SharedProgress.Enabled || !MPSteamworks.Instance.HasConnection(sender)) return;
		string json = reader.GetString();
		if (string.IsNullOrEmpty(json) || json.Length > 100000) return;
		Message message;
		try { message = JsonConvert.DeserializeObject<Message>(json); } catch { return; }
		if (message == null || message.Generation != ManualSaves.LoadGeneration || !Valid(message.Add) || message.Remove == null || message.Remove.Count > 500 || message.Remove.Any(id => !Guid.TryParseExact(id, "N", out _))) return;
		if (MPSteamworks.IsHost) instance.HostReceive(sender, message);
		else if (sender == MPSteamworks.Instance.HostSteamId && message.Kind == "snapshot") instance.ClientReceive(message);
	}
	private void HostReceive(ulong sender, Message message) {
		if (!ValidCurrencyAssets(message.Add)) return;
		Scan();
		if (message.Kind == "init") {
			if (!acknowledged.ContainsKey(sender)) { foreach (var pair in message.Add) if (!spent.Contains(pair.Key)) ledger[pair.Key] = pair.Value; acknowledged[sender] = 0; ApplyInventory(ledger); }
			BroadcastSnapshots();
		} else if (message.Kind == "delta" && acknowledged.TryGetValue(sender, out int ack)) {
			if (message.Sequence == ack + 1) { ApplyHostDelta(message); acknowledged[sender] = message.Sequence; ApplyInventory(ledger); }
			BroadcastSnapshots();
		}
	}
	private void ClientReceive(Message message) {
		Scan();
		ready = true;
		pending.RemoveAll(change => change.Sequence <= message.Sequence);
		var desired = new Dictionary<string, string>(message.Add);
		foreach (var change in pending) ApplyDelta(desired, change);
		ApplyInventory(desired);
	}
	private void ApplyInventory(Dictionary<string, string> desired) {
		var inventory = GetInventory();
		if (inventory == null || !Valid(desired)) return;
		var existing = CurrencyItems(inventory).ToArray();
		foreach (var item in existing) {
			string id = item.GetFirstDataStringByType(Tag);
			if (Guid.TryParseExact(id, "N", out _) && !desired.ContainsKey(id)) AccessTools.Method(typeof(Inventory), "ClearItemFromInventory").Invoke(inventory, new object[] { item });
		}
		var current = ReadItems();
		foreach (var pair in desired) {
			if (current.ContainsKey(pair.Key)) continue;
			var prefab = CL_AssetManager.GetItemObjectPrefab(pair.Value);
			if (prefab?.itemData == null || !prefab.itemData.HasTag("currency")) continue;
			var item = prefab.itemData.GetClone();
			item.SetFirstDataStringsofType(Tag, pair.Key);
			inventory.AddItemToInventoryCenter(item);
		}
		Copy(ReadItems(), observed);
	}
}

[HarmonyPatch(typeof(Item_Object), "Start")]
public static class Patch_CurrencyWorldStart {
	private static void Postfix(Item_Object __instance) { SharedCurrencyItems.IdentifyWorldCurrency(__instance); }
}

[HarmonyPatch(typeof(Item_Object), nameof(Item_Object.Pickup))]
public static class Patch_CurrencyWorldPickup {
	private static void Prefix(Item_Object __instance) { SharedCurrencyItems.IdentifyWorldCurrency(__instance); }
}
