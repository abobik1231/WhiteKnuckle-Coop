using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Util;
using WKMPMod.World;
using static Unity.Collections.AllocatorManager;
using static WorldLoader;

namespace WKMPMod.Patch;

[HarmonyPatch(typeof(WorldLoader))]
public class Patch_WorldLoader {
	[Serializable]
	public struct LevelTransformData {
		public string levelName;
		public Vector3 position;
		public Quaternion rotation;
		public override readonly string ToString() {
			return $"\"{levelName}\" : \"({position.x}, {position.y}, {position.z}) ({rotation.x}, {rotation.y}, {rotation.z}, {rotation.w})\"";
		}
	}
	private static readonly AccessTools.FieldRef<WorldLoader, bool> LoadingField =
		AccessTools.FieldRefAccess<WorldLoader, bool>("loading");
	private sealed class SavedLevelGenerator : WorldGenerator {
		private readonly WorldGenerator original;
		private List<M_Level.LevelAssetHolder> first;
		private readonly UnityEngine.Random.State state;
		public SavedLevelGenerator(WorldGenerator original, List<M_Level.LevelAssetHolder> first, UnityEngine.Random.State state) { this.original = original; this.first = first; this.state = state; }
		public List<M_Level.LevelAssetHolder> GetGenerationList(M_Region lastRegion = null, M_Region regionToGenerate = null, M_Subregion startAfterSubregion = null) {
			if (first != null && lastRegion == null && regionToGenerate == null && startAfterSubregion == null) { var result = first; first = null; UnityEngine.Random.state = state; return result; }
			return original.GetGenerationList(lastRegion, regionToGenerate, startAfterSubregion);
		}
	}
	private static string LevelFamily(string name) {
		if (string.IsNullOrEmpty(name)) return "";
		int separator = name.LastIndexOf('_');
		if (separator < 0 || separator == name.Length - 1) return name;
		for (int i = separator + 1; i < name.Length; i++) if (!char.IsDigit(name[i])) return name;
		return name.Substring(0, separator);
	}
	private static List<M_Level.LevelAssetHolder> LegacyGeneration(M_Gamemode mode, M_Region region) {
		if (region == null || mode.regions.Contains(region)) return mode.GetGenerationList(null, region, null);
		var canonical = mode.GetGenerationList();
		var parent = mode.regions.Where(item => region.name.StartsWith(item.name + "_", StringComparison.Ordinal)).OrderByDescending(item => item.name.Length).FirstOrDefault();
		if (parent == null) return mode.GetGenerationList(null, region, null);
		for (int i = 0; i < canonical.Count; i++) {
			if (canonical[i].region != parent) continue;
			var level = canonical[i].GetLevel(); if (level == null) continue;
			var template = level.GetComponentsInChildren<UT_BranchGenerator>(true).SelectMany(trigger => trigger.branch ?? new List<M_GenerationBranch>()).FirstOrDefault(asset => asset != null && asset.regions != null && asset.regions.Contains(region) && asset.branchEnd == M_GenerationBranch.BranchEnd.continueRegion);
			if (template == null) continue;
			var branch = UnityEngine.Object.Instantiate(template); branch.branchEnd = M_GenerationBranch.BranchEnd.end;
			try {
				var result = branch.GetGenerationList(parent, null, null);
				if (result.Count > 0 && !result[result.Count - 1].pauseGeneration) result[result.Count - 1].isPausingGeneration = false;
				result.AddRange(canonical.Skip(i + 1));
				MPMain.LogInfo("[Saves] Rebuilt legacy branch " + template.name + " with continuation after " + canonical[i].id);
				return result;
			} finally { UnityEngine.Object.Destroy(branch); }
		}
		return mode.GetGenerationList(null, region, null);
	}

	// 记录每个关卡的世界坐标和旋转数据, 用于后续矫正坐标
	public static List<LevelTransformData> levelWorldTransformDatas = new List<LevelTransformData>();

