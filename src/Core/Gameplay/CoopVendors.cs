using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;

namespace WKMPMod.Gameplay;

public sealed class CoopVendors : MonoBehaviour {
	private static readonly Dictionary<ulong, CoopVendors> vendors = new Dictionary<ulong, CoopVendors>();
	private static readonly AccessTools.FieldRef<ENV_VendingMachine, bool> Dead = AccessTools.FieldRefAccess<ENV_VendingMachine, bool>("dead");
	private static readonly AccessTools.FieldRef<ENV_VendingMachine, bool> Blocked = AccessTools.FieldRefAccess<ENV_VendingMachine, bool>("isBlocked");
	private static readonly System.Reflection.MethodInfo Refresh = AccessTools.Method(typeof(ENV_VendingMachine), "UpdateButtons");
	private ENV_VendingMachine vendor;
	private List<ENV_VendingMachine.Purchase> catalog;
	private ulong id;
	private bool applying;
	private bool ready;
	private float nextRequest;
	private float purchasePending;
	public static bool Enabled => MPCore.IsInLobby && MPSteamworks.Instance.IsLan && !UI.ManualSaves.Loading;
	public static CoopVendors Get(ENV_VendingMachine vendor) {
		var component = vendor.GetComponent<CoopVendors>() ?? vendor.gameObject.AddComponent<CoopVendors>();
		if (component.vendor == null) { component.vendor = vendor; component.catalog = new List<ENV_VendingMachine.Purchase>(vendor.purchaseList); component.id = CoopIdentity.For(vendor.transform); vendors[component.id] = component; }
		return component;
	}
	private void Update() {
		if (!Enabled || vendor == null || Time.unscaledTime < nextRequest) return;
		ulong currentId = CoopIdentity.For(vendor.transform);
		if (currentId != id) { if (vendors.TryGetValue(id, out var previous) && previous == this) vendors.Remove(id); id = currentId; vendors[id] = this; ready = false; }
		nextRequest = Time.unscaledTime + (ready ? 5f : 1f);
		if (MPSteamworks.IsHost) Broadcast(0);
		else { var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPSteamworks.Instance.HostSteamId, PacketType.VendorState); writer.Put((byte)0).Put(id); MPSteamworks.Instance.SendToHost(writer); }
	}
	public void Broadcast(ulong target) {
		if (!Enabled || !MPSteamworks.IsHost || vendor.buttons == null) return;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, target, PacketType.VendorState);
		writer.Put((byte)1).Put(id).Put(Dead(vendor)).Put(vendor.buttons.Length);
		foreach (var button in vendor.buttons) writer.Put(button.purchase?.name ?? "").Put(button.available).Put(button.purchase?.price ?? 0);
		if (target == 0) MPSteamworks.Instance.Broadcast(writer); else MPSteamworks.Instance.SendToPeer(target, writer);
	}
	public static bool Buy(ENV_VendingMachine vendor, int slot, bool force, bool free) {
		if (!Enabled) return true;
		var net = Get(vendor);
		if (net.applying || MPSteamworks.IsHost) return true;
		if (!net.ready || Time.unscaledTime < net.purchasePending || slot < 0 || slot >= vendor.buttons.Length || !vendor.buttons[slot].available) return false;
		net.purchasePending = Time.unscaledTime + 3f;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPSteamworks.Instance.HostSteamId, PacketType.VendorState);
		writer.Put((byte)2).Put(net.id).Put(slot); MPSteamworks.Instance.SendToHost(writer);
		return false;
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (!Enabled || !MPSteamworks.Instance.HasConnection(sender)) return;
		byte action = reader.GetByte(); ulong id = reader.GetULong();
		if (!vendors.TryGetValue(id, out var net) || net == null || net.vendor == null) return;
		var vendor = net.vendor;
		if (action == 0 && MPSteamworks.IsHost) net.Broadcast(sender);
		else if (action == 1 && !MPSteamworks.IsHost && sender == MPSteamworks.Instance.HostSteamId) {
			bool dead = reader.GetBool(); int count = reader.GetInt();
			if (count < 0 || count > 64) return;
			for (int i = 0; i < count; i++) {
				string name = reader.GetString(); bool available = reader.GetBool(); int price = reader.GetInt();
				if (i >= vendor.buttons.Length) continue;
				var purchase = net.catalog.FirstOrDefault(item => item.name == name);
				if (purchase == null) { MPMain.LogWarning("[Vendor] Missing purchase " + name); return; }
				vendor.buttons[i].purchase = purchase; vendor.buttons[i].available = available; purchase.price = price;
			}
			Dead(vendor) = dead; net.ready = true; Refresh.Invoke(vendor, null);
		} else if (action == 2 && MPSteamworks.IsHost) {
			int slot = reader.GetInt();
			if (slot < 0 || slot >= vendor.buttons.Length || Dead(vendor) || Blocked(vendor) || CL_GameManager.HasActiveFlag("blockshops")) return;
			var button = vendor.buttons[slot];
			if (!button.available || button.purchase == null || button.purchase.price > CL_GameManager.GetRoaches()) { net.Broadcast(sender); return; }
			if (vendor.singlePurchase) button.available = false;
			CL_GameManager.AddRoaches(-button.purchase.price);
			var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, sender, PacketType.VendorState);
			writer.Put((byte)3).Put(id).Put(slot); MPSteamworks.Instance.SendToPeer(sender, writer);
			Refresh.Invoke(vendor, null); net.Broadcast(0);
		} else if (action == 3 && !MPSteamworks.IsHost && sender == MPSteamworks.Instance.HostSteamId) {
			int slot = reader.GetInt(); if (slot < 0 || slot >= vendor.buttons.Length) return;
			net.purchasePending = 0; net.applying = true;
			try { vendor.Buy(slot, true, true); } finally { net.applying = false; }
		}
	}
	private void OnDestroy() { if (vendors.TryGetValue(id, out var current) && current == this) vendors.Remove(id); }
}

[HarmonyPatch(typeof(ENV_VendingMachine), nameof(ENV_VendingMachine.Start))]
public static class Patch_VendorCatalog {
	private static void Prefix(ENV_VendingMachine __instance) => CoopVendors.Get(__instance);
	private static void Postfix(ENV_VendingMachine __instance) { if (CoopVendors.Enabled && MPSteamworks.IsHost) CoopVendors.Get(__instance).Broadcast(0); }
}
[HarmonyPatch(typeof(ENV_VendingMachine), nameof(ENV_VendingMachine.Buy), new[] { typeof(int), typeof(bool), typeof(bool) })]
public static class Patch_VendorPurchase {
	private static bool Prefix(ENV_VendingMachine __instance, int i, bool force, bool free) => CoopVendors.Buy(__instance, i, force, free);
	private static void Postfix(ENV_VendingMachine __instance) { if (CoopVendors.Enabled && MPSteamworks.IsHost) CoopVendors.Get(__instance).Broadcast(0); }
}
