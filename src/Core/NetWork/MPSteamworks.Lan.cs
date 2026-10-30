using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using WKMPMod.Component;
using WKMPMod.Core;
using WKMPMod.Data;

namespace WKMPMod.NetWork;

public partial class MPSteamworks {
	private static ulong activeLanId;
	private LanTransport lan;
	private int lanCapacity;
	private CancellationTokenSource lanConnect;
	private readonly Dictionary<ulong, LanMember> lanMembers = new Dictionary<ulong, LanMember>();
	public bool IsLan => lan != null;
	public float LanOneWayLatency(ulong id) => lan?.OneWayLatency(id) ?? 0;
	public IEnumerable<ulong> ConnectedPeerIds => IsLan
		? lanMembers.Keys.Where(id => id != UserSteamId).ToArray()
		: _allConnections.Keys.Select(id => id.Value).ToArray();
	public bool HasConnection(ulong id) => IsLan ? id != UserSteamId && lanMembers.ContainsKey(id) : _allConnections.ContainsKey(id);
	public string GetLobbyValue(string key) => IsLan ? LobbyData.GetValueOrDefault(key, "") : _currentLobby.GetData(key);
	public string GetMemberName(ulong id) => IsLan ? GetMemberData(id, MPKeys.PLAYER_NAME) ?? id.ToString() : new Friend(id).Name;
	public string LocalDisplayName => !string.IsNullOrWhiteSpace(MPConfig.RemotePlayerName) ? MPConfig.RemotePlayerName
		: IsLan ? Environment.MachineName : SteamClient.IsValid ? SteamClient.Name : Environment.MachineName;

	public void CreateLanRoom(int port, int capacity, string name, bool useChain = false, float chainLength = 6f) {
		DisconnectAll();
		activeLanId = LanTransport.NewId();
		HostSteamId = activeLanId;
		lanCapacity = capacity;
		LobbyData = GetDefaultLobbyData();
		LobbyData[MPKeys.LOBBY_NAME] = name;
		LobbyData[MPKeys.LOBBY_VISIBILITY] = "lan";
		LobbyData[Gameplay.PlayerChain.EnabledKey] = useChain.ToString();
		LobbyData[Gameplay.WorldLayoutSync.EnabledKey] = (UI.LanMenu.Instance?.WorldSync == true).ToString();
		LobbyData[Gameplay.PlayerChain.LengthKey] = Mathf.Clamp(chainLength, 3f, 12f).ToString(System.Globalization.CultureInfo.InvariantCulture);
		var state = new LanRoom {
			HostId = activeLanId, Capacity = capacity, GameVersion = Application.version,
			ModVersion = MPMain.PLUGIN_VERSION, Data = new Dictionary<string, string>(LobbyData),
			Members = new List<LanMember> { new LanMember { Id = activeLanId } }
		};
		try {
			lan = LanTransport.Host(port, activeLanId, state);
			AttachLan();
			EnterLan(state);
		} catch { DisconnectAll(); throw; }
	}

	public async Task JoinLanRoom(string address, int port) {
		DisconnectAll();
		var attempt = new CancellationTokenSource();
		lanConnect = attempt;
		ulong id = LanTransport.NewId();
		LanTransport connected = null;
		try {
			connected = await LanTransport.Connect(address, port, id, Application.version, MPMain.PLUGIN_VERSION, attempt.Token);
			if (attempt.IsCancellationRequested || lanConnect != attempt) { connected.Dispose(); return; }
			activeLanId = id;
			lan = connected;
			AttachLan();
			EnterLan(connected.Snapshot());
		} catch { connected?.Dispose(); throw; }
		finally { if (lanConnect == attempt) lanConnect = null; attempt.Dispose(); }
	}

	private void EnterLan(LanRoom state) {
		Component.RemotePlayer.LowLatency = true;
		HostSteamId = state.HostId;
		lanCapacity = state.Capacity;
		LobbyData = new Dictionary<string, string>(state.Data);
		lanMembers.Clear();
		foreach (var member in state.Members) lanMembers[member.Id] = member;
		if (!lanMembers.ContainsKey(UserSteamId)) lanMembers[UserSteamId] = new LanMember { Id = UserSteamId };
		HasConnections = ConnectedPeerIds.Any();
		LocalPlayer.Instance.Initialize(UserSteamId, MPConfig.RemotePlayerModel, MPConfig.RemotePlayerColor);
		MPCore.SetStatus(MPStatus.LOBBY_MASK, MPStatus.InLobby);
		MPEventBusNet.NotifyLobbyEntered(default);
		MPEventBusNet.NotifyLobbyDataChanged(LobbyData);
		foreach (var member in lanMembers.Values.ToArray()) {
			if (member.Id == UserSteamId) continue;
			MPEventBusNet.NotifyMemberDataChanged(new Friend(member.Id), member.Data);
			MPEventBusNet.NotifyPlayerConnected(member.Id);
		}
		MPMain.LogInfo($"[LAN] Joined room {state.HostId}, player {UserSteamId}, members {lanMembers.Count}.");
		if (!IsHost) StartCoroutine(AnnounceLanReady(lan));
	}

