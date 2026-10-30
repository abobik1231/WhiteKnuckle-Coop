using Newtonsoft.Json;
using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using WKMPMod.NetWork;

namespace WKMPMod.Core;

public partial class MPCore {
	public void HostLan(int port = LanTransport.DefaultPort, int capacity = 8, bool useChain = false, float chainLength = 6f) {
		if (IsInLobby) throw new InvalidOperationException("Сначала выйди из текущей комнаты.");
		string scene = SceneManager.GetActiveScene().name;
		if (scene != "Game-Main" && scene != "Playground") throw new InvalidOperationException("Сначала запусти одиночную игру, затем нажми F8 и создай комнату.");
		SetStatus(MPStatus.LOBBY_MASK, MPStatus.JoiningLobby);
		try {
			_MPSteamworks.CreateLanRoom(port, capacity, Environment.MachineName, useChain, chainLength);
			_MPSteamworks.SetLobbyData(Data.MPKeys.GAMEMODE_JSON, JsonConvert.SerializeObject(MPGameModeManager.CaptureCurrentModeData()));
			SetStatus(MPStatus.INIT_MASK, MPStatus.Initialized);
			CommandConsole.Log($"LAN host started on TCP port {port}. Radmin IP: {string.Join(", ", MPSteamworks.RadminAddresses())}");
		} catch { ResetStateVariables(); throw; }
	}

	public async Task ConnectLan(string address, int port = LanTransport.DefaultPort) {
		Gameplay.PersonalEquipment.Capture();
		if (IsInLobby) throw new InvalidOperationException("Сначала выйди из текущей комнаты.");
		SetStatus(MPStatus.LOBBY_MASK, MPStatus.JoiningLobby);
		SetStatus(MPStatus.INIT_MASK, MPStatus.NotInitialized);
		try { await _MPSteamworks.JoinLanRoom(address, port); }
		catch { ResetStateVariables(); throw; }
	}

	private void HostLanCommand(string[] args) {
		try {
			int port = args.Length > 0 ? int.Parse(args[0]) : LanTransport.DefaultPort;
			int capacity = args.Length > 1 ? int.Parse(args[1]) : 8;
			ValidateLan(port, capacity);
			HostLan(port, capacity);
		} catch (Exception ex) { CommandConsole.LogError(ex.Message); }
	}

	private async Task JoinLanCommand(string[] args) {
		try {
			if (args.Length == 0) throw new ArgumentException("join lan <IP> [port]");
			int port = args.Length > 1 ? int.Parse(args[1]) : LanTransport.DefaultPort;
			ValidateLan(port, 8);
			await ConnectLan(args[0], port);
		} catch (Exception ex) { CommandConsole.LogError(ex.Message); }
	}

	public static void ValidateLan(int port, int capacity) {
		if (port < 1024 || port > 65535) throw new ArgumentException("Порт должен быть от 1024 до 65535.");
		if (capacity < 2 || capacity > 16) throw new ArgumentException("В комнате может быть от 2 до 16 игроков.");
	}
}
