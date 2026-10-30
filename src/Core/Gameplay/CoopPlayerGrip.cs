using System.Collections.Generic;
using UnityEngine;
using WKMPMod.Data;
using WKMPMod.NetWork;

namespace WKMPMod.Gameplay;

public static class CoopPlayerGrip {
    private sealed class Grip { public bool Drag; public bool Hang; public Vector3 Point; public Vector3 Velocity; public float Time; }
    private static readonly Dictionary<ulong, Grip> grips = new Dictionary<ulong, Grip>();
    public static void Reset() => grips.Clear();
    public static void Observe(ref PlayerData data) {
        ulong local = MPSteamworks.UserSteamId;
        bool left = data.LeftHand.IsBeGrabbing(local), right = data.RightHand.IsBeGrabbing(local);
        grips[data.playId] = new Grip { Drag = left || right, Hang = data.LeftHand.IsBeHanging(local) || data.RightHand.IsBeHanging(local), Point = left && right ? (data.LeftHand.DesiredPosition + data.RightHand.DesiredPosition) * 0.5f : left ? data.LeftHand.DesiredPosition : data.RightHand.DesiredPosition, Velocity = data.HasPhysics ? data.WorldVelocity : Vector3.zero, Time = UnityEngine.Time.unscaledTime };
    }
    private static bool Holding(ENT_Player player, ulong peer) {
        foreach (var hand in player.hands) {
            if (hand.interactState == ENT_Player.InteractType.grab && hand.grabTarget is WKMPMod.Component.RemoteEntity entity && entity.playerId == peer) return true;
            var reference = hand.handhold != null ? hand.handhold.GetComponentInParent<WKMPMod.Component.RPContainerRef>() : null;
            if (hand.interactState == ENT_Player.InteractType.hanging && reference != null && reference.container.PlayerId == peer) return true;
        }
        return false;
    }
    public static bool NativeHang(ENT_Player.Hand hand) {
        if (hand.handhold == null || hand.handhold.GetComponent<ChainGrip>() != null) return false;
        var reference = hand.handhold.GetComponentInParent<WKMPMod.Component.RPContainerRef>();
        if (reference == null) return false;
        return !grips.TryGetValue(reference.container.PlayerId, out var other) || UnityEngine.Time.unscaledTime - other.Time > 0.35f || !other.Drag && !other.Hang;
    }
    public static void Apply(ENT_Player player, ref Vector3 velocity) {
        Vector3 total = Vector3.zero;
        foreach (var pair in grips) {
            var grip = pair.Value;
            if (!grip.Drag || UnityEngine.Time.unscaledTime - grip.Time > 0.35f || Holding(player, pair.Key) || !CoopPresence.Alive(pair.Key)) continue;
            Vector3 point = grip.Point + grip.Velocity * Mathf.Clamp(MPSteamworks.Instance.LanOneWayLatency(pair.Key), 0, 0.1f);
            Vector3 difference = point - PlayerChain.LocalAnchor(player);
            if (difference.sqrMagnitude > 64) continue;
            Vector3 desired = grip.Velocity + Vector3.ClampMagnitude(difference * 8f, 8f);
            total += Vector3.ClampMagnitude((desired - velocity) * 10f, 35f);
        }
        velocity += Vector3.ClampMagnitude(total, 35f) * UnityEngine.Time.fixedDeltaTime;
    }
}
