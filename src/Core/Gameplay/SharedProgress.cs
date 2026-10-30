using System;
using HarmonyLib;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.UI;

namespace WKMPMod.Gameplay;

public sealed class SharedProgress : MonoBehaviour {
	internal static bool Applying;
	private float nextBroadcast;
	internal static bool Enabled => MPCore.CanSync && MPSteamworks.Instance.IsLan && !ManualSaves.Loading && !ManualSaves.GroupLoading;
	private void Update() {
		if (!Enabled || Time.unscaledTime < nextBroadcast) return;
		nextBroadcast = Time.unscaledTime + 3f;
		var player = ENT_Player.GetPlayer();
		if (player != null && player.perks != null) foreach (var perk in player.perks) if (perk != null) SendPerk(perk);
		if (MPSteamworks.IsHost) BroadcastRoaches();
	}
	internal static void SendPerk(Perk perk) {
		if (!Enabled || Applying || perk == null || string.IsNullOrEmpty(perk.id)) return;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.SharedPerk);
		writer.Put(ManualSaves.LoadGeneration).Put(perk.id).Put(perk.GetStackAmount());
		MPSteamworks.Instance.Broadcast(writer);
	}
	public static void ReceivePerk(ulong sender, DataReader reader) {
		if (!Enabled || !MPSteamworks.Instance.HasConnection(sender) || reader.GetULong() != ManualSaves.LoadGeneration) return;
		string id = reader.GetString();
		int stacks = reader.GetInt();
		if (string.IsNullOrEmpty(id) || id.Length > 128 || stacks < 1 || stacks > 100) return;
		var player = ENT_Player.GetPlayer();
		if (player == null || player.IsDead() || player.perks == null) return;
		int current = 0;
		foreach (var perk in player.perks) if (perk != null && perk.id == id) { current = perk.GetStackAmount(); break; }
		if (current >= stacks) return;
		var asset = CL_AssetManager.GetPerkAsset(id);
		if (asset == null) return;
		Applying = true;
		try { player.AddPerk(asset, stacks - current, true); } finally { Applying = false; }
	}
	internal static void CurrencyChanged(int before, bool global) {
		if (!Enabled || Applying || global) return;
		int delta = CL_GameManager.GetRoaches() - before;
		if (delta == 0) return;
		if (MPSteamworks.IsHost) BroadcastRoaches();
		else {
			var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPSteamworks.Instance.HostSteamId, PacketType.SharedRoaches);
			writer.Put((byte)0).Put(ManualSaves.LoadGeneration).Put(delta);
			MPSteamworks.Instance.SendToPeer(MPSteamworks.Instance.HostSteamId, writer);
		}
	}
	private static void BroadcastRoaches() {
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.SharedRoaches);
		writer.Put((byte)1).Put(ManualSaves.LoadGeneration).Put(CL_GameManager.GetRoaches());
		MPSteamworks.Instance.Broadcast(writer);
	}
	public static void ReceiveRoaches(ulong sender, DataReader reader) {
		if (!Enabled || !MPSteamworks.Instance.HasConnection(sender)) return;
		byte action = reader.GetByte();
		if (reader.GetULong() != ManualSaves.LoadGeneration) return;
		int value = reader.GetInt();
		if (action == 0 && MPSteamworks.IsHost && value >= -999 && value <= 999) {
			Applying = true;
			try { CL_GameManager.AddRoaches(value); } finally { Applying = false; }
			BroadcastRoaches();
		} else if (action == 1 && !MPSteamworks.IsHost && sender == MPSteamworks.Instance.HostSteamId && value >= 0 && value <= 999) {
			Applying = true;
			try { CL_GameManager.SetRoaches(value); } finally { Applying = false; }
		}
	}
}

[HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.AddPerk))]
public static class Patch_SharedPerk {
	private static void Postfix(ENT_Player __instance, Perk __result) {
		if (__instance == ENT_Player.GetPlayer()) SharedProgress.SendPerk(__result);
	}
}

[HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.AddRoaches), new Type[] { typeof(int), typeof(bool) })]
public static class Patch_SharedRoaches {
	private static void Prefix(bool global, out int __state) { __state = global ? 0 : CL_GameManager.GetRoaches(); }
	private static void Postfix(bool global, int __state) { SharedProgress.CurrencyChanged(__state, global); }
}