	// 补丁类: 在联机模式下默认是固定种子,不上传成绩
	// 初始化生物同步
	[HarmonyPatch(nameof(WorldLoader.Initialize))]
	[HarmonyPostfix]
	public static void Initialize_UseCustomSeed() {
		if (MPCore.IsInLobby) customSeed = true;
	}
	[HarmonyPatch(nameof(WorldLoader.Initialize))]
	[HarmonyPrefix]
	private static void ResetWorldLayout() { WKMPMod.Gameplay.WorldLayoutSync.ResetGeneration(); WKMPMod.Gameplay.CoopCoordinates.Reset(); }

	// 补丁类: 关闭种子偏移, 使复活时种子同步
	[HarmonyPatch(("IncrementSeed"))]
	[HarmonyPrefix]
	public static bool IncrementSeed_Off() {
		if (MPCore.IsInLobby)
			return false;
		return true;
	}

	// 补丁类: 关闭生成器的种子偏移, 使复活时种子同步
	[HarmonyPatch(("GenerateLevels"))]
	[HarmonyPrefix]
	public static void GenerateLevels_GenParams_Off(BranchInfo branch, ref GenerationParameters genParams, WorldLoader __instance, WorldGenerator ___currentGenerator) {
		genParams ??= new GenerationParameters();
		if (MPCore.IsInLobby) genParams.seedOffset = 0;
		if (WKMPMod.UI.ManualSaves.Loading && !WKMPMod.Gameplay.WorldLayoutSync.HasSavedPlan && !(WKMPMod.Gameplay.WorldLayoutSync.Enabled && !WKMPMod.NetWork.MPSteamworks.IsHost) && genParams?.generationType == GenerationParameters.GenerationType.generateUntilFound && genParams.preloadLevels != null && genParams.preloadLevels.Count > 0) {
			var required = new List<M_Level.LevelAssetHolder>(genParams.preloadLevels);
			var generator = (WorldGenerator)CL_GameManager.gamemode;
			try {
				var previousRandom = UnityEngine.Random.state;
				List<M_Level.LevelAssetHolder> generated;
				UnityEngine.Random.State generatedRandom;
				try { UnityEngine.Random.InitState(__instance.seed + genParams.seedOffset); generated = LegacyGeneration(CL_GameManager.gamemode, genParams.startRegion); generatedRandom = UnityEngine.Random.state; }
				finally { UnityEngine.Random.state = previousRandom; }
				int index = generated?.FindIndex(item => item != null && item.id == required[0].id) ?? -1;
				if (index < 0 && generated != null) index = generated.FindIndex(item => item != null && LevelFamily(item.id) == LevelFamily(required[0].id));
				if (index >= 0) {
					var previous = generated[index];
					required[0].region = previous.region; required[0].subregion = previous.subregion;
					generated[index] = required[0];
					genParams.generator = new SavedLevelGenerator(generator, generated, generatedRandom);
					genParams.forceResetGeneratorToGamemode = false;
					MPMain.LogInfo("[Saves] Restoring saved level with surrounding world at index " + index);
				} else {
					genParams.generationType = GenerationParameters.GenerationType.replace;
					MPMain.LogWarning("[Saves] No matching surrounding level found; restoring saved rooms only");
				}
			} catch (Exception ex) { genParams.generationType = GenerationParameters.GenerationType.replace; MPMain.LogWarning("[Saves] Could not rebuild surrounding level list: " + ex.Message); }
		}
		if (MPCore.IsInLobby && CL_GameManager.GetBaseGamemode().gamemodeName == "Campaign") {
			// 禁用种子偏移
			if (genParams != null) genParams.seedOffset = 0;
			// 启动协程等待生成结束并收集数据
			bool isGenerationBranch = genParams?.generator is M_GenerationBranch;
			if (!WKMPMod.NetWork.MPSteamworks.Instance.IsLan) __instance.StartCoroutine(WaitAndCollectData(__instance, branch, isGenerationBranch));
		}
	}
	[HarmonyPatch("GenerateLevels")]
	[HarmonyPostfix]
	private static void SynchronizeLayout(ref IEnumerator __result, BranchInfo branch, GenerationParameters genParams, WorldGenerator ___currentGenerator) {
		if (!WKMPMod.Gameplay.WorldLayoutSync.Enabled && !WKMPMod.Gameplay.WorldLayoutSync.HasSavedPlan && !WKMPMod.UI.ManualSaves.Loading) return;
		__result = WKMPMod.Gameplay.WorldLayoutSync.Wrap(__result, branch, genParams, genParams.generator ?? ___currentGenerator ?? CL_GameManager.gamemode);
	}

