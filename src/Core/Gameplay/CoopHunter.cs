using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WKMPMod.Component;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.RemotePlayer;
using WKMPMod.World;

namespace WKMPMod.Gameplay;

public static class CoopEnemyCombat { public static int Depth; public static bool Active => Depth > 0; }

public sealed class CoopHunter : MonoBehaviour {
    private static readonly AccessTools.FieldRef<DEN_Hunter, Queue<Vector3>> History = AccessTools.FieldRefAccess<DEN_Hunter, Queue<Vector3>>("playerTrackPositions");
    private static readonly AccessTools.FieldRef<DEN_Hunter, Vector3> Last = AccessTools.FieldRefAccess<DEN_Hunter, Vector3>("lastPlayerTrackPosition");
    private static readonly AccessTools.FieldRef<DEN_Hunter, bool> Visible = AccessTools.FieldRefAccess<DEN_Hunter, bool>("targetVisible");
    private static readonly AccessTools.FieldRef<DEN_Hunter, bool> Active = AccessTools.FieldRefAccess<DEN_Hunter, bool>("active");
    public static DEN_Hunter Moving;
    private readonly Dictionary<ulong, float> hitAt = new Dictionary<ulong, float>();
    private DEN_Hunter hunter;
    private UT_SegmentTrail trail;
    private bool replica;
    private Vector3[] pose;
    private Vector3[] sizes;
    private Quaternion[] rotations;
    private void Awake() { hunter = GetComponent<DEN_Hunter>(); trail = GetComponentInChildren<UT_SegmentTrail>(true); }
    public static bool Track(DEN_Hunter enemy) {
        if (!MPSteamworks.Instance.IsLan || !MPSteamworks.IsHost || !EnemySyncModule.Instance.Synced(enemy)) return true;
        if (enemy.target == null) return true;
        var history = History(enemy); if (history == null) History(enemy) = history = new Queue<Vector3>();
        bool visible = enemy.sight.CanSeeTarget(enemy.target); Visible(enemy) = visible;
        Vector3 position = enemy.target.position;
        if (visible) history.Clear();
        if (visible || history.Count == 0 || Vector3.Distance(Last(enemy), position) > enemy.trackPositionDistance) { history.Enqueue(position); Last(enemy) = position; }
        return false;
    }
    private void LateUpdate() {
        if (replica && (!MPCore.IsInLobby || !EnemySyncModule.Instance.IsEnabled)) { if (trail != null) trail.enabled = true; replica = false; }
        if (replica) { Render(); return; }
        if (hunter == null || hunter.dead || !MPSteamworks.IsHost || !EnemySyncModule.Instance.Synced(hunter) || !Active(hunter)) return;
        var local = ENT_Player.GetPlayer();
        if (local != null && !local.IsDead() && (!hitAt.TryGetValue(MPSteamworks.UserSteamId, out float localUntil) || Time.unscaledTime >= localUntil)) {
            float amount = ContactDamage(local.transform.position);
            if (amount > 0) { local.Damage(Damageable.DamageInfo.CreateDamageInfo(amount, hunter, hunter.objectType)); hitAt[MPSteamworks.UserSteamId] = Time.unscaledTime + Mathf.Max(.1f, hunter.collisionDamageRate); }
        }
        foreach (var pair in RPManager.Instance.Players) {
            if (!CoopPresence.Alive(pair.Key) || pair.Value.PlayerObject == null || pair.Value.RemoteEntities == null || pair.Value.RemoteEntities.Length == 0 || hitAt.TryGetValue(pair.Key, out float until) && Time.unscaledTime < until) continue;
            Vector3 position = pair.Value.PlayerObject.transform.position;
            float amount = ContactDamage(position);
            if (amount <= 0) continue;
            var info = Damageable.DamageInfo.CreateDamageInfo(amount, hunter, hunter.objectType);
            pair.Value.RemoteEntities[0].Damage(info);
            hitAt[pair.Key] = Time.unscaledTime + Mathf.Max(0.1f, hunter.collisionDamageRate);
        }
    }
    private float ContactDamage(Vector3 position) {
        if (Vector3.Distance(position, hunter.transform.position) < 2) return 10000;
        if (trail != null) foreach (var segment in trail.GetSegments()) if (segment != null && Vector3.Distance(position, segment.position) < Mathf.Abs(segment.lossyScale.y) * hunter.segmentDamageRadius) return hunter.collisionDamage;
        return 0;
    }
    private void Render() {
        if (trail == null || pose == null || hunter == null) return;
        var segments = trail.GetSegments();
        for (int i = 0; i < pose.Length && i < segments.Count; i++) { var segment = segments[i]; segment.gameObject.SetActive(true); if (segment.parent != null) segment.parent.gameObject.SetActive(true); segment.SetPositionAndRotation(hunter.transform.TransformPoint(pose[i]), hunter.transform.rotation * rotations[i]); segment.localScale = sizes[i]; }
    }
    public static void Write(DataWriter writer, GameEntity entity) {
        var hunter = entity as DEN_Hunter;
        var trail = hunter != null ? hunter.GetComponent<CoopHunter>()?.trail ?? hunter.GetComponentInChildren<UT_SegmentTrail>(true) : null;
        writer.Put(trail != null);
        if (trail == null) return;
        var segments = trail.GetSegments(); int count = Mathf.Min(segments.Count, 96);
        bool compact = true;
        for (int i = 0; i < count; i++) { var point = hunter.transform.InverseTransformPoint(segments[i].position); var size = segments[i].localScale; if (!Finite(point) || !Finite(size) || Mathf.Max(Mathf.Abs(point.x), Mathf.Abs(point.y), Mathf.Abs(point.z)) > 320 || Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) > 32) { compact = false; break; } }
        writer.Put(Active(hunter)).Put((byte)count).Put(compact);
        var inverse = Quaternion.Inverse(hunter.transform.rotation);
        for (int i = 0; i < count; i++) { var segment = segments[i]; var point = hunter.transform.InverseTransformPoint(segment.position); var rotation = inverse * segment.rotation; if (compact) { Put(writer, point, 100); writer.Put((short)Mathf.RoundToInt(rotation.x * 32767)).Put((short)Mathf.RoundToInt(rotation.y * 32767)).Put((short)Mathf.RoundToInt(rotation.z * 32767)).Put((short)Mathf.RoundToInt(rotation.w * 32767)); Put(writer, segment.localScale, 1000); } else writer.Put(point).Put(rotation).Put(segment.localScale); }
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    private static void Put(DataWriter writer, Vector3 value, float scale) { writer.Put((short)Mathf.RoundToInt(value.x * scale)).Put((short)Mathf.RoundToInt(value.y * scale)).Put((short)Mathf.RoundToInt(value.z * scale)); }
    private static Vector3 Get(DataReader reader, float scale) => new Vector3(reader.GetShort() / scale, reader.GetShort() / scale, reader.GetShort() / scale);
    public static void Read(DataReader reader, NetworkedEnemy identity) {
        if (!reader.GetBool()) return;
        bool active = reader.GetBool(); int count = reader.GetByte(); if (count > 96) throw new InvalidOperationException("Invalid hunter pose"); bool compact = reader.GetBool();
        var hunter = identity != null ? identity.Entity as DEN_Hunter : null;
        var net = hunter != null ? hunter.GetComponent<CoopHunter>() ?? hunter.gameObject.AddComponent<CoopHunter>() : null;
        var trail = net?.trail; var segments = trail?.GetSegments();
        if (trail != null) { trail.enabled = false; net.replica = true; Active(hunter) = active; while (segments.Count < count) trail.Grow(); if (net.pose == null || net.pose.Length != count) { net.pose = new Vector3[count]; net.rotations = new Quaternion[count]; net.sizes = new Vector3[count]; } }
        for (int i = 0; i < count; i++) {
            Vector3 position = compact ? Get(reader, 100) : reader.GetVector3(); Quaternion rotation = compact ? Quaternion.Normalize(new Quaternion(reader.GetShort() / 32767f, reader.GetShort() / 32767f, reader.GetShort() / 32767f, reader.GetShort() / 32767f)) : reader.GetQuaternion(); Vector3 scale = compact ? Get(reader, 1000) : reader.GetVector3();
            if (segments == null || i >= segments.Count) continue;
            net.pose[i] = position; net.rotations[i] = rotation; net.sizes[i] = scale;
        }
        net?.Render();
    }
}
[HarmonyPatch(typeof(DEN_Hunter), "TrackPlayerPosition")]
public static class Patch_HunterTargetTrack { private static bool Prefix(DEN_Hunter __instance) => CoopHunter.Track(__instance); }
[HarmonyPatch(typeof(DEN_Hunter), "Movement")]
public static class Patch_HunterMovementScope {
    private static void Prefix(DEN_Hunter __instance, out DEN_Hunter __state) { __state = CoopHunter.Moving; CoopHunter.Moving = __instance; }
    private static Exception Finalizer(DEN_Hunter __state, Exception __exception) { CoopHunter.Moving = __state; return __exception; }
}
[HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.Kill))]
public static class Patch_HunterWrongVictim {
    private static bool Prefix(ENT_Player __instance) => CoopHunter.Moving == null || !MPSteamworks.Instance.IsLan || !MPSteamworks.IsHost || Vector3.Distance(__instance.transform.position, CoopHunter.Moving.transform.position) < 2.2f;
}
