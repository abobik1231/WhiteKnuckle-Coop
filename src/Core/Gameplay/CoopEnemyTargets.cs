using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.NetWork;
using WKMPMod.RemotePlayer;
using WKMPMod.World;

namespace WKMPMod.Gameplay;

public sealed class CoopEnemyTargets : MonoBehaviour {
	private AIGameEntity enemy;
	private float nextChoice;
	private ulong identity;
	public static List<(ulong Id, Transform Target, float Distance)> Candidates(Vector3 position, float radius) {
		var result = new List<(ulong, Transform, float)>();
		var local = ENT_Player.GetPlayer();
		if (local != null && !local.IsDead() && Vector3.Distance(local.transform.position, position) <= radius) result.Add((MPSteamworks.UserSteamId, local.transform, Vector3.Distance(local.transform.position, position)));
		foreach (var pair in RPManager.Instance.Players) {
			if (!CoopPresence.Alive(pair.Key) || pair.Value.PlayerObject == null || !pair.Value.PlayerObject.activeInHierarchy || pair.Value.RemoteEntities == null || pair.Value.RemoteEntities.Length == 0) continue;
			float distance = Vector3.Distance(pair.Value.PlayerObject.transform.position, position);
			if (distance <= radius) result.Add((pair.Key, pair.Value.RemoteEntities[0].transform, distance));
		}
		return result.OrderBy(item => item.Item1).ToList();
	}
	public static ulong Choose(Vector3 position, float radius, ulong key) {
		var candidates = Candidates(position, radius);
		return Choose(candidates, key);
	}
	private static ulong Choose(List<(ulong Id, Transform Target, float Distance)> candidates, ulong key) {
		if (candidates.Count == 0) return MPSteamworks.Instance.HostSteamId;
		float nearest = candidates.Min(item => item.Distance);
		candidates.RemoveAll(item => item.Distance > nearest + 1.25f);
		double sum = candidates.Sum(item => 1.0 / (1 + item.Distance * item.Distance));
		var random = new System.Random(unchecked((int)(key ^ key >> 32) + (int)(Time.unscaledTime / 3) * 397));
		double value = random.NextDouble() * sum;
		foreach (var item in candidates) { value -= 1.0 / (1 + item.Distance * item.Distance); if (value <= 0) return item.Id; }
		return candidates[0].Id;
	}
	public static Transform TargetFor(AIGameEntity entity, float radius = 24) {
		return TargetFor(entity, CoopIdentity.For(entity.transform), radius);
	}
	private static Transform TargetFor(AIGameEntity entity, ulong key, float radius = 24) {
		var candidates = Candidates(entity.transform.position, radius);
		ulong selected = Choose(candidates, key);
		return candidates.FirstOrDefault(item => item.Id == selected).Target;
	}
	private void Update() {
		if (!MPSteamworks.Instance.IsLan || !MPSteamworks.IsHost || !MPCore.CanSync || Time.unscaledTime < nextChoice) return;
		if (enemy == null) { enemy = GetComponent<AIGameEntity>(); nextChoice = Time.unscaledTime + (Mathf.Abs(GetInstanceID() % 100) / 100f) * 2f; return; }
		if (enemy == null || enemy.dead || !EnemySyncModule.Instance.Synced(enemy)) return;
		nextChoice = Time.unscaledTime + 3;
		if (enemy.target != null && enemy.target.GetComponentInParent<GameEntity>() is GameEntity current && !(current is ENT_Player) && !(current is WKMPMod.Component.RemoteEntity)) return;
		if (identity == 0) identity = CoopIdentity.For(enemy.transform);
		var target = TargetFor(enemy, identity, enemy is DEN_Hunter ? 10000f : 50f);
		if (target != null) enemy.SetTarget(target);
	}
}

