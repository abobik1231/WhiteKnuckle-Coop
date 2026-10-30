using BepInEx.Configuration;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using WKMPMod.Core;
using WKMPMod.NetWork;

namespace WKMPMod.UI;

public sealed class LanMenu : MonoBehaviour {
	public static LanMenu Instance { get; private set; }
	public const string DefaultAddress = "";
	public static bool IsOpen { get; private set; }
	private static float blockInputUntil;
	public static bool BlocksGameplayInput => IsOpen || MultiplayerChat.BlocksGameplayInput || SaveBindings.Capturing || ManualSaves.IsOpen || ManualSaves.Loading || ManualSaves.GroupLoading || Gameplay.WorldLayoutSync.WaitingForMap || Gameplay.CoopSpectator.Active || Time.unscaledTime < blockInputUntil;
	public static int Port { get; private set; } = LanTransport.DefaultPort;
	public static string Status = "";
	private bool visible;
	private bool busy;
	private bool cursorWasVisible;
	private CursorLockMode cursorLock;
	private string address = DefaultAddress;
	private string portText = LanTransport.DefaultPort.ToString();
	private string playerName = "";
	private string[] detectedAddresses = Array.Empty<string>();
	private float nextAddressRefresh;
	private float lastHotkey = -1;
	private bool enemySync;
	private bool useChain;
	private float chainLength = 6f;
	private ConfigEntry<bool> savedChain;
	private ConfigEntry<float> savedChainLength;
	private ConfigEntry<bool> savedEnemySync;
	private ConfigEntry<bool> savedWorldSync;
	private ConfigEntry<string> savedAddress;
	private ConfigEntry<int> savedPort;
	private Rect window = new Rect(0, 0, 550, 550);

