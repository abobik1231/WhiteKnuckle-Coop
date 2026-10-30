using System;
using System.Collections.Generic;
using System.Linq;
using WKMPMod.Component;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.Gameplay;
using WKMPMod.NetWork;
using UnityEngine;

namespace WKMPMod.UI;

public sealed partial class ManualSaves {
	public static ulong LoadGeneration { get; private set; }
	public static bool GroupLoading => Instance != null && Instance.loadToken != null && !Instance.loadReleased;
	private string loadToken;
	private bool localLoadComplete;
	private bool loadReleased = true;
	private float nextLoadRetry;
	private float loadDeadline;
	private readonly HashSet<ulong> readyPlayers = new HashSet<ulong>();
	private ManualPlayerSave pendingRemotePlayer;
	public static void ResetCheckpointSession() {
		LoadGeneration = 0; Loading = false; LoadTargetLevel = null; LoadTargetRegion = null; if (Instance == null) return; Instance.loadToken = null; Instance.loadReleased = true; Instance.localLoadComplete = false; Instance.pendingRemotePlayer = null; Instance.readyPlayers.Clear(); Instance.loadedRun = null;
	}
	private void BeginHostHandshake() {
		loadToken = Guid.NewGuid().ToString("N"); LoadGeneration = BitConverter.ToUInt64(Guid.ParseExact(loadToken, "N").ToByteArray(), 0);
		localLoadComplete = false; loadReleased = false; readyPlayers.Clear(); nextLoadRetry = Time.unscaledTime + 3; loadDeadline = Time.unscaledTime + 180;
	}
	private bool AcceptLoadToken(ManualPlayerSave player) {
		if (player == null) return false;
		if (player.LoadToken == loadToken && loadToken != null) { if (localLoadComplete) Send(MPSteamworks.Instance.HostSteamId, PacketType.SaveReady, loadToken); return false; }
		if (Loading || GroupLoading) { pendingRemotePlayer = player; return false; }
		loadToken = player.LoadToken; LoadGeneration = Guid.TryParseExact(loadToken, "N", out var token) ? BitConverter.ToUInt64(token.ToByteArray(), 0) : 0;
		localLoadComplete = false; loadReleased = loadToken == null; return true;
	}
	private void CompletedLoad(string token) {
		if (token == null || token != loadToken) return;
		localLoadComplete = true;
		if (!MPCore.IsInLobby) { loadReleased = true; return; }
		if (!MPSteamworks.IsHost) Send(MPSteamworks.Instance.HostSteamId, PacketType.SaveReady, token);
		else ReleaseIfReady();
	}
	public void ReceiveReady(ulong sender, string token) {
		if (!MPSteamworks.Instance.IsLan || !MPSteamworks.Instance.HasConnection(sender) || token != loadToken) return;
		if (MPSteamworks.IsHost) { readyPlayers.Add(sender); ReleaseIfReady(); }
		else if (sender == MPSteamworks.Instance.HostSteamId) ReleaseLoad();
	}
	private void AbortLoad() {
		if (loadToken == null || loadReleased) return;
		string token = loadToken;
		if (MPSteamworks.Instance.IsLan) {
			if (MPSteamworks.IsHost) { foreach (ulong peer in MPSteamworks.Instance.ConnectedPeerIds) Send(peer, PacketType.SaveAbort, token); }
			else if (MPSteamworks.Instance.HasConnection(MPSteamworks.Instance.HostSteamId)) Send(MPSteamworks.Instance.HostSteamId, PacketType.SaveAbort, token);
		}
		loadReleased = true; loadToken = null; pendingRemotePlayer = null; loadedRun = null; readyPlayers.Clear(); Loading = false; LoadTargetLevel = null; LoadTargetRegion = null;
		status = "Загрузка прервана. Можно повторить загрузку сохранения.";
		MPMain.LogError("[Saves] Group checkpoint aborted " + token);
	}
	public void ReceiveAbort(ulong sender, string token) {
		if (token == loadToken && MPSteamworks.Instance.HasConnection(sender) && (MPSteamworks.IsHost || sender == MPSteamworks.Instance.HostSteamId)) AbortLoad();
	}
	private void ReleaseIfReady() {
		if (!localLoadComplete || MPSteamworks.Instance.ConnectedPeerIds.Any(peer => !readyPlayers.Contains(peer))) return;
		foreach (ulong peer in MPSteamworks.Instance.ConnectedPeerIds) Send(peer, PacketType.SaveReady, loadToken);
		ReleaseLoad();
	}
	private void ReleaseLoad() {
		loadReleased = true; CoopCoordinates.Reset(); CoopPresence.Views.Clear();
		LocalPlayer.Instance?.TriggerTeleport(); LocalPlayer.Instance?.RequestFullUpdate();
		if (MPSteamworks.IsHost) { CoopPassageDoor.BroadcastAll(); foreach (var door in FindObjectsOfType<UT_Door>()) WorldDoors.Send(door, !WorldDoors.State(door)); }
		MPMain.LogInfo("[Saves] Group checkpoint ready " + loadToken);
	}
	private void UpdateLoadHandshake() {
		if (!Loading && pendingRemotePlayer != null) { var pending = pendingRemotePlayer; pendingRemotePlayer = null; loadReleased = true; loadToken = null; if (AcceptLoadToken(pending)) StartCoroutine(WaitForRemoteLoad(pending, MPSteamworks.Instance.HostSteamId)); }
		if (loadToken == null || loadReleased || !MPSteamworks.Instance.IsLan || !MPSteamworks.IsHost || loadedRun == null || Time.unscaledTime < nextLoadRetry) return;
		if (Time.unscaledTime >= loadDeadline) { AbortLoad(); return; }
		nextLoadRetry = Time.unscaledTime + 3;
		foreach (ulong peer in MPSteamworks.Instance.ConnectedPeerIds.Where(peer => !readyPlayers.Contains(peer)).ToArray()) SendLoad(peer, loadedRun);
		ReleaseIfReady();
	}
}
