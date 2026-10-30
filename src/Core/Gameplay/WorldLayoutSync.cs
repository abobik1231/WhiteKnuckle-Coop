using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.UI;
using WKMPMod.Util;

namespace WKMPMod.Gameplay;

public sealed class WorldLayoutSync : MonoBehaviour {
	public const string EnabledKey = "host world experimental";
	public static bool Enabled => MPSteamworks.Instance.IsLan && bool.TryParse(MPSteamworks.Instance.GetLobbyValue(EnabledKey), out var value) && value;
	public static bool ApplyingEvent { get; private set; }
	private static int waiting;
	public static bool WaitingForMap => waiting > 0;
	private static readonly Dictionary<string, Plan> plans = new Dictionary<string, Plan>();
	private static readonly Dictionary<int, Room> rooms = new Dictionary<int, Room>();
	private sealed class LayoutResolver : DefaultContractResolver {
		protected override List<MemberInfo> GetSerializableMembers(Type type) => type.GetFields(BindingFlags.Instance | BindingFlags.Public).Cast<MemberInfo>().ToList();
	}
	private static readonly JsonSerializerSettings layoutJson = new JsonSerializerSettings { ContractResolver = new LayoutResolver(), MaxDepth = 32 };
	private static string SerializePlan(Plan plan) => JsonConvert.SerializeObject(plan, layoutJson);
	private static readonly AccessTools.FieldRef<WorldLoader, List<WorldLoader.BranchInfo>> Branches = AccessTools.FieldRefAccess<WorldLoader, List<WorldLoader.BranchInfo>>("branches");
	private static Generation current;
	private static Plan savedPlan;
	private static readonly Dictionary<string, Plan> savedBranches = new Dictionary<string, Plan>();
	public static bool RestoringBranches { get; private set; }
	public static bool HasSavedPlan => savedPlan != null;
	public static void SetSaved(string json) {
		savedBranches.Clear(); savedPlan = string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<Plan>(json, layoutJson);
		void Add(Plan plan, int depth) { if (depth > 12 || plan.Rooms == null || plan.Rooms.Count == 0 || plan.Rooms.Count > 2048) throw new InvalidOperationException("Повреждена сохранённая карта."); savedBranches[plan.Branch] = plan; if (plan.Children != null) foreach (var child in plan.Children) Add(child, depth + 1); }
		if (savedPlan != null) Add(savedPlan, 0);
	}
	public static string CaptureSaved() {
		if (!WorldLoader.isLoaded || WorldLoader.instance == null) return null;
		var branch = WorldLoader.instance.currentBranch;
		var plan = Existing("saved", branch.id);
		if (plan == null) return null;
		var live = branch.levelTracker.Where(entry => entry?.GetLevel() != null).Select(entry => entry.GetLevel()).ToList();
		int index = live.IndexOf(WorldLoader.GetClosestLevelToPosition(ENT_Player.GetPlayer().transform.position)?.GetLevel());
		var kept = new HashSet<string>(live.Skip(Math.Max(0, index - 1)).Select(level => level.levelName + ":" + level.GetLevelSeed()));
		plan.Rooms.RemoveAll(room => !room.HasPose || !kept.Contains(room.Id + ":" + room.Seed));
		plan.Continuation = Paused(branch)?.Select(id => new Room { Id = id.level, Region = id.region, Subregion = id.subRegion }).ToList();
		CaptureChildren(plan, branch, 0);
		return SerializePlan(plan);
	}
	private static void CaptureChildren(Plan plan, WorldLoader.BranchInfo branch, int depth) {
		if (depth > 8) return;
		var generator = branch.generationBranch ?? (ReferenceEquals(branch, WorldLoader.instance.currentBranch) ? Unwrap(CurrentGenerator(WorldLoader.instance)) as M_GenerationBranch : null);
		plan.Generator = generator != null ? MPUtil.CleanCloneName(generator.name) : null;
		plan.Children = new List<Plan>();
		foreach (var child in AllBranches().Where(item => item.parent == branch && item.parentLevel?.GetLevel() != null)) {
			var parent = child.parentLevel.GetLevel(); if (!plan.Rooms.Any(room => room.Id == parent.levelName && room.Seed == parent.GetLevelSeed())) continue;
			var next = Existing("saved", child.id); if (next == null) continue;
			next.Rooms.RemoveAll(room => !room.HasPose); if (next.Rooms.Count == 0) continue;
			next.ParentLevel = parent.levelName; next.ParentSeed = parent.GetLevelSeed();
			next.Continuation = Paused(child)?.Select(id => new Room { Id = id.level, Region = id.region, Subregion = id.subRegion }).ToList();
			CaptureChildren(next, child, depth + 1); plan.Children.Add(next);
		}
	}
	private static readonly AccessTools.FieldRef<WorldLoader, WorldGenerator> CurrentGenerator = AccessTools.FieldRefAccess<WorldLoader, WorldGenerator>("currentGenerator");
	private static readonly AccessTools.FieldRef<WorldLoader.BranchInfo, List<WorldLoader.BranchInfo.LevelId>> Paused = AccessTools.FieldRefAccess<WorldLoader.BranchInfo, List<WorldLoader.BranchInfo.LevelId>>("pausedLevelIDs");
	private static readonly AccessTools.FieldRef<WorldLoader.LevelInfo, WorldLoader.BranchInfo.LevelId> LevelId = AccessTools.FieldRefAccess<WorldLoader.LevelInfo, WorldLoader.BranchInfo.LevelId>("levelID");
	private static ulong session;
	private float nextSnapshot;
	private string lastMechanisms;
	private Task<string> packedSnapshot;
	private string packingMechanisms;
	private int packingEpoch;
	private static int generationEpoch;
	private readonly Dictionary<string, float> pendingStates = new Dictionary<string, float>();
	[Serializable] private sealed class Room {
		public string Id;
		public string Region;
		public string Subregion;
		public bool Pause;
		public bool Flip;
		public int Seed;
		public bool HasPose;
		public Vector3 Position;
		public Quaternion Rotation;
		public Vector3 Scale;
	}
	[Serializable] private sealed class Plan {
		public string Key;
		public string Branch;
		public int Seed;
		public string Random;
		public bool Direct;
		public int Type;
		public List<Room> Rooms = new List<Room>();
		public List<Room> Continuation;
		public List<Plan> Children;
		public string ParentLevel;
		public int ParentSeed;
		public string Generator;
	}
	private sealed class Generation {
		public WorldLoader.BranchInfo Branch;
		public WorldLoader.GenerationParameters Parameters;
		public string Key;
		public int Spawned;
		public Plan Plan;
		public bool FromSave;
	}
	private sealed class Generator : WorldGenerator {
		private readonly WorldGenerator original;
		private readonly Generation generation;
		public WorldGenerator Original => original;
		public Generator(WorldGenerator original, Generation generation) { this.original = original; this.generation = generation; }
		public List<M_Level.LevelAssetHolder> GetGenerationList(M_Region lastRegion = null, M_Region regionToGenerate = null, M_Subregion startAfterSubregion = null) {
			if (generation.FromSave) return Assets(generation.Plan);
			if (!Enabled) return original.GetGenerationList(lastRegion, regionToGenerate, startAfterSubregion);
			if (MPSteamworks.IsHost) {
				var result = original.GetGenerationList(lastRegion, regionToGenerate, startAfterSubregion);
				var plan = MakePlan(generation, result, false);
				generation.Plan = plan;
				plans[plan.Key] = plan;
				SendPlan(0, plan);
				return result;
			}
			var authoritative = generation.Plan ?? throw new InvalidOperationException("Карта хоста ещё не получена.");
			if (!string.IsNullOrEmpty(authoritative.Random)) UnityEngine.Random.state = JsonUtility.FromJson<UnityEngine.Random.State>(authoritative.Random);
			return Assets(authoritative);
		}
	}
	private static List<M_Level.LevelAssetHolder> Assets(Plan authoritative) {
		var list = new List<M_Level.LevelAssetHolder>();
		foreach (var entry in authoritative.Rooms) {
				var asset = CL_AssetManager.GetLevelAsset(entry.Id);
				if (asset == null) throw new InvalidOperationException("В игре отсутствует комната хоста: " + entry.Id);
				var copy = new M_Level.LevelAssetHolder { id = asset.id, level = asset.level, levelAssetReference = asset.levelAssetReference, length = asset.length, tags = asset.tags, spawnSettings = asset.spawnSettings, setCustomRegionInfo = asset.setCustomRegionInfo, pauseGeneration = asset.pauseGeneration, isPausingGeneration = entry.Pause, region = string.IsNullOrEmpty(entry.Region) ? asset.region : CL_AssetManager.GetRegionAsset(entry.Region), subregion = string.IsNullOrEmpty(entry.Subregion) ? asset.subregion : CL_AssetManager.GetSubregionAsset(entry.Subregion) };
			list.Add(copy);
		}
		return list;
	}
	private static Plan MakePlan(Generation generation, List<M_Level.LevelAssetHolder> assets, bool direct) {
		var plan = new Plan { Key = generation.Key, Branch = generation.Branch.id, Seed = WorldLoader.instance.seed, Random = direct ? null : JsonUtility.ToJson(UnityEngine.Random.state), Direct = direct, Type = (int)generation.Parameters.generationType };
		int index = generation.Branch.GetLevelIds()?.Count ?? 0;
		foreach (var entry in assets) {
			ulong hash = MPUtil.Hash64(generation.Branch.id + ":" + index + ":" + plan.Seed);
			plan.Rooms.Add(new Room { Id = entry.id, Region = entry.region?.name, Subregion = entry.subregion?.name, Pause = entry.isPausingGeneration, Flip = (hash & 1) == 1, Seed = unchecked(plan.Seed + index++ * 999) });
		}
		return plan;
	}
	public static WorldGenerator Unwrap(WorldGenerator generator) { while (generator is Generator wrapped) generator = wrapped.Original; return generator; }
	public static IEnumerator Wrap(IEnumerator original, WorldLoader.BranchInfo branch, WorldLoader.GenerationParameters parameters, WorldGenerator generator) {
		bool restoring = ManualSaves.Loading && savedPlan != null && (branch.GetLevelIds()?.Count ?? 0) == 0;
		if (!Enabled && !restoring) {
			UnityEngine.Random.State state = UnityEngine.Random.state; bool started = false;
			while (true) {
				bool moved; var previousRandom = UnityEngine.Random.state;
				try { if (started) UnityEngine.Random.state = state; moved = original.MoveNext(); state = UnityEngine.Random.state; started = true; }
				finally { UnityEngine.Random.state = previousRandom; }
				if (!moved) break; yield return original.Current;
			}
			(original as IDisposable)?.Dispose(); yield break;
		}
		EnsureSession();
		generator = Unwrap(generator);
		string target = parameters.preloadLevels?.FirstOrDefault()?.id ?? "";
		string key = branch.id + ":" + (branch.GetLevelIds()?.Count ?? 0) + ":" + target;
		Plan restored = null;
		if (restoring && !savedBranches.TryGetValue(branch.id, out restored)) restored = savedPlan;
		var generation = new Generation { Branch = branch, Parameters = parameters, Key = key, FromSave = restoring, Plan = restored };
		var paused = Paused(branch);
		if (!restoring && MPSteamworks.IsHost && (paused?.Count > 0 || parameters.generationType == WorldLoader.GenerationParameters.GenerationType.replace && parameters.preloadLevels != null)) {
			var assets = paused?.Count > 0 ? paused.Select(id => { var asset = CL_AssetManager.GetLevelAsset(id.level); if (!string.IsNullOrEmpty(id.region)) asset.region = CL_AssetManager.GetRegionAsset(id.region); if (!string.IsNullOrEmpty(id.subRegion)) asset.subregion = CL_AssetManager.GetSubregionAsset(id.subRegion); return asset; }).ToList() : parameters.preloadLevels;
			generation.Plan = MakePlan(generation, assets, true); plans[key] = generation.Plan; SendPlan(0, generation.Plan);
		}
		if (restoring) {
			branch.id = restored.Branch; paused?.Clear();
			WorldLoader.instance.seed = restored.Seed;
			if (!string.IsNullOrEmpty(restored.Generator)) { var asset = Resources.FindObjectsOfTypeAll<M_GenerationBranch>().FirstOrDefault(item => MPUtil.CleanCloneName(item.name) == restored.Generator); if (asset != null) { branch.generationBranch = UnityEngine.Object.Instantiate(asset); branch.generationBranch.id = branch.id; generator = branch.generationBranch; } }
			parameters.generationType = parameters.preloadLevels?.Count > 0 ? WorldLoader.GenerationParameters.GenerationType.generateUntilFound : WorldLoader.GenerationParameters.GenerationType.replace;
			if (parameters.preloadLevels?.Count > 0 && !restored.Rooms.Any(room => room.Id == parameters.preloadLevels[0].id)) throw new InvalidOperationException("Сохранённая карта не содержит комнату игрока.");
			if (parameters.preloadLevels == null) parameters.preloadLevels = Assets(restored);
		} else if (!MPSteamworks.IsHost) {
			float deadline = Time.unscaledTime + 60f;
			float nextRequest = 0;
			waiting++; WorldLoader.isLoaded = false; CL_GameManager.SetLoading(true);
			try {
				while (Enabled && !plans.TryGetValue(key, out generation.Plan)) {
					if (Time.unscaledTime >= deadline) { Abort("Timed out waiting for " + key); yield break; }
					if (Time.unscaledTime >= nextRequest) { nextRequest = Time.unscaledTime + 2f; var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPSteamworks.Instance.HostSteamId, PacketType.WorldLayout); writer.Put((byte)0).Put(key).Put(branch.id); MPSteamworks.Instance.SendToHost(writer); }
					yield return null;
				}
			} finally { waiting = Mathf.Max(0, waiting - 1); }
			if (!Enabled) { CL_GameManager.SetLoading(false); yield break; }
			WorldLoader.instance.seed = generation.Plan.Seed;
			if (!string.IsNullOrEmpty(generation.Plan.Branch)) branch.id = generation.Plan.Branch;
			paused?.Clear();
			if (ManualSaves.Loading && parameters.preloadLevels?.Count > 0) {
				string required = parameters.preloadLevels[0].id;
				if (!generation.Plan.Rooms.Any(room => room.Id == required)) { Abort("Saved room " + required + " is absent from host plan " + key); yield break; }
				parameters.generationType = WorldLoader.GenerationParameters.GenerationType.generateUntilFound;
			} else {
				parameters.generationType = WorldLoader.GenerationParameters.GenerationType.replace;
				parameters.preloadLevels = Assets(generation.Plan);
				if (parameters.levelData?.Count > 0) { parameters.levelData = null; parameters.setPlayerPositionToStartLevel = true; }
			}
			MPMain.LogInfo("[Host world] Applying " + key + ", rooms=" + generation.Plan.Rooms.Count + ", saved=" + ManualSaves.Loading);
		}
		var wrappedGenerator = new Generator(generator, generation);
		parameters.generator = wrappedGenerator;
		parameters.forceResetGeneratorToGamemode = false;
		try {
			UnityEngine.Random.State state = UnityEngine.Random.state; bool started = false;
			while (true) {
				bool moved;
				var previous = current; current = generation;
				var previousRandom = UnityEngine.Random.state;
				try { if (started) UnityEngine.Random.state = state; moved = original.MoveNext(); state = UnityEngine.Random.state; started = true; } finally { current = previous; UnityEngine.Random.state = previousRandom; }
				if (!moved) break;
				yield return original.Current;
			}
		} finally { (original as IDisposable)?.Dispose(); if (WorldLoader.instance != null && ReferenceEquals(CurrentGenerator(WorldLoader.instance), wrappedGenerator)) CurrentGenerator(WorldLoader.instance) = generator; }
		if (restoring && restored?.Continuation?.Count > 0) Paused(branch) = restored.Continuation.Select((room, index) => new WorldLoader.BranchInfo.LevelId { id = (branch.GetLevelIds()?.Count ?? 0) + index, level = room.Id, region = room.Region, subRegion = room.Subregion }).ToList();
		if (restoring && restored?.Children?.Count > 0) {
			bool previous = RestoringBranches; RestoringBranches = true;
			try { yield return RestoreChildren(restored, branch); } finally { RestoringBranches = previous; }
		}
	}
	private static IEnumerator RestoreChildren(Plan plan, WorldLoader.BranchInfo parent) {
		foreach (var child in plan.Children) {
			var level = parent.levelTracker.FirstOrDefault(entry => entry.GetLevel() != null && entry.GetLevel().levelName == child.ParentLevel && entry.GetLevel().GetLevelSeed() == child.ParentSeed);
			if (level == null || AllBranches().Any(branch => branch.id == child.Branch)) continue;
			var branch = new WorldLoader.BranchInfo { id = child.Branch, parent = parent, parentLevel = level, originTransform = level.GetLevel().transform };
			Branches(WorldLoader.instance).Add(branch); if (parent.children == null) parent.children = new List<WorldLoader.BranchInfo>(); parent.children.Add(branch);
			var parameters = new WorldLoader.GenerationParameters { generationType = WorldLoader.GenerationParameters.GenerationType.replace, preloadLevels = Assets(child), generator = CL_GameManager.gamemode };
			yield return (IEnumerator)AccessTools.Method(typeof(WorldLoader), "GenerateLevels").Invoke(WorldLoader.instance, new object[] { branch, parameters });
		}
	}
	private static void Abort(string reason) {
		MPMain.LogError("[Host world] " + reason);
		LanMenu.Status = "Не удалось загрузить карту хоста. Подробности в журнале мода.";
		CL_GameManager.SetLoading(false);
		CL_GameManager.gMan.LoadScene("Main-Menu");
	}
	public static void Spawned(M_Level level) {
		if (current?.Plan == null || current.Spawned >= current.Plan.Rooms.Count) return;
		var room = current.Plan.Rooms[current.Spawned++];
		if (room.Id != level.levelName) return;
		rooms[level.GetInstanceID()] = room;
		if (room.Flip && level.canFlip) { level.transform.localScale = new Vector3(-1, 1, 1); level.SetFlipped(true); }
	}
	public static void Flip(M_Level level, ref bool value) {
		if ((!Enabled && !ManualSaves.Loading) || !level.canFlip || !rooms.TryGetValue(level.GetInstanceID(), out var room)) return;
		value = room.Flip;
		var scale = level.transform.localScale; scale.x = Mathf.Abs(scale.x) * (value ? -1 : 1); level.transform.localScale = scale;
	}
	public static void Seed(M_Level level, ref int seed) {
		if ((!Enabled && !ManualSaves.Loading) || !rooms.TryGetValue(level.GetInstanceID(), out var room)) return;
		seed = room.Seed;
		if (room.HasPose && (!MPSteamworks.IsHost || current?.FromSave == true)) {
			level.transform.SetPositionAndRotation(room.Position, room.Rotation); level.transform.localScale = room.Scale;
			var info = current?.Branch.levelTracker.FirstOrDefault(item => item.GetLevel() == level);
			if (info != null) {
				info.lowBounds = level.GetLevelEntrance().position.y - 1; info.highBounds = level.GetLevelExit().position.y + 1;
				if (info.spawnedLOD != null) { info.spawnedLOD.transform.SetPositionAndRotation(room.Position, room.Rotation); info.spawnedLOD.transform.localScale = room.Scale; }
			}
		}
	}
	private static void EnsureSession() {
		ulong id = MPSteamworks.Instance.IsLan ? MPSteamworks.Instance.HostSteamId : 0;
		if (session == id) return;
		session = id; ResetGeneration();
	}
	public static void ResetGeneration() { generationEpoch++; plans.Clear(); rooms.Clear(); CoopIdentity.Reset(); CoopFlowers.Reset(); if (!ManualSaves.Loading) savedPlan = null; waiting = 0; CoopMass.Reset(); CoopGrabAuthority.Reset(); CoopWorldActions.Reset(); CoopPlayerGrip.Reset(); if (Instance != null) { Instance.lastMechanisms = null; Instance.pendingStates.Clear(); } }
	private static void SendPlan(ulong peer, Plan plan) {
		string packed = ManualSaveStore.Pack(SerializePlan(plan));
		if (packed.Length > 800000) throw new InvalidOperationException("Слишком большая карта для передачи.");
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, peer, PacketType.WorldLayout);
		writer.Put((byte)1).Put(packed);
		if (peer == 0) MPSteamworks.Instance.Broadcast(writer); else MPSteamworks.Instance.SendToPeer(peer, writer);
	}
	private static Plan Existing(string key, string branchId) {
		if (WorldLoader.instance == null || !WorldLoader.isLoaded) return null;
		var branch = branchId == "branch-root" ? WorldLoader.instance.currentBranch : Branches(WorldLoader.instance)?.FirstOrDefault(item => item.id == branchId);
		if (branch?.GetLevelIds() == null) return null;
		var plan = new Plan { Key = key, Branch = branch.id, Seed = WorldLoader.instance.seed, Direct = true, Type = (int)WorldLoader.GenerationParameters.GenerationType.replace };
		foreach (var id in branch.GetLevelIds()) {
			var asset = CL_AssetManager.GetLevelAsset(id.level);
			if (asset == null) return null;
			var level = branch.levelTracker.FirstOrDefault(item => LevelId(item)?.id == id.id)?.GetLevel();
			float heightOffset = ENT_Player.GetPlayer() != null ? CL_GameManager.gMan.GetPlayerCorrectedHeight() - ENT_Player.GetPlayer().transform.position.y : 0;
			plan.Rooms.Add(new Room { Id = id.level, Region = id.region, Subregion = id.subRegion, Pause = level != null ? level.IsPausingGeneration() : asset.isPausingGeneration, Flip = level != null && level.transform.localScale.x < 0, Seed = level != null ? level.GetLevelSeed() : plan.Seed + plan.Rooms.Count * 999, HasPose = level != null, Position = level != null ? level.transform.position + Vector3.up * heightOffset : Vector3.zero, Rotation = level != null ? level.transform.rotation : Quaternion.identity, Scale = level != null ? level.transform.localScale : Vector3.one });
		}
		return plan.Rooms.Count > 0 ? plan : null;
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (!MPSteamworks.Instance.IsLan || !MPSteamworks.Instance.HasConnection(sender)) return;
		byte action = reader.GetByte();
		if (action == 3) {
			if (reader.GetULong() != ManualSaves.LoadGeneration) return;
			ulong triggerId = reader.GetULong();
			foreach (var generator in FindObjectsOfType<UT_BranchGenerator>(true)) if (CoopIdentity.For(generator.transform) == triggerId) { CoopBranches.Generate(generator, true); break; }
			return;
		}
		if (!Enabled) return;
		EnsureSession();
		if (action == 0 && MPSteamworks.IsHost) {
			string key = reader.GetString(); string branch = reader.GetString();
			if (key.Length > 512 || branch.Length > 256) return;
			Plan plan = key.StartsWith(branch + ":0:", StringComparison.Ordinal) ? Existing(key, branch) : null;
			if (plan == null) plans.TryGetValue(key, out plan);
			if (plan != null) SendPlan(sender, plan);
			else ContinueBranch(key, branch);
			if (Instance != null) Instance.lastMechanisms = null;
		} else if (action == 1 && !MPSteamworks.IsHost && sender == MPSteamworks.Instance.HostSteamId) {
			var plan = JsonConvert.DeserializeObject<Plan>(ManualSaveStore.Unpack(reader.GetString()), layoutJson);
			if (plan?.Key == null || plan.Key.Length > 512 || plan.Rooms == null || plan.Rooms.Count == 0 || plan.Rooms.Count > 2048 || plan.Type < 0 || plan.Type > 3 || plan.Rooms.Any(room => room == null || string.IsNullOrEmpty(room.Id))) return;
			if (plans.Count >= 64 && !plans.ContainsKey(plan.Key)) plans.Clear();
			plans[plan.Key] = plan;
		} else if (action == 2 && !MPSteamworks.IsHost && sender == MPSteamworks.Instance.HostSteamId && !ManualSaves.Loading && !ManualSaves.GroupLoading) {
			if (reader.GetULong() != ManualSaves.LoadGeneration) return;
			string state = reader.GetString();
			MechanismSnapshots.Restore(ManualSaveStore.Unpack(state));
		} else if (action == 4) {
			if (reader.GetULong() != ManualSaves.LoadGeneration || ManualSaves.Loading || ManualSaves.GroupLoading) return;
			string level = reader.GetString(); string state = reader.GetString();
			if (level.Length > 256 || state.Length > 256) return;
			Instance?.ApplyState(level, state);
		}
	}
	private static void ContinueBranch(string key, string branchId) {
		if (!WorldLoader.isLoaded || WorldLoader.instance == null) return;
		var branch = AllBranches().FirstOrDefault(item => item.id == branchId);
		if (branch?.levelTracker == null || branch.levelTracker.Count == 0 || key != branchId + ":" + (branch.GetLevelIds()?.Count ?? 0) + ":") return;
		var last = branch.levelTracker[branch.levelTracker.Count - 1];
		if (last.GetLevel() == null || last.stopLevel && (Paused(branch)?.Count ?? 0) == 0 || last.GetLevel().IsLastLevel()) return;
		var parameters = new WorldLoader.GenerationParameters { generator = branch.generationBranch != null ? branch.generationBranch : CL_GameManager.gamemode, lastRegion = last.GetLevel().region, seedOffset = 0 };
		var routine = (IEnumerator)AccessTools.Method(typeof(WorldLoader), "GenerateLevels").Invoke(WorldLoader.instance, new object[] { branch, parameters });
		WorldLoader.instance.StartCoroutine(routine);
	}
	public static WorldLayoutSync Instance { get; private set; }
	private void Awake() { Instance = this; }
	private void OnDestroy() { if (Instance == this) Instance = null; }
	public static void StatePlayed(M_Level level, string state) {
		if (!Enabled || ApplyingEvent || RemoteWorldEffects.Active || PersonalHandholds.Active || CoopWorldActions.DamageInProgress || WorldButtons.InteractionDepth > 0 || ManualSaves.Loading || !MPCore.CanSync) return;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.WorldLayout);
		writer.Put((byte)4).Put(ManualSaves.LoadGeneration).Put(CoopIdentity.For(level.transform).ToString()).Put(state); MPSteamworks.Instance.Broadcast(writer);
	}
	private void ApplyState(string name, string state) {
		if (!WorldLoader.isLoaded || ManualSaves.Loading) { if (pendingStates.Count < 128) pendingStates[name + "\n" + state] = Time.unscaledTime + 60; return; }
		var level = FindObjectsOfType<M_Level>(true).FirstOrDefault(item => item.gameObject.activeInHierarchy && CoopIdentity.For(item.transform).ToString() == name);
		if (level == null) { if (pendingStates.Count < 128) pendingStates[name + "\n" + state] = Time.unscaledTime + 60; return; }
		ApplyingEvent = true;
		using var scope = new RemoteWorldEffects();
		try { if (level.loadStates?.Any(item => item.name == state && item.state) != true) level.SetLoadState(state); } finally { ApplyingEvent = false; }
	}
	private void Update() {
		EnsureSession();
		if (packedSnapshot?.IsCompleted == true) {
			var task = packedSnapshot; packedSnapshot = null;
			try { string packed = task.GetAwaiter().GetResult(); if (packingEpoch == generationEpoch && Enabled && MPSteamworks.IsHost && MPCore.CanSync && !ManualSaves.Loading && !ManualSaves.GroupLoading) { lastMechanisms = packingMechanisms; var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.WorldLayout); writer.Put((byte)2).Put(ManualSaves.LoadGeneration).Put(packed); MPSteamworks.Instance.Broadcast(writer); } } catch (Exception ex) { MPMain.LogWarning("[Host world] Snapshot compression: " + ex.Message); }
		}
		if (!Enabled || !MPCore.CanSync || !WorldLoader.isLoaded || ManualSaves.Loading || ManualSaves.GroupLoading) return;
		if (Time.unscaledTime >= nextSnapshot) {
			nextSnapshot = Time.unscaledTime + 1f;
			if (MPSteamworks.IsHost && packedSnapshot == null) {
				string mechanisms = MechanismSnapshots.Capture();
				if (mechanisms != lastMechanisms) { packingMechanisms = mechanisms; packingEpoch = generationEpoch; packedSnapshot = Task.Run(() => ManualSaveStore.Pack(mechanisms)); }
			}
			foreach (var pair in pendingStates.ToArray()) { pendingStates.Remove(pair.Key); if (pair.Value < Time.unscaledTime) continue; var parts = pair.Key.Split('\n'); ApplyState(parts[0], parts[1]); }
		}
	}
	public static IEnumerable<WorldLoader.BranchInfo> AllBranches() => WorldLoader.instance != null ? Branches(WorldLoader.instance) ?? Enumerable.Empty<WorldLoader.BranchInfo>() : Enumerable.Empty<WorldLoader.BranchInfo>();
}