	private void Awake() {
		Instance = this;
		savedAddress = MPMain.Instance.Config.Bind("LAN", "HostAddress", DefaultAddress);
		savedPort = MPMain.Instance.Config.Bind("LAN", "Port", LanTransport.DefaultPort);
		savedChain = MPMain.Instance.Config.Bind("LAN", "UseChain", false);
		savedChainLength = MPMain.Instance.Config.Bind("LAN", "ChainLength", 6f);
		useChain = savedChain.Value;
		chainLength = Mathf.Clamp(savedChainLength.Value, 3f, 12f);
		address = string.IsNullOrWhiteSpace(savedAddress.Value) ? DefaultAddress : savedAddress.Value;
		savedAddress.Value = address;
		Port = savedPort.Value;
		portText = Port.ToString();
		playerName = string.IsNullOrWhiteSpace(MPConfig.RemotePlayerName) ? Environment.MachineName : MPConfig.RemotePlayerName;
		savedEnemySync = MPMain.Instance.Config.Bind("LAN", "EnemySyncV2", true);
		enemySync = savedEnemySync.Value;
		savedWorldSync = MPMain.Instance.Config.Bind("LAN", "HostWorldExperimental", false);
		if (savedWorldSync.Value) enemySync = true;
		MPConfig.EnemySync = enemySync;
		detectedAddresses = MPSteamworks.RadminAddresses();
	}
	private void Update() {
		if (Keyboard.current?.f8Key.wasPressedThisFrame == true) Hotkey();
		if ((visible || UI_Manager.Instance != null && UI_Manager.Instance.MultiplayerScreenVisible) && Time.unscaledTime >= nextAddressRefresh) { detectedAddresses = MPSteamworks.RadminAddresses(); nextAddressRefresh = Time.unscaledTime + 3; }
		if (visible) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
	}
	private void Hotkey() { if (MultiplayerChat.IsOpen || ManualSaves.IsOpen || ManualSaves.Loading || Time.unscaledTime - lastHotkey < 0.2f) return; lastHotkey = Time.unscaledTime; Toggle(); }
	private void Toggle() {
		visible = !visible;
		IsOpen = visible;
		blockInputUntil = Time.unscaledTime + 0.15f;
		if (visible) { cursorLock = Cursor.lockState; cursorWasVisible = Cursor.visible; }
		else { Cursor.lockState = cursorLock; Cursor.visible = cursorWasVisible; }
	}
	private void OnDisable() {
		if (visible) { visible = false; Cursor.lockState = cursorLock; Cursor.visible = cursorWasVisible; }
		IsOpen = false;
		blockInputUntil = 0;
	}
	private void OnGUI() {
		string scene = SceneManager.GetActiveScene().name;
		if (scene != "Main-Menu" && scene != "Game-Main" && scene != "Playground") return;
		if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F8) { Hotkey(); Event.current.Use(); }
		float scale = Mathf.Clamp(Screen.height / 900f, 0.7f, 2f);
		var previous = GUI.matrix;
		GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
		float width = Screen.width / scale;
		float height = Screen.height / scale;
		if (visible) {
			window.x = (width - window.width) / 2;
			window.y = (height - window.height) / 2;
			window = GUI.Window(47653, window, Draw, "Radmin LAN");
		}
		GUI.matrix = previous;
	}
	private void Draw(int id) {
		GUILayout.Space(12);
		GUILayout.Label("Оба игрока должны быть в одной сети Radmin VPN.");
		GUILayout.Label("Имя игрока");
		playerName = GUILayout.TextField(playerName, 40);
		GUILayout.Label("IP хоста из Radmin VPN");
		address = GUILayout.TextField(address, 128);
		GUILayout.Label("Порт (одинаковый у обоих игроков)");
		portText = GUILayout.TextField(portText, 5);
		if (MPSteamworks.Instance.IsLan) {
			GUILayout.Label(Gameplay.PlayerChain.RoomEnabled ? "Комната с цепью, длина " + Gameplay.PlayerChain.RoomLength.ToString("0") + " м" : "Комната без цепи");
		} else GUILayout.Label("Цепь и синхронизацию врагов выбирает хост в меню паузы.");
		GUILayout.Space(8);
		GUI.enabled = !busy;
		if (GUILayout.Button("Подключиться", GUILayout.Height(34))) Connect();
		if (GUILayout.Button("Создать комнату в текущей игре", GUILayout.Height(34))) CreateRoom();
		GUI.enabled = true;
		if (MPCore.IsInLobby && GUILayout.Button("Выйти из комнаты или отменить подключение", GUILayout.Height(28))) {
			MPCore.Instance.Leave(Array.Empty<string>());
			Status = "Подключение закрыто.";
		}
		GUILayout.Label("Для создания комнаты: запусти одиночную игру, затем нажми F8.");
		var addresses = detectedAddresses;
		GUILayout.Label("Твой Radmin IP: " + (addresses.Length > 0 ? string.Join(", ", addresses) : "адаптер не найден"));
		if (MPSteamworks.Instance.IsLan) GUILayout.Label("В комнате: " + MPSteamworks.Instance.Members.Count() + "   Порт: " + Port);
		GUILayout.Label(busy ? "Подключение, подожди до 10 секунд..." : Status);
		if (GUILayout.Button("Закрыть", GUILayout.Height(28))) Toggle();
	}
	private void Save() {
		if (!int.TryParse(portText, out var value)) throw new ArgumentException("Укажи числовой порт.");
		MPCore.ValidateLan(value, 8);
		Port = value;
		savedPort.Value = Port;
		savedAddress.Value = address.Trim();
		MPConfig.RemotePlayerName = playerName.Trim();
		savedChain.Value = useChain;
		savedChainLength.Value = chainLength;
	}
	public string Address { get => address; set => address = value; }
	public string PortText { get => portText; set => portText = value; }
	public string PlayerName { get => playerName; set => playerName = value; }
	public bool Busy => busy;
	public bool WorldSync {
		get => savedWorldSync.Value;
		set { if (!MPCore.IsInLobby || MPSteamworks.IsHost) { savedWorldSync.Value = value; if (value) EnemySync = true; } }
	}
	public string Addresses => detectedAddresses.Length > 0 ? string.Join(", ", detectedAddresses) : "адаптер не найден";
	public bool UseChain { get => useChain; set { if (!MPCore.IsInLobby || MPSteamworks.IsHost) useChain = value; } }
	public float ChainLength { get => chainLength; set { if (!MPCore.IsInLobby || MPSteamworks.IsHost) chainLength = Mathf.Clamp(Mathf.Round(value), 3, 12); } }
	public bool EnemySync {
		get => enemySync;
		set {
			if (MPCore.IsInLobby && !MPSteamworks.IsHost) return;
			if (savedWorldSync?.Value == true) value = true;
			if (value == enemySync) return;
			enemySync = value;
			MPConfig.EnemySync = value;
			savedEnemySync.Value = value;
			if (MPSteamworks.Instance.IsLan && MPSteamworks.IsHost) MPSteamworks.Instance.SetLobbyData(Data.MPKeys.ENEMY_SYNC, value.ToString());
		}
	}
	public void CreateRoom() {
		try { Save(); MPCore.Instance.HostLan(Port, 8, useChain, chainLength); Status = "Комната создана. Передай другу свой Radmin IP и порт."; }
		catch (Exception ex) { Status = ex.Message; }
	}
	public async void Connect() {
		try {
			Save();
			if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Введи IP друга из Radmin VPN.");
			busy = true;
			await MPCore.Instance.ConnectLan(address.Trim(), Port);
			Status = MPSteamworks.Instance.IsLan ? "Подключено. Мир загружается автоматически." : "Подключение отменено.";
		} catch (Exception ex) {
			Status = "Не удалось подключиться: " + ex.Message + " Проверь IP, порт и разрешение игры в брандмауэре Windows.";
			MPMain.LogError("[LAN UI] " + ex);
		} finally { busy = false; }
	}
}
