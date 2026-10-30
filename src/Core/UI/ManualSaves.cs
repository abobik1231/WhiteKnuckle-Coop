using HarmonyLib;
using Newtonsoft.Json;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.Gameplay;
using WKMPMod.NetWork;
using WKMPMod.World;

namespace WKMPMod.UI;

public sealed partial class ManualSaves : MonoBehaviour {
	public const string ProfileKey = "save profile";
	public static ManualSaves Instance { get; private set; }
	public static bool IsOpen { get; private set; }
	public static bool Loading { get; private set; }
	public static CL_SaveManager.SessionSave PendingNativeSave;
	public static string LoadTargetLevel;
	public static string LoadTargetRegion;
	public static string SaveDirectory => Path.Combine(Application.persistentDataPath, "WKMultiPlayer", "Saves");
	private static string profileId;
	public static string ProfileId {
		get {
			if (profileId != null) return profileId;
			string folder = Path.Combine(Application.persistentDataPath, "WKMultiPlayer");
			Directory.CreateDirectory(folder);
			string path = Path.Combine(folder, "profile.id");
			if (File.Exists(path) && Guid.TryParse(File.ReadAllText(path), out var id)) return profileId = id.ToString("N");
			profileId = Guid.NewGuid().ToString("N");
			File.WriteAllText(path, profileId);
			return profileId;
		}
	}
	private readonly List<(string Path, ManualRunSave Save)> saves = new List<(string, ManualRunSave)>();
	private readonly Dictionary<string, (long Length, long Modified, ManualRunSave Save)> saveCache = new Dictionary<string, (long, long, ManualRunSave)>();
	private readonly Dictionary<ulong, ManualPlayerSave> received = new Dictionary<ulong, ManualPlayerSave>();
	private string request;
	private bool busy;
	private string status = "";
	private Vector2 scroll;
	private bool showAllSaves;
	private ManualRunSave loadedRun;
	private ulong loadedRoom;
	private readonly HashSet<ulong> restoredMembers = new HashSet<ulong>();
	private string editing;
	private string deleting;
	private string editTitle;
	private bool editChain;
	private float editLength;
	private bool editEnemies;
    private bool editWorld;
	private float statusUntil;
	private GameObject pauseRoot;
	private GameObject continueButton;
	private GameObject lobbyButton;
	private GameObject chainOption;
	private GameObject enemyOption;
	private GameObject worldOption;
	private GameObject createLobbyButton;
	private bool lobbyExpanded;
	private SaveMenuSkin saveSkin;
	private bool mainMenuBrowser;
	private GameObject modalBlocker;
	private string search = "";
	private string requestedMainSave;
	private bool previousNavigation;
	private static bool Authority => !MPCore.IsInLobby || MPSteamworks.Instance.IsLan && MPSteamworks.IsHost;
	private static bool InGame => SceneManager.GetActiveScene().name == "Game-Main" || SceneManager.GetActiveScene().name == "Playground";
	private void Awake() { Instance = this; MPEventBusNet.OnPlayerConnected += PlayerJoined; }
	private void OnDestroy() { MPEventBusNet.OnPlayerConnected -= PlayerJoined; saveSkin?.Dispose(); if (Instance == this) { Instance = null; IsOpen = false; Loading = false; } }
	private void Update() {
		UpdateLoadHandshake();
		if (requestedMainSave != null) { string path = requestedMainSave; requestedMainSave = null; Close(); StartCoroutine(LoadFromMainMenu(path)); return; }
		if (InGame && CL_GameManager.gMan != null && CL_GameManager.gMan.pauseMenu != null && pauseRoot != CL_GameManager.gMan.pauseMenu) CreatePauseButtons();
		if (continueButton != null) continueButton.SetActive(Authority && CoopSpectator.Active);
		if (lobbyButton != null) {
			bool canCreate = !MPCore.IsInLobby && !CoopSpectator.Active;
			if (!canCreate) lobbyExpanded = false;
			lobbyButton.SetActive(canCreate);
			chainOption.SetActive(canCreate && lobbyExpanded);
			enemyOption.SetActive(canCreate && lobbyExpanded);
			worldOption.SetActive(canCreate && lobbyExpanded);
			enemyOption.GetComponent<Toggle>().interactable = !LanMenu.Instance.WorldSync;
			createLobbyButton.SetActive(canCreate && lobbyExpanded);
		}
		if (loadedRun != null && (!MPCore.IsInLobby || MPSteamworks.Instance.HostSteamId != loadedRoom)) loadedRun = null;
		if (IsOpen && !mainMenuBrowser && (!InGame || CL_GameManager.gMan == null || !CL_GameManager.gMan.isPaused)) Close();
		if (IsOpen && !busy && Keyboard.current?.escapeKey.wasPressedThisFrame == true) { if (editing != null || deleting != null) { editing = null; deleting = null; } else Close(); }
		if (!InGame || CL_GameManager.gMan == null || IsOpen || LanMenu.IsOpen || MultiplayerChat.IsOpen || Loading || GroupLoading || busy || !Authority) return;
		if (SaveBindings.Capturing) return;
		if (SaveBindings.Pressed(true)) { statusUntil = Time.unscaledTime + 15; StartCoroutine(SaveRoutine()); }
		if (SaveBindings.Pressed(false)) LoadLatest();
	}
	public void LoadLatest() {
		if (!Authority || busy || Loading || GroupLoading || !InGame) return;
		Refresh();
		var latest = saves.FirstOrDefault(entry => entry.Save.GameVersion == Application.version && string.Equals(entry.Save.GameMode, CL_GameManager.GetGamemodeName(false, true), StringComparison.OrdinalIgnoreCase));
		statusUntil = Time.unscaledTime + 8;
		if (latest.Path == null) status = "Подходящих сохранений не найдено.";
		else { status = "Загружаю сохранение с высотой " + Mathf.RoundToInt(latest.Save.Height) + " м."; MPMain.LogInfo("[Saves] Selected " + latest.Path + ", created=" + latest.Save.CreatedUtc + ", height=" + latest.Save.Height); TryLoad(latest.Path); }
	}
	public void ContinueFromMainMenu() { if (Authority && !busy && !Loading) StartCoroutine(LoadFromMainMenu()); }
	private IEnumerator LoadFromMainMenu(string selectedPath = null) {
		Refresh();
		var latest = saves.FirstOrDefault(entry => entry.Save.GameVersion == Application.version && (selectedPath == null || entry.Path == selectedPath));
		if (latest.Path == null) { MPMain.LogWarning("[Saves] No compatible save found"); yield break; }
		var host = latest.Save.Players.FirstOrDefault(player => player.Host) ?? latest.Save.Players[0];
		CL_SaveManager.SessionSave session;
		try { session = ValidateSession(host.Session); MPGameModeManager.LoadGameMode(new MPGameModeManager.GameModeData { gameModeName = latest.Save.GameMode, activeSettings = host.Settings ?? new List<string>(), activeTrinkets = new List<string>(), seed = session.worldSeed }); }
		catch (Exception ex) { MPMain.LogError("[Saves] Continue: " + ex); yield break; }
		float deadline = Time.unscaledTime + 120;
		while ((!InGame || CL_GameManager.gMan == null || ENT_Player.GetPlayer() == null || WorldLoader.initialized && !WorldLoader.isLoaded) && Time.unscaledTime < deadline) yield return null;
		if (InGame && ENT_Player.GetPlayer() != null) TryLoad(latest.Path);
	}
	private void CreatePauseButtons() {
		pauseRoot = CL_GameManager.gMan.pauseMenu;
		var template = pauseRoot.GetComponentsInChildren<Button>(true).FirstOrDefault(button => button.name == "Resume");
		if (template == null) return;
		var parent = template.transform.parent;
		if (parent.GetComponent<PauseMenuLayout>() == null) parent.gameObject.AddComponent<PauseMenuLayout>();
		CreatePauseButton(template, parent, "SAVES", () => Open());
		CreatePauseButton(template, parent, "RESET EFFECTS", () => ResetPlayerEffects(true));
		continueButton = CreatePauseButton(template, parent, "CONTINUE LAST SAVE", () => LoadLatest());
		continueButton.SetActive(false);
		lobbyExpanded = false;
		lobbyButton = CreatePauseButton(template, parent, "CREATE LOBBY", ToggleLobbyOptions);
		chainOption = CreatePauseToggle(template, parent, "CHAIN", LanMenu.Instance.UseChain, value => LanMenu.Instance.UseChain = value);
		enemyOption = CreatePauseToggle(template, parent, "ENEMY SYNC", LanMenu.Instance.EnemySync, value => LanMenu.Instance.EnemySync = value);
		worldOption = CreatePauseToggle(template, parent, "HOST MAP (EXPERIMENTAL)", LanMenu.Instance.WorldSync, value => { LanMenu.Instance.WorldSync = value; enemyOption.GetComponent<Toggle>().SetIsOnWithoutNotify(LanMenu.Instance.EnemySync); });
		createLobbyButton = CreatePauseButton(template, parent, "CREATE", CreateLobbyFromPause);
		int lobbyIndex = template.transform.GetSiblingIndex() + 1;
		foreach (var control in new[] { lobbyButton, chainOption, enemyOption, worldOption, createLobbyButton }) control.transform.SetSiblingIndex(lobbyIndex++);
		chainOption.SetActive(false); enemyOption.SetActive(false); worldOption.SetActive(false); createLobbyButton.SetActive(false);
	}
	private void ToggleLobbyOptions() {
		lobbyExpanded = !lobbyExpanded;
		if (lobbyExpanded) {
			chainOption.GetComponent<Toggle>().SetIsOnWithoutNotify(LanMenu.Instance.UseChain);
			enemyOption.GetComponent<Toggle>().SetIsOnWithoutNotify(LanMenu.Instance.EnemySync);
			worldOption.GetComponent<Toggle>().SetIsOnWithoutNotify(LanMenu.Instance.WorldSync);
		}
	}
	private void CreateLobbyFromPause() {
		var game = CL_GameManager.gMan;
		if (game == null || MPCore.IsInLobby || busy || Loading) return;
		ManualPlayerSave player;
		try { player = Capture(); }
		catch (Exception ex) { MPMain.LogError("[Saves] Create room capture: " + ex); UI_Manager.DisplayMessage("Не удалось создать комнату: " + ex.Message, UI_Manager.UIDisplayType.Subtitle); return; }
		string gameMode = CL_GameManager.GetGamemodeName(false, true);
		bool paused = game.isPaused;
		if (paused) game.UnPause();
		LanMenu.Instance.CreateRoom();
		if (!MPCore.IsInLobby && paused) { game.Pause(); UI_Manager.DisplayMessage(LanMenu.Status, UI_Manager.UIDisplayType.Subtitle); }
		if (!MPCore.IsInLobby) return;
		var previous = loadedRun;
		var players = previous != null && string.Equals(previous.GameMode, gameMode, StringComparison.OrdinalIgnoreCase) ? previous.Players.Where(item => !item.Host).ToList() : new List<ManualPlayerSave>();
		players.Insert(0, player);
		loadedRun = new ManualRunSave { GameVersion = Application.version, GameMode = gameMode, Height = player.Height, PlayerCount = players.Count, Chain = PlayerChain.RoomEnabled, ChainLength = PlayerChain.RoomLength, EnemySync = MPConfig.EnemySync, WorldSync = WorldLayoutSync.Enabled, Mechanisms = player.Mechanisms, WorldLayout = player.WorldLayout, Currency = player.Currency, Roaches = player.Roaches, Players = players };
		loadedRoom = MPSteamworks.Instance.HostSteamId;
		restoredMembers.Clear();
	}
	private static GameObject CreatePauseToggle(Button template, Transform parent, string label, bool selected, UnityEngine.Events.UnityAction<bool> changed) {
		var copy = Instantiate(template.gameObject, parent);
		copy.name = "WK " + label;
		foreach (var script in copy.GetComponents<MonoBehaviour>()) if (script is UI_FirstSelect || script is UI_CloseButtonOnBack || script is UI_PressButtonOnInput || script is UI_AnimateOnSelect) Destroy(script);
		var old = copy.GetComponent<Button>(); old.enabled = false; DestroyImmediate(old);
		var text = copy.GetComponentInChildren<TMP_Text>(true); if (text != null) text.text = label;
		var box = new GameObject("Checkbox", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		box.transform.SetParent(copy.transform, false);
		var boxRect = (RectTransform)box.transform; boxRect.anchorMin = new Vector2(0, 0.5f); boxRect.anchorMax = new Vector2(0, 0.5f); boxRect.pivot = new Vector2(0, 0.5f); boxRect.anchoredPosition = new Vector2(18, 0); boxRect.sizeDelta = new Vector2(23, 23);
		box.GetComponent<Image>().color = new Color(0.6f, 0.6f, 0.6f);
		var inner = new GameObject("Interior", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		inner.transform.SetParent(box.transform, false);
		var innerRect = (RectTransform)inner.transform; innerRect.anchorMin = Vector2.zero; innerRect.anchorMax = Vector2.one; innerRect.offsetMin = new Vector2(2, 2); innerRect.offsetMax = new Vector2(-2, -2); inner.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.04f);
		var mark = new GameObject("Check", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
		mark.transform.SetParent(inner.transform, false);
		var markRect = (RectTransform)mark.transform; markRect.anchorMin = Vector2.zero; markRect.anchorMax = Vector2.one; markRect.offsetMin = new Vector2(4, 4); markRect.offsetMax = new Vector2(-4, -4); mark.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.2f);
		var toggle = copy.AddComponent<Toggle>(); toggle.targetGraphic = copy.GetComponent<Image>(); toggle.graphic = mark.GetComponent<Image>(); toggle.isOn = selected; toggle.onValueChanged.AddListener(changed);
		int exitIndex = parent.Find("Exit")?.GetSiblingIndex() ?? parent.childCount - 1;
		copy.transform.SetSiblingIndex(exitIndex);
		return copy;
	}
	private static GameObject CreatePauseButton(Button template, Transform parent, string text, UnityEngine.Events.UnityAction action) {
		var copy = Instantiate(template.gameObject, parent);
		copy.name = "WK " + text;
		foreach (var script in copy.GetComponents<MonoBehaviour>()) if (script is UI_FirstSelect || script is UI_CloseButtonOnBack || script is UI_PressButtonOnInput) Destroy(script);
		var button = copy.GetComponent<Button>(); button.onClick = new Button.ButtonClickedEvent(); button.onClick.AddListener(action);
		var label = copy.GetComponentInChildren<TMP_Text>(true); if (label != null) label.text = text;
		int exitIndex = parent.Find("Exit")?.GetSiblingIndex() ?? parent.childCount - 1;
		copy.transform.SetSiblingIndex(exitIndex);
		copy.SetActive(true);
		return copy;
	}
	private void Open() {
		mainMenuBrowser = false;
		showAllSaves = false; scroll = Vector2.zero;
		IsOpen = true;
		BlockModal(true);
		CL_GameManager.gMan.pauseMenu.SetActive(false);
		Refresh();
	}
	public void OpenFromMainMenu() {
		if (SceneManager.GetActiveScene().name != "Main-Menu" || busy) return;
		mainMenuBrowser = true; showAllSaves = false; scroll = Vector2.zero; search = ""; IsOpen = true;
		BlockModal(true); Refresh();
	}
	private void BlockModal(bool active) {
		var events = UnityEngine.EventSystems.EventSystem.current;
		if (events != null) {
			if (active && (modalBlocker == null || !modalBlocker.activeSelf)) { previousNavigation = events.sendNavigationEvents; events.sendNavigationEvents = false; events.SetSelectedGameObject(null); }
			else if (!active && modalBlocker != null && modalBlocker.activeSelf) events.sendNavigationEvents = previousNavigation;
		}
		if (modalBlocker == null && active) {
			modalBlocker = new GameObject("Save Menu Input Blocker", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)); modalBlocker.transform.SetParent(transform, false);
			var canvas = modalBlocker.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = 30000;
			var panel = new GameObject("Blocker", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(modalBlocker.transform, false); var rect = (RectTransform)panel.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; panel.GetComponent<Image>().color = Color.clear;
		}
		if (modalBlocker != null) modalBlocker.SetActive(active);
	}
	private void Close() {
		editing = null;
		deleting = null;
		IsOpen = false;
		BlockModal(false);
		if (!mainMenuBrowser && CL_GameManager.gMan != null && CL_GameManager.gMan.isPaused) CL_GameManager.gMan.pauseMenu.SetActive(true);
		mainMenuBrowser = false;
	}
	private void Refresh() {
		saves.Clear();
		Directory.CreateDirectory(SaveDirectory);
		int broken = 0;
		foreach (string file in Directory.GetFiles(SaveDirectory, "*.wksave")) {
			try { var info = new FileInfo(file); if (!saveCache.TryGetValue(file, out var cached) || cached.Length != info.Length || cached.Modified != info.LastWriteTimeUtc.Ticks) { cached = (info.Length, info.LastWriteTimeUtc.Ticks, ManualSaveStore.Read(file)); saveCache[file] = cached; } saves.Add((file, cached.Save)); } catch { broken++; }
		}
		saves.Sort((a, b) => string.CompareOrdinal(b.Save.CreatedUtc, a.Save.CreatedUtc));
		status = broken == 0 ? "" : "Пропущено повреждённых файлов: " + broken;
	}
	private void OnGUI() {
		if (InGame && CL_GameManager.gMan != null && ENT_Player.GetPlayer() != null && !CL_GameManager.gMan.isPaused && !Loading) {
			var heightStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(6, 6, 0, 0) };
			GUI.Box(new Rect(Screen.width - 210, 16, 190, 32), "Высота: " + Mathf.RoundToInt(CL_GameManager.gMan.GetPlayerCorrectedHeight()) + " м", heightStyle);
			if (Time.unscaledTime < statusUntil && !string.IsNullOrEmpty(status)) GUI.Box(new Rect(20, Screen.height - 76, 540, 45), status);
		}
		if ((!mainMenuBrowser && (!InGame || CL_GameManager.gMan == null || !CL_GameManager.gMan.isPaused)) || Loading) return;
		if (!IsOpen) return;
		saveSkin ??= new SaveMenuSkin();
		var previousSkin = GUI.skin;
		var previousMatrix = GUI.matrix;
		float scale = Mathf.Clamp(Screen.height / 1080f, 0.65f, 2f);
		GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
		GUI.skin = saveSkin.Skin;
		float screenWidth = Screen.width / scale;
		float screenHeight = Screen.height / scale;
		float width = Mathf.Min(820, screenWidth - 40);
		float height = Mathf.Min(780, screenHeight - 40);
		var area = new Rect((screenWidth - width) / 2, (screenHeight - height) / 2, width, height);
		GUI.DrawTexture(new Rect(0, 0, screenWidth, screenHeight), saveSkin.Backdrop);
		GUI.Box(area, "");
		GUILayout.BeginArea(new Rect(area.x + 24, area.y + 18, width - 48, height - 36));
		GUILayout.Label(editing != null ? "РЕДАКТИРОВАНИЕ" : deleting != null ? "УДАЛЕНИЕ СОХРАНЕНИЯ" : "СОХРАНЕНИЯ", saveSkin.Title, GUILayout.Height(42));
		if (editing != null || deleting != null) { DrawEditor(); GUILayout.EndArea(); GUI.skin = previousSkin; GUI.matrix = previousMatrix; return; }
		GUILayout.Label(mainMenuBrowser ? "Выбери сохранение, нужный режим запустится автоматически" : Authority ? "Сохранение и загрузка доступны хосту" : "Сохранение и загрузка доступны только хосту", saveSkin.Subtitle, GUILayout.Height(30));
		GUI.enabled = Authority && !busy && !Loading;
		if (!mainMenuBrowser && GUILayout.Button("СОХРАНИТЬ ИГРУ", GUILayout.Height(44))) StartCoroutine(SaveRoutine());
		GUI.enabled = !busy;
		GUILayout.BeginHorizontal(); GUILayout.Label("Поиск", GUILayout.Width(64)); search = GUILayout.TextField(search, 80, GUILayout.Height(34)); GUILayout.EndHorizontal();
		scroll = GUILayout.BeginScrollView(scroll, false, true, GUILayout.ExpandHeight(true));
		if (saves.Count == 0) GUILayout.Label("Сохранений пока нет.", saveSkin.Subtitle, GUILayout.Height(50));
		var visibleSaves = saves.Where(entry => string.IsNullOrWhiteSpace(search) || ((entry.Save.Title ?? "") + " " + entry.Save.GameMode + " " + string.Join(" ", entry.Save.Players.Select(player => player.Name))).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).Take(showAllSaves ? saves.Count : 20);
		foreach (var entry in visibleSaves) {
			GUILayout.BeginVertical(GUI.skin.box);
			if (!string.IsNullOrWhiteSpace(entry.Save.Title)) using (new RussianGame.UserTextScope(true)) GUILayout.Label(entry.Save.Title, GUILayout.Height(27));
			DateTime.TryParse(entry.Save.CreatedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var date);
			GUILayout.Label(date.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") + "   Высота: " + Mathf.RoundToInt(entry.Save.Height) + " м   Игроков: " + entry.Save.PlayerCount);
			using (new RussianGame.UserTextScope(true)) GUILayout.Label(string.Join(", ", entry.Save.Players.Select(player => player.Name)), saveSkin.Subtitle);
			GUILayout.Label(Gameplay.RussianGame.Translate(entry.Save.GameMode ?? "") + (entry.Save.Chain ? "   Цепь: " + entry.Save.ChainLength.ToString("0") + " м" : "   Без цепи") + (entry.Save.EnemySync == true ? "   Синхрон врагов" : "") + (entry.Save.WorldSync == true ? "   Карта хоста" : ""), saveSkin.Subtitle);
			GUI.enabled = Authority && !busy && !Loading;
			if (GUILayout.Button("ЗАГРУЗИТЬ", GUILayout.Height(38))) { if (mainMenuBrowser) requestedMainSave = entry.Path; else TryLoad(entry.Path); }
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("РЕДАКТИРОВАТЬ", GUILayout.Height(36))) { editing = entry.Path; editTitle = entry.Save.Title ?? ""; editChain = entry.Save.Chain; editLength = Mathf.Clamp(entry.Save.ChainLength, 3, 12); editEnemies = entry.Save.EnemySync ?? MPConfig.EnemySync; editWorld = entry.Save.WorldSync ?? false; }
			if (GUILayout.Button("УДАЛИТЬ", GUILayout.Height(36))) deleting = entry.Path;
			GUILayout.EndHorizontal();
			GUI.enabled = !busy;
			GUILayout.EndVertical();
		}
		GUI.enabled = !busy;
		if (!showAllSaves && saves.Count > 20 && GUILayout.Button("ПОКАЗАТЬ ВСЕ", GUILayout.Height(42))) showAllSaves = true;
		GUILayout.EndScrollView();
		GUILayout.Label(status, saveSkin.Subtitle, GUILayout.MinHeight(26));
		if (GUILayout.Button("НАЗАД", GUILayout.Height(42))) Close();
		GUI.enabled = true;
		GUILayout.EndArea();
		GUI.skin = previousSkin;
		GUI.matrix = previousMatrix;
	}
	private void DrawEditor() {
		GUI.enabled = Authority && !busy;
		if (deleting != null) {
			GUILayout.Label("Удалить выбранное сохранение?");
			GUILayout.Label("Файл будет перенесён в папку Deleted. Оттуда его можно восстановить.");
			if (GUILayout.Button("ДА, УДАЛИТЬ", GUILayout.Height(44))) {
				try { ManualSaveStore.MoveToDeleted(deleting, true); deleting = null; Refresh(); status = "Сохранение перенесено в Deleted."; }
				catch (Exception ex) { status = ex.Message; }
			}
		} else {
			GUILayout.Label("Название");
			editTitle = GUILayout.TextField(editTitle, 80, GUILayout.Height(42));
			editChain = GUILayout.Toggle(editChain, editChain ? "Цепь между игроками: включена" : "Цепь между игроками: выключена", GUILayout.Height(38));
			GUILayout.Label("Длина цепи: " + editLength.ToString("0") + " м");
			editLength = Mathf.Round(GUILayout.HorizontalSlider(editLength, 3, 12));
			editEnemies = GUILayout.Toggle(editEnemies, editEnemies ? "Синхронизация врагов: включена" : "Синхронизация врагов: выключена", GUILayout.Height(38));
			editWorld = GUILayout.Toggle(editWorld, "Синхронизация карты хоста", GUILayout.Height(38));
            GUILayout.Label("Дата, высота, состав игроков, инвентарь и прогресс останутся прежними. Перед изменением создаётся резервная копия.");
			if (GUILayout.Button("СОХРАНИТЬ ИЗМЕНЕНИЯ", GUILayout.Height(44))) {
				try { ManualSaveStore.Edit(editing, editTitle, editChain, editLength, editEnemies || editWorld, editWorld); editing = null; Refresh(); status = "Изменения сохранены. Исходный файл находится в Backups."; }
				catch (Exception ex) { status = ex.Message; }
			}
		}
		GUI.enabled = true;
		GUILayout.Label(status);
		if (GUILayout.Button("ОТМЕНА", GUILayout.Height(42))) { editing = null; deleting = null; }
	}
	public static ManualPlayerSave Capture() => CaptureParticipant(false);
    private static ManualPlayerSave CaptureParticipant(bool allowDead) {
		var watch = System.Diagnostics.Stopwatch.StartNew();
		if (CL_SaveManager.instance == null || ENT_Player.GetPlayer() == null || !allowDead && ENT_Player.GetPlayer().IsDead() || WorldLoader.initialized && !WorldLoader.isLoaded) throw new InvalidOperationException("Дождись загрузки мира и живого персонажа.");
		var states = CL_SaveManager.saveStates;
		bool savedRun = CL_SaveManager.isSavedRun;
		var lastField = AccessTools.Field(typeof(CL_SaveManager), "lastSessionSave");
		var previous = lastField.GetValue(CL_SaveManager.instance);
		try {
			CL_SaveManager.saveStates = new List<CL_SaveManager.SaveState>(states);
			if (DroppedItemModule.Instance.PendingPickups) throw new InvalidOperationException("Дождись завершения передачи предмета.");
			string artifacts = BoundArtifacts.Capture();
			if (WorldLoader.isLoaded) { var info = WorldLoader.GetClosestLevelToPosition(ENT_Player.GetPlayer().transform.position); if (info?.GetLevel() != null) { WorldLoader.instance.currentBranch = info.branch; info.branch.currentLevel = info; } }
            var session = CL_SaveManager.instance.SaveSession(saveToFile: false);
			session.saveStates.RemoveAll(state => state.flag != null && state.flag.StartsWith("session", StringComparison.Ordinal));
			session.saveStates.Add(session.playerSave);
			var player = ENT_Player.GetPlayer();
			return new ManualPlayerSave { Profile = ProfileId, Name = MPSteamworks.Instance.LocalDisplayName, Host = !MPCore.IsInLobby || MPSteamworks.IsHost, Session = JsonUtility.ToJson(session), Health = player.health, LeftGrip = player.hands[0].gripStrength, RightGrip = player.hands[1].gripStrength, Height = CL_GameManager.gMan.GetPlayerCorrectedHeight(), Ascent = CL_GameManager.gMan.GetPlayerAscent(), Settings = new List<string>(MPGameModeManager.CaptureCurrentModeData().activeSettings), Mechanisms = MechanismSnapshots.Capture(), WorldLayout = WorldLayoutSync.CaptureSaved(), Currency = SharedCurrencyItems.CaptureSaved(), Roaches = CL_GameManager.GetRoaches(), Artifacts = artifacts };
		} finally { CL_SaveManager.saveStates = states; CL_SaveManager.isSavedRun = savedRun; lastField.SetValue(CL_SaveManager.instance, previous); MPMain.LogInfo("[Saves] Main thread capture ms=" + watch.ElapsedMilliseconds); }
	}
	private IEnumerator SaveRoutine() {
		if (!Authority || busy) yield break;
		busy = true;
		status = "Собираю состояние участников...";
		request = Guid.NewGuid().ToString("N");
		received.Clear();
		var peers = MPCore.IsInLobby ? MPSteamworks.Instance.ConnectedPeerIds.ToArray() : Array.Empty<ulong>();
		ManualRunSave save = null;
		try {
			save = new ManualRunSave { Id = Guid.NewGuid().ToString("N"), CreatedUtc = DateTime.UtcNow.ToString("O"), GameVersion = Application.version, Height = CL_GameManager.gMan.GetPlayerCorrectedHeight(), PlayerCount = peers.Length + 1, GameMode = CL_GameManager.GetGamemodeName(false, true), Chain = PlayerChain.RoomEnabled, ChainLength = PlayerChain.RoomLength };
			save.Players.Add(Capture());
			save.EnemySync = MPSteamworks.Instance.IsLan ? EnemySyncModule.Instance.IsEnabled : MPConfig.EnemySync;
			save.WorldSync = WorldLayoutSync.Enabled; save.WorldLayout = save.Players[0].WorldLayout; save.Currency = save.Players[0].Currency; save.Roaches = save.Players[0].Roaches;
			foreach (ulong peer in peers) Send(peer, PacketType.SaveRequest, request);
		} catch (Exception ex) { save = null; status = "Не удалось сохранить: " + ex.Message; statusUntil = Time.unscaledTime + 20; MPMain.LogError("[Saves] Save failed: " + ex); }
		if (save != null) {
			float deadline = Time.unscaledTime + 20; float nextRetry = Time.unscaledTime + 2;
			while (peers.Any(peer => !received.ContainsKey(peer)) && Time.unscaledTime < deadline && Authority) { if (Time.unscaledTime >= nextRetry) { nextRetry = Time.unscaledTime + 2; foreach (ulong peer in peers.Where(peer => !received.ContainsKey(peer))) Send(peer, PacketType.SaveRequest, request); } yield return null; }
			Task<string> write = null;
			string directory = SaveDirectory;
			try {
				if (!Authority || peers.Any(peer => !received.ContainsKey(peer))) throw new InvalidOperationException("Не все участники ответили. Сохранение не создано, попробуй снова.");
				save.Players.AddRange(peers.Select(peer => received[peer]));
				BoundArtifacts.Normalize(save);
				write = Task.Run(() => { save.Mechanisms = MechanismSnapshots.Merge(save.Players.Select(player => player.Mechanisms)); foreach (var participant in save.Players) { participant.Mechanisms = null; participant.WorldLayout = null; participant.Currency = null; } return ManualSaveStore.Write(directory, save); });
			} catch (Exception ex) { status = "Не удалось сохранить: " + ex.Message; statusUntil = Time.unscaledTime + 20; MPMain.LogError("[Saves] Save failed: " + ex); }
			while (write != null && !write.IsCompleted) yield return null;
			if (write != null) try {
				string savedPath = write.GetAwaiter().GetResult(); MPMain.LogInfo("[Saves] Created " + savedPath + ", height=" + save.Height + ", players=" + save.PlayerCount);
				var info = new FileInfo(savedPath); saveCache[savedPath] = (info.Length, info.LastWriteTimeUtc.Ticks, save); saves.Insert(0, (savedPath, save));
				status = "Игра сохранена. Высота: " + Mathf.RoundToInt(save.Height) + " м. Игроков: " + save.PlayerCount; statusUntil = Time.unscaledTime + 15;
			} catch (Exception ex) { status = "Не удалось сохранить: " + ex.Message; statusUntil = Time.unscaledTime + 20; MPMain.LogError("[Saves] Save failed: " + ex); }
		}
		request = null;
		received.Clear();
		busy = false;
	}
	public void ReceiveRequest(ulong sender, string token) {
		if (!MPSteamworks.Instance.IsLan || sender != MPSteamworks.Instance.HostSteamId || MPSteamworks.IsHost || !Guid.TryParseExact(token, "N", out _)) return;
		try { Send(sender, PacketType.SaveResponse, token + "\n" + ManualSaveStore.Pack(JsonConvert.SerializeObject(CaptureParticipant(true)))); }
		catch (Exception ex) { MPMain.LogError("[Saves] Capture: " + ex.Message); }
	}
	public void ReceiveResponse(ulong sender, string data) {
		if (!MPSteamworks.IsHost || request == null || !MPSteamworks.Instance.HasConnection(sender) || received.ContainsKey(sender)) return;
		int separator = data.IndexOf('\n');
		if (separator < 0 || data.Substring(0, separator) != request) return;
		try {
			var player = JsonConvert.DeserializeObject<ManualPlayerSave>(ManualSaveStore.Unpack(data.Substring(separator + 1)));
			ValidateSession(player.Session);
			player.Host = false;
			player.Profile = MPSteamworks.Instance.GetMemberData(sender, ProfileKey) ?? player.Profile;
			player.Name = MPSteamworks.Instance.GetMemberName(sender);
			received[sender] = player;
		} catch (Exception ex) { MPMain.LogError("[Saves] Response: " + ex.Message); }
	}
	private static void Send(ulong target, PacketType type, string value) {
		if (value.Length > 900000) throw new InvalidDataException("Сохранение слишком большое для передачи.");
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, target, type);
		writer.Put(value);
		MPSteamworks.Instance.SendToPeer(target, writer);
	}
	private void TryLoad(string path) {
		try {
			if (!Authority || busy || Loading || GroupLoading) return;
			var save = ManualSaveStore.Read(path);
			if (save.GameVersion != Application.version) throw new InvalidOperationException("Версия игры отличается от сохранения.");
			if (!string.Equals(save.GameMode, CL_GameManager.GetGamemodeName(false, true), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Сначала запусти режим " + save.GameMode + ", затем выбери это сохранение.");
			foreach (var player in save.Players) ValidateSession(player.Session);
			BoundArtifacts.Normalize(save);
			loadedRun = save;
			BeginHostHandshake();
			if (!MPCore.IsInLobby && save.WorldSync.HasValue && LanMenu.Instance != null) LanMenu.Instance.WorldSync = save.WorldSync.Value;
			if (MPSteamworks.Instance.IsLan) {
				if (save.WorldSync.HasValue) MPSteamworks.Instance.SetLobbyData(WorldLayoutSync.EnabledKey, save.WorldSync.Value.ToString());
                MPSteamworks.Instance.SetLobbyData(PlayerChain.EnabledKey, save.Chain.ToString());
				if (save.EnemySync.HasValue) MPSteamworks.Instance.SetLobbyData(MPKeys.ENEMY_SYNC, (WorldLayoutSync.Enabled || save.EnemySync.Value).ToString());
				MPSteamworks.Instance.SetLobbyData(PlayerChain.LengthKey, save.ChainLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
				restoredMembers.Clear();
				foreach (ulong peer in MPSteamworks.Instance.ConnectedPeerIds) { SendLoad(peer, save); restoredMembers.Add(peer); }
				loadedRoom = MPSteamworks.Instance.HostSteamId;
			}
			var host = save.Players.FirstOrDefault(player => player.Host) ?? save.Players[0];
			host.LoadToken = loadToken;
			host.Mechanisms = save.Mechanisms; host.WorldLayout = save.WorldLayout; host.Currency = save.Currency; host.Roaches = save.Roaches;
			host.OtherArtifacts = BoundArtifacts.ReservedByOthers(save, host.Profile);
			BeginLoad(host);
		} catch (Exception ex) { AbortLoad(); status = "Не удалось загрузить: " + ex.Message; MPMain.LogError("[Saves] Load: " + ex); }
	}
	private void SendLoad(ulong peer, ManualRunSave save) {
		string profile = MPSteamworks.Instance.GetMemberData(peer, ProfileKey);
		var player = save.Players.FirstOrDefault(item => !item.Host && item.Profile == profile);
		if (player == null) player = save.Players.FirstOrDefault(item => !item.Host && item.Name == MPSteamworks.Instance.GetMemberName(peer));
		if (player == null) {
			var host = save.Players.First(item => item.Host);
			player = new ManualPlayerSave { Profile = profile, Name = MPSteamworks.Instance.GetMemberName(peer), Session = host.Session, Health = host.Health, LeftGrip = host.LeftGrip, RightGrip = host.RightGrip, Height = host.Height, Ascent = host.Ascent, Settings = host.Settings, KeepLocalInventory = true };
		} else player = AlignNearbySavedPlayer(player, save.Players.First(item => item.Host));
		player.OtherArtifacts = BoundArtifacts.ReservedByOthers(save, player.Profile);
		player.LoadToken = loadToken;
		player.Mechanisms = save.Mechanisms; player.WorldLayout = save.WorldLayout; player.Currency = save.Currency; player.Roaches = save.Roaches ?? JsonUtility.FromJson<CL_SaveManager.SessionSave>(save.Players.First(item => item.Host).Session).roaches;
		Send(peer, PacketType.SaveLoad, ManualSaveStore.Pack(JsonConvert.SerializeObject(player)));
	}
	private static ManualPlayerSave AlignNearbySavedPlayer(ManualPlayerSave player, ManualPlayerSave host) {
		var target = ValidateSession(player.Session);
		var source = ValidateSession(host.Session);
		string a = target.playerSave.levelData?.FirstOrDefault()?.levelName;
		string b = source.playerSave.levelData?.FirstOrDefault()?.levelName;
		if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return player;
		var copy = JsonConvert.DeserializeObject<ManualPlayerSave>(JsonConvert.SerializeObject(player));
		target.worldSeed = source.worldSeed;
		target.playerSave.levelData = source.playerSave.levelData;
		target.playerSave.playerData.positionRelativeToLevel = source.playerSave.playerData.positionRelativeToLevel + Vector3.right;
		target.playerSave.playerData.playerRotation = source.playerSave.playerData.playerRotation;
		if (target.saveStates.Count > 0 && target.saveStates[target.saveStates.Count - 1].flag?.StartsWith("session", StringComparison.Ordinal) == true) target.saveStates[target.saveStates.Count - 1] = target.playerSave;
		copy.Session = JsonUtility.ToJson(target);
		copy.Height = host.Height;
		copy.Ascent = host.Ascent;
		MPMain.LogInfo("[Saves] Aligned nearby players to the same saved world while preserving personal inventory");
		return copy;
	}
	private static string LevelFamily(string name) {
		int separator = name.LastIndexOf('_');
		if (separator < 0 || separator == name.Length - 1) return name;
		for (int i = separator + 1; i < name.Length; i++) if (!char.IsDigit(name[i])) return name;
		return name.Substring(0, separator);
	}
	public void ReceiveLoad(ulong sender, string data) {
		if (!MPSteamworks.Instance.IsLan || MPSteamworks.IsHost || sender != MPSteamworks.Instance.HostSteamId) return;
		try { var player = JsonConvert.DeserializeObject<ManualPlayerSave>(ManualSaveStore.Unpack(data)); if (!AcceptLoadToken(player)) return; StartCoroutine(WaitForRemoteLoad(player, sender)); }
		catch (Exception ex) { MPMain.LogError("[Saves] Remote load: " + ex); }
	}
	private IEnumerator WaitForRemoteLoad(ManualPlayerSave player, ulong sender) {
		float deadline = Time.unscaledTime + 120;
		while (MPSteamworks.Instance.HasConnection(sender) && (!InGame || !MPCore.IsInitialized || CL_SaveManager.instance == null || ENT_Player.GetPlayer() == null || SceneManager.GetActiveScene().name == "Game-Main" && (!WorldLoader.initialized || !WorldLoader.isLoaded) || Loading) && Time.unscaledTime < deadline) yield return null;
		if (player.LoadToken != null && player.LoadToken != loadToken) yield break;
		if (!MPSteamworks.Instance.HasConnection(sender) || Time.unscaledTime >= deadline) { MPMain.LogError("[Saves] Remote load timed out waiting for world"); AbortLoad(); yield break; }
		try {
			if (player.KeepLocalInventory) {
				var local = ValidateSession(CaptureParticipant(true).Session);
				var target = ValidateSession(player.Session);
				player.Artifacts = PersonalEquipment.ApplyFallback(target, local);
				player.Session = JsonUtility.ToJson(target);
			}
			BoundArtifacts.Exclude(player, player.OtherArtifacts);
			BeginLoad(player);
		} catch (Exception ex) { MPMain.LogError("[Saves] Remote load: " + ex); AbortLoad(); }
	}
	private static CL_SaveManager.SessionSave ValidateSession(string json) {
		if (string.IsNullOrEmpty(json) || json.Length > 32 * 1024 * 1024) throw new InvalidDataException("Неверные данные участника.");
		var session = JsonUtility.FromJson<CL_SaveManager.SessionSave>(json);
		if (session?.playerSave?.playerData == null || session.saveStates == null || session.sessionFlags == null) throw new InvalidDataException("Состояние забега повреждено.");
		if (WorldLoader.initialized && (session.playerSave.levelData == null || session.playerSave.levelData.Count == 0)) throw new InvalidDataException("В сохранении отсутствует карта.");
		return session;
	}
	private void BeginLoad(ManualPlayerSave player) {
		if (Loading) throw new InvalidOperationException("Загрузка уже идёт.");
		CoopEnding.Reset();
		var session = ValidateSession(player.Session);
		if (CL_SaveManager.instance == null) throw new InvalidOperationException("Дождись загрузки игры.");
		IsOpen = false;
		BlockModal(false);
		CoopSpectator.Exit();
		AccessTools.Field(typeof(CL_GameManager), "dead").SetValue(CL_GameManager.gMan, false);
		AccessTools.Field(typeof(CL_GameManager), "reviving")?.SetValue(CL_GameManager.gMan, false);
		AccessTools.Field(typeof(CL_GameManager), "runHasEnded")?.SetValue(CL_GameManager.gMan, false);
		foreach (var screen in FindObjectsOfType<UI_ScoreScreen>(true)) { screen.StopAllCoroutines(); screen.gameObject.SetActive(false); }
		CL_GameManager.gMan.UnPause();
		BoundArtifacts.BeginRestore(player);
		WorldDoors.ResetPending(); CoopCoordinates.Reset(); CoopPresence.Views.Clear();
		Loading = true;
		WorldLayoutSync.ResetGeneration(); WorldLayoutSync.SetSaved(player.WorldLayout);
		LoadTargetLevel = session.playerSave.levelData?.FirstOrDefault()?.levelName;
		LoadTargetRegion = session.playerSave.levelData?.FirstOrDefault()?.regionName;
		MPMain.LogInfo("[Saves] Loading participant " + player.Name + ", inventory=" + (session.playerSave.playerData.inventory?.Length ?? 0) + ", hands=" + (session.playerSave.playerData.handInventory?.Count(item => item != null && !string.IsNullOrEmpty(item.prefabName)) ?? 0) + ", token=" + player.LoadToken);
		try {
			WKMPMod.Patch.Patch_CL_GameManager.RestartHeightOffset();
			if (player.Settings != null) StatManager.saveData.GetGameMode(CL_GameManager.GetBaseGamemode().gamemodeName).activeSettings = new List<string>(player.Settings);
			if (WorldLoader.instance != null) { WorldLoader.instance.seed = session.worldSeed; WorldLoader.SetPresetSeed(session.worldSeed.ToString()); }
			WorldSyncManager.Instance.ResetAll();
			PendingNativeSave = session;
			CL_SaveManager.instance.LoadSession();
			StartCoroutine(FinishLoad(player));
		} catch { Loading = false; LoadTargetLevel = null; LoadTargetRegion = null; throw; }
		finally { PendingNativeSave = null; }
	}
	private IEnumerator FinishLoad(ManualPlayerSave savedPlayer) {
		yield return null;
		float deadline = Time.unscaledTime + 120;
		while (InGame && WorldLoader.initialized && (!WorldLoader.isLoaded || WorldLayoutSync.RestoringBranches) && Time.unscaledTime < deadline) yield return null;
		if (!InGame || WorldLoader.initialized && !WorldLoader.isLoaded) { Loading = false; LoadTargetLevel = null; LoadTargetRegion = null; MPMain.LogError("[Saves] World load failed or timed out"); AbortLoad(); yield break; }
		yield return null;
		yield return new WaitForSecondsRealtime(0.15f);
		if (savedPlayer.LoadToken != null && savedPlayer.LoadToken != loadToken) yield break;
		try { CompleteLoad(savedPlayer); }
		catch (Exception ex) { AbortLoad(); status = "Не удалось полностью восстановить сохранение: " + ex.Message; MPMain.LogError("[Saves] Restore: " + ex); }
		finally { Loading = false; LoadTargetLevel = null; LoadTargetRegion = null; }
	}
	private void CompleteLoad(ManualPlayerSave savedPlayer) {
		try { MechanismSnapshots.Restore(savedPlayer.Mechanisms); }
		catch (Exception ex) { MPMain.LogError("[Saves] Mechanism restore: " + ex); }
		var player = ENT_Player.GetPlayer();
		if (player != null) {
			if (WorldLoader.isLoaded) { var info = WorldLoader.GetClosestLevelToPosition(player.transform.position); if (info?.GetLevel() != null) { WorldLoader.instance.currentBranch = info.branch; info.branch.currentLevel = info; } }
            var inventoryState = JsonUtility.FromJson<CL_SaveManager.SessionSave>(savedPlayer.Session);
			inventoryState.playerSave.playerData.LoadDataIntoPlayer(player, true, false, false, "other");
			BoundArtifacts.Restore(savedPlayer.Artifacts);
			EnsureFlashlight(); SharedCurrencyItems.RestoreSaved(savedPlayer.Currency); CL_GameManager.SetRoaches(savedPlayer.Roaches ?? inventoryState.roaches);
			AccessTools.Field(typeof(CL_GameManager), "currentWorldOffset").SetValue(CL_GameManager.gMan, player.transform.position.y - savedPlayer.Height);
			AccessTools.Field(typeof(CL_GameManager), "playerAscent").SetValue(CL_GameManager.gMan, savedPlayer.Ascent);
			AccessTools.Field(typeof(ENT_Player), "vel").SetValue(player, Vector3.zero);
			var native = JsonUtility.FromJson<CL_SaveManager.SessionSave>(savedPlayer.Session);
			CL_GameManager.gMan.SetPlayerBestTravelDistance(native.travelDistance);
			var rotation = native.playerSave.playerData.playerRotation;
			if (WorldLoader.initialized) rotation = WorldLoader.GetCurrentLevelFromBounds().GetLevel().transform.rotation * rotation;
			player.transform.rotation = rotation;
			player.camTransform.localRotation = native.playerSave.playerData.playerCameraRotation;
			player.health = Mathf.Clamp(savedPlayer.Health, 0.1f, player.maxHealth);
			player.hands[0].SetGripStrength(savedPlayer.LeftGrip);
			player.hands[1].SetGripStrength(savedPlayer.RightGrip);
			ResetPlayerEffects();
			CoopPassageDoor.RepairLegacy(savedPlayer.Mechanisms);
			CL_GameManager.ResetTimescale(); CL_GameManager.SetTimescale(1); AudioListener.pause = false; CL_GameManager.gMan.isPaused = false; CL_GameManager.gMan.pauseMenu.SetActive(false);
		}
		Loading = false;
		foreach (var item in BoundArtifacts.Items()) BoundArtifacts.Remember(item);
		PersonalEquipment.Capture();
		WKMPMod.Component.LocalPlayer.Instance.TriggerTeleport();
		WKMPMod.Component.LocalPlayer.Instance.RequestFullUpdate();
		if (MPSteamworks.Instance.IsLan && MPSteamworks.IsHost) {
			MPSteamworks.Instance.SetLobbyData(MPKeys.GAMEMODE_JSON, JsonConvert.SerializeObject(MPGameModeManager.CaptureCurrentModeData()));
		}
		MPMain.LogInfo("[Saves] Manual run restored without leaving room");
		CompletedLoad(savedPlayer.LoadToken);
	}
	private static void EnsureFlashlight() {
		try {
			var inventory = Inventory.instance;
			if (inventory == null) return;
			bool IsFlashlight(Item item) => item != null && string.Equals(item.prefabName, "Item_Flashlight", StringComparison.OrdinalIgnoreCase);
			if (inventory.GetItems(true, true, true).Any(IsFlashlight) || inventory.GetAllPouches().Any(pouch => pouch != null && pouch.pouchItems != null && pouch.pouchItems.Any(IsFlashlight))) return;
			var prefab = CL_AssetManager.GetItemObjectPrefab("Item_Flashlight");
			if (prefab?.itemData == null) { MPMain.LogWarning("[Saves] Flashlight prefab not found"); return; }
			inventory.AddItemToInventoryCenter(prefab.itemData.GetClone());
		} catch (Exception ex) { MPMain.LogError("[Saves] Could not provide flashlight: " + ex.Message); }
	}
	private static void ResetPlayerEffects(bool clearTemporary = false) {
		var player = ENT_Player.GetPlayer();
		var game = CL_GameManager.gMan;
		if (player == null || game == null || player.IsDead()) return;
		player.SetCutsceneMode(false); player.SetSpeedMult(1); CL_GameManager.SetSafe(false);
		if (clearTemporary && player.curBuffs?.currentBuffs != null) foreach (var buff in player.curBuffs.currentBuffs.ToArray()) if (buff != null && buff.loseOverTime) player.RemoveBuff(buff);
		if (DEN_Teeth.instance != null && (bool)AccessTools.Field(typeof(DEN_Teeth), "locked").GetValue(DEN_Teeth.instance)) { DEN_Teeth.instance.StopAllCoroutines(); AccessTools.Field(typeof(DEN_Teeth), "locked").SetValue(DEN_Teeth.instance, false); if (DEN_Teeth.instance.screenHandEffect != null) DEN_Teeth.instance.screenHandEffect.Clear(); }
		if (player.bloodEffect != null) player.bloodEffect.Clear();
		foreach (var takeover in FindObjectsOfType<UT_PlayerTakeOver>()) {
			if (player.transform.IsChildOf(takeover.transform)) { takeover.SetActive(false); player.transform.SetParent(null, true); }
		}
		player.UnlockInteraction();
		player.UnLock();
		game.lockPlayer = false;
		game.lockPlayerInput = false;
		foreach (var hand in player.hands) if (hand != null) { hand.SetLocked(false); hand.lockHand = false; }
		if (CL_UIManager.instance != null) { CL_UIManager.instance.SetFadeTarget(0); CL_UIManager.instance.SetFadeAmount(0); }
	}
	private void PlayerJoined(SteamId peer) {
		if (loadedRun != null && MPSteamworks.Instance.IsLan && MPSteamworks.IsHost && restoredMembers.Add(peer.Value)) StartCoroutine(RestoreJoined(peer.Value));
	}
	private IEnumerator RestoreJoined(ulong peer) {
		yield return new WaitForSecondsRealtime(1);
		while (loadedRun != null && (Loading || busy) && MPSteamworks.Instance.HasConnection(peer)) yield return null;
		if (loadedRun != null && MPSteamworks.Instance.HasConnection(peer)) SendLoad(peer, loadedRun);
	}
}