[HarmonyPatch(typeof(M_Level), nameof(M_Level.OnSpawn))]
public static class Patch_WorldLayoutSpawn { private static void Postfix(M_Level __instance) => WorldLayoutSync.Spawned(__instance); }
[HarmonyPatch(typeof(M_Level), nameof(M_Level.SetFlipped))]
public static class Patch_WorldLayoutFlip { private static void Prefix(M_Level __instance, ref bool b) => WorldLayoutSync.Flip(__instance, ref b); }
[HarmonyPatch(typeof(M_Level), nameof(M_Level.Initialize))]
public static class Patch_WorldLayoutSeed { private static void Prefix(M_Level __instance, ref int seed) => WorldLayoutSync.Seed(__instance, ref seed); }
[HarmonyPatch(typeof(M_Level), nameof(M_Level.PlayLoadState))]
public static class Patch_WorldLayoutState { private static void Postfix(M_Level __instance, string s) => WorldLayoutSync.StatePlayed(__instance, s); }
[HarmonyPatch(typeof(M_Level), nameof(M_Level.SetLoadState))]
public static class Patch_WorldLayoutPersistentState { private static void Postfix(M_Level __instance, string s) => WorldLayoutSync.StatePlayed(__instance, s); }
[HarmonyPatch]
public static class Patch_WorldStateTeleport {
	private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => typeof(ENT_Player).GetMethods().Where(method => method.Name == "Teleport" && method.DeclaringType == typeof(ENT_Player));
	private static bool Prefix() => !WorldLayoutSync.ApplyingEvent && !RemoteWorldEffects.Active;
}