	#region[支线坐标同步协程]
	public static IEnumerator WaitAndCollectData(WorldLoader loader, BranchInfo targetBranch, bool isGenerationBranch) {
		// 等待生成流程开始 (loading 变为 true) 
		yield return new WaitUntil(() => LoadingField(loader) == true);
		// 等待生成流程彻底结束 (isLoaded 变为 true) 
		yield return new WaitUntil(() => WorldLoader.isLoaded == true);

		if (targetBranch == null) {
			MPMain.LogError(Localization.Get("Patch.TargetBranchIsNull"));
			yield break;
		}
		if (isGenerationBranch) {
			yield return null;

			MPMain.LogInfo(Localization.Get("Patch.StartCorrectingSideRouteCoords"));

			// 获取核心组件
			var worldRoot = GameObject.Find("World_Root(Clone)");
			var player = ENT_Player.GetPlayer();
			if (worldRoot == null || player == null)
				yield break; 

			// 匹配第一个相同的关卡
			// 提前构建 Dictionary 提升匹配速度 (O(n))
			var targetDataDict = levelWorldTransformDatas.ToDictionary(d => d.levelName, d => d);

			var match = targetBranch.levelTracker
				.Select(info => new { Info = info, Level = info.GetLevel() })
				.FirstOrDefault(x => x.Level != null && targetDataDict.ContainsKey(x.Level.gameObject.name));

			if (match == null) {
				MPMain.LogError(Localization.Get("Patch.NoMatchingLevelDataForCorrection"));
				yield break;
			}

			// 提取变换引用
			var targetData = targetDataDict[match.Level.gameObject.name];
			Transform childTf = match.Level.transform;
			Transform rootTf = worldRoot.transform;
			Transform playerTf = player.transform;

			// 计算目标位姿
			// 旋转目标: Target = Root * Local -> Root = Target * inv(Local)
			Quaternion finalRootRot = targetData.rotation * Quaternion.Inverse(childTf.localRotation);
			// 位置目标: Target = RootPos + (RootRot * LocalPos) -> RootPos = Target - (RootRot * LocalPos)
			Vector3 finalRootPos = targetData.position - (finalRootRot * childTf.localPosition);

			// 记录偏移并处理分数补偿
			Vector3 posDelta = finalRootPos - rootTf.position;
			// 累加偏移, 防止多次矫正导致的跳变
			Patch_CL_GameManager.HeightOffset += posDelta.y;

			// 应用变换
			Transform originalPlayerParent = playerTf.parent;

			player.Lock();
			playerTf.SetParent(rootTf, true);

			// 执行关卡位置修正和同步
			rootTf.SetPositionAndRotation(finalRootPos, finalRootRot);

			Physics.SyncTransforms();

			yield return new WaitForFixedUpdate();

			// 刷新关卡碰撞
			foreach (var info in targetBranch.levelTracker) {
				var lv = info.GetLevel();
				lv.CycleColliders();
			}

			Physics.SyncTransforms();
			yield return new WaitForFixedUpdate();

			playerTf.SetParent(originalPlayerParent, true);
			player.UnLock();

			// 最终兜底同步
			Physics.SyncTransforms();

			// 给低帧率留出至少一个固定的物理帧安全垫, 阻断游戏引擎的动态剔除误判
			yield return new WaitForFixedUpdate();

			MPMain.LogInfo(Localization.Get("Patch.CorrectionCompleted", targetData.levelName, posDelta.ToString()));

		} else {
			levelWorldTransformDatas = targetBranch.levelTracker
				.Where(info => info.GetLevel() != null)
				.Select(info => {
					var go = info.GetLevel().gameObject;
					return new LevelTransformData {
						levelName = go.name,
						position = go.transform.position,
						rotation = go.transform.rotation
					};
				}).ToList();

			MPMain.LogInfo(Localization.Get("Patch.LevelDataRetrieved", levelWorldTransformDatas.Count));
		}
	}

	#endregion
}