	private void AttachLan() {
		lan.Packet = (id, bytes) => MPEventBusNet.NotifyReceive(id, new ArraySegment<byte>(bytes));
		lan.Joined = member => {
			lanMembers[member.Id] = member;
			HasConnections = ConnectedPeerIds.Any();
			MPEventBusNet.NotifyLobbyMemberJoined(new Friend(member.Id));
			if (!IsHost) MPEventBusNet.NotifyPlayerConnected(member.Id);
		};
		lan.MemberChanged = (id, data) => {
			if (!lanMembers.TryGetValue(id, out var member)) return;
			member.Data = data;
			MPEventBusNet.NotifyMemberDataChanged(new Friend(id), data);
		};
		lan.RoomChanged = data => {
			LobbyData = new Dictionary<string, string>(data);
			MPEventBusNet.NotifyLobbyDataChanged(data);
		};
		lan.Left = id => {
			if (!IsHost && id == HostSteamId) {
				MPCore.Instance.ResetStateVariables();
				CommandConsole.LogError("LAN host disconnected. Reconnect using the host IP.");
				return;
			}
			lanMembers.Remove(id);
			HasConnections = ConnectedPeerIds.Any();
			MPEventBusNet.NotifyPlayerDisconnected(id);
		};
		lan.Error = error => MPMain.LogError("[LAN] " + error);
		lan.Ready = id => StartCoroutine(FinishLanJoin(lan, id));
	}

	private IEnumerator AnnounceLanReady(LanTransport session) {
		yield return null;
		while (lan == session && !LanWorldLoaded()) yield return null;
		if (lan == session) session.MarkReady();
	}
	private static bool LanWorldLoaded() {
		string scene = SceneManager.GetActiveScene().name;
		return MPCore.IsInitialized && (scene == "Playground" || scene == "Game-Main" && WorldLoader.isLoaded);
	}
	private IEnumerator FinishLanJoin(LanTransport session, ulong id) {
		while (lan == session && HasConnection(id) && !LanWorldLoaded()) yield return null;
		if (lan == session && HasConnection(id)) MPEventBusNet.NotifyPlayerConnected(id);
	}

	private void StopLan() {
		Component.RemotePlayer.LowLatency = false;
		lanConnect?.Cancel();
		lanConnect = null;
		var previous = lan;
		lan = null;
		activeLanId = 0;
		lanMembers.Clear();
		previous?.Dispose();
	}

	private void LanSend(ulong target, byte[] data, int offset, int length, Steamworks.Data.SendType type) {
		lan.Send(target, data, offset, length, ((int)type & (int)Steamworks.Data.SendType.Reliable) != 0);
	}

	private void SetLanLobbyData(Dictionary<string, string> values) {
		if (!IsHost || values == null) return;
		foreach (var pair in values) LobbyData[pair.Key] = pair.Value;
		lan.SetRoom(LobbyData);
		MPEventBusNet.NotifyLobbyDataChanged(values);
	}

	private void SetLanMemberData(Dictionary<string, string> values) {
		if (values == null || !lanMembers.TryGetValue(UserSteamId, out var member)) return;
		values[UI.ManualSaves.ProfileKey] = UI.ManualSaves.ProfileId;
		foreach (var pair in values) member.Data[pair.Key] = pair.Value;
		lan.SetMember(member.Data);
	}

	public static string[] RadminAddresses() {
		try {
			return NetworkInterface.GetAllNetworkInterfaces()
				.Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && (adapter.Name + adapter.Description).IndexOf("Radmin", StringComparison.OrdinalIgnoreCase) >= 0)
				.SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
				.Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
				.Select(address => address.Address.ToString()).Distinct().ToArray();
		} catch { return Array.Empty<string>(); }
	}
}
