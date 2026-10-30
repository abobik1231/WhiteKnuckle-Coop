using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using WKMPMod.Component;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.UI;

namespace WKMPMod.Gameplay;

public static class CoopWorldActions {
    public sealed class ActionState { public string token; public ulong id; public string type; public float amount; public string damage; public string kind; public ulong generation; }
    public sealed class Hit { public ActionState State; public M_Level Level; public bool Outer; public UnityEngine.Component Source; public float Health; }
    private static readonly Dictionary<string, (ActionState State, M_Level Level)> history = new Dictionary<string, (ActionState, M_Level)>();
    private static readonly HashSet<ulong> flowers = new HashSet<ulong>();
    private static readonly Dictionary<string, (ActionState Action, float Until)> pending = new Dictionary<string, (ActionState, float)>();
    private static bool applying;
    private static int depth;
    public static bool DamageInProgress => depth > 0;
    public static bool Enabled => MPSteamworks.Instance.IsLan && MPCore.IsInLobby && !ManualSaves.Loading && !ManualSaves.GroupLoading;
    public static void Reset() { history.Clear(); flowers.Clear(); pending.Clear(); }
    private static bool Eligible(UnityEngine.Component item) => item != null && !(item is AIGameEntity) && !(item is ENT_Player) && !(item is RemoteEntity) && item.GetComponent<Item_Object>() == null && item.GetComponentInParent<AIGameEntity>() == null && item.GetComponentInParent<M_Level>(true) != null;
    public static Hit Before(UnityEngine.Component item, Damageable.DamageInfo info) {
        if (!Enabled || applying || RemoteWorldEffects.Active || PersonalHandholds.Active || PersonalHandholds.Contains(item) || !Eligible(item) || info == null || !float.IsFinite(info.amount) || info.amount <= 0) return null;
        return new Hit { Outer = depth++ == 0, Source = item, Health = item is UT_Damage damage ? damage.health : item is GameEntity entity ? entity.health : float.NaN, Level = item.GetComponentInParent<M_Level>(true), State = new ActionState { token = Guid.NewGuid().ToString("N"), id = CoopIdentity.For(item.transform), type = item.GetType().FullName, kind = "damage", amount = info.amount, damage = info.type } };
    }
    public static void After(Hit hit, bool accepted) {
        if (hit?.Outer != true) return;
        bool changed = hit.Source == null || hit.Source is GameEntity entity && entity.health != hit.Health || hit.Source is UT_Damage damage && (damage.health != hit.Health || damage.maxHealth <= 0 && damage.canDamage && hit.State.amount >= damage.minimumDamageThreshold);
        if (accepted || changed) Publish(hit.State, hit.Level);
    }
    public static void Finish(Hit hit) { if (hit != null) depth = Math.Max(0, depth - 1); }
    private static void Publish(ActionState action, M_Level level) {
        if (!Enabled || applying || history.ContainsKey(action.token)) return;
        action.generation = ManualSaves.LoadGeneration;
        history[action.token] = (action, level);
        var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.WorldAction);
        writer.Put(JsonConvert.SerializeObject(action)); MPSteamworks.Instance.Broadcast(writer);
    }
    public static bool Flower(ENV_SignalOrchid_VineFlower flower) {
        if (!Enabled && !applying) return true;
        ulong id = CoopIdentity.For(flower.transform);
        if (!flowers.Add(id)) return false;
        CoopFlowers.Start(flower);
        if (!applying && depth == 0) Publish(new ActionState { token = Guid.NewGuid().ToString("N"), id = id, type = typeof(ENV_SignalOrchid_VineFlower).FullName, kind = "flower" }, flower.GetComponentInParent<M_Level>(true));
        return true;
    }
    public static string Capture(HashSet<M_Level> levels) => JsonConvert.SerializeObject(history.Values.Where(entry => entry.Level != null && (levels == null || levels.Contains(entry.Level))).Select(entry => entry.State));
    public static void Restore(string json) { if (string.IsNullOrEmpty(json)) return; var actions = JsonConvert.DeserializeObject<List<ActionState>>(json); if (actions == null || actions.Count > 8192) return; foreach (var action in actions) Apply(action); }
    public static void Receive(ulong sender, DataReader reader) {
        if (!Enabled || !MPSteamworks.Instance.HasConnection(sender)) return;
        string json = reader.GetString(); if (json.Length > 2048) return;
        var action = JsonConvert.DeserializeObject<ActionState>(json); if (action?.generation != ManualSaves.LoadGeneration) return; Apply(action);
    }
    public static void Flush() { if (!Enabled || !WorldLoader.isLoaded) return; int count = 0; foreach (var pair in pending.ToArray()) { pending.Remove(pair.Key); if (pair.Value.Until > Time.unscaledTime) Apply(pair.Value.Action, pair.Value.Until); if (++count >= 8) break; } }
    private static void Apply(ActionState action, float until = 0) {
        if (action == null || !Guid.TryParseExact(action.token, "N", out _) || history.ContainsKey(action.token) || action.type?.Length > 256 || !float.IsFinite(action.amount) || action.amount < 0 || action.amount > 100000) return;
        var type = typeof(M_Level).Assembly.GetType(action.type ?? ""); if (type == null || !typeof(UnityEngine.Component).IsAssignableFrom(type)) return;
        var item = CoopIdentity.Find(action.id, type);
        if (item == null && until == 0) item = UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None).OfType<UnityEngine.Component>().FirstOrDefault(component => CoopIdentity.For(component.transform) == action.id);
        if (item == null) { if (pending.Count < 256) pending[action.token] = (action, until > 0 ? until : Time.unscaledTime + 120); return; }
        if (!Eligible(item)) return;
        if (PersonalHandholds.Contains(item)) return;
        var level = item.GetComponentInParent<M_Level>(true);
        applying = true;
        using var scope = new RemoteWorldEffects();
        try {
            if (action.kind == "flower" && item is ENV_SignalOrchid_VineFlower flower) flower.GrowAlongSpline();
            else if (action.kind == "damage" && item is Damageable damageable) damageable.Damage(Damageable.DamageInfo.CreateDamageInfo(action.amount, action.damage));
            history[action.token] = (action, level);
            pending.Remove(action.token);
        } finally { applying = false; }
    }
}

[HarmonyPatch]
public static class Patch_WorldDamage {
    private static IEnumerable<MethodBase> TargetMethods() => typeof(M_Level).Assembly.GetTypes().Where(type => type == typeof(UT_Damage) || typeof(GameEntity).IsAssignableFrom(type) && !typeof(AIGameEntity).IsAssignableFrom(type)).Select(type => type.GetMethod("Damage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, new[] { typeof(Damageable.DamageInfo) }, null)).Where(method => method != null && !method.IsAbstract && method.ReturnType == typeof(bool));
    private static void Prefix(UnityEngine.Component __instance, Damageable.DamageInfo __0, out CoopWorldActions.Hit __state) => __state = CoopWorldActions.Before(__instance, __0);
    private static void Postfix(bool __result, CoopWorldActions.Hit __state) => CoopWorldActions.After(__state, __result);
    private static Exception Finalizer(Exception __exception, CoopWorldActions.Hit __state) { CoopWorldActions.Finish(__state); return __exception; }
}
[HarmonyPatch(typeof(ENV_SignalOrchid_VineFlower), nameof(ENV_SignalOrchid_VineFlower.GrowAlongSpline))]
public static class Patch_WorldFlower { private static bool Prefix(ENV_SignalOrchid_VineFlower __instance) => CoopWorldActions.Flower(__instance); }

