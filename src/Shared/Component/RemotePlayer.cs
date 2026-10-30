using System.Collections;
using UnityEngine;
using WKMPMod.Data;

namespace WKMPMod.Component;

public class RemotePlayer : MonoBehaviour {
    public static bool LowLatency;
    public ulong playerId;
    public float teleportThreshold = 50f;
    public float maxSmoothDistance = 10f;
    public bool HasSnapshot { get; private set; }
    public float LastReceivedTime { get; private set; }
    public Vector3 NetworkVelocity { get; private set; }
    public Vector3 NetworkPosition => targetPosition;
    public float LastTeleportTime { get; private set; }
    public bool HasPhysics { get; private set; }
    public Vector3 AnchorOffset { get; private set; }
    public bool SurfaceAnchored { get; private set; }
    public bool MobPulled { get; private set; }
    public ulong GripPeer { get; private set; }
    public float GripLength { get; private set; }
    public Vector3 NetworkAcceleration { get; private set; }
    private long lastSenderTicks;
    private bool teleporting;
    private Vector3 targetPosition;
    private Vector3 smoothVelocity;

    private void LateUpdate() {
        if (teleporting || !HasSnapshot) return;
        float age = Time.unscaledTime - LastReceivedTime;
        float prediction = LowLatency && age < 0.12f ? Mathf.Min(age, 0.035f) * Mathf.Clamp01((0.12f - age) / 0.06f) : 0;
        Vector3 destination = targetPosition + NetworkVelocity * prediction;
        float distance = Vector3.Distance(transform.position, destination);
        if (distance > teleportThreshold) { Teleport(targetPosition); return; }
        float smoothing = LowLatency ? 0.018f : Mathf.Clamp(distance / 10f, 0.05f, 0.1f);
        transform.position = Vector3.SmoothDamp(transform.position, destination, ref smoothVelocity, smoothing, float.MaxValue, LowLatency ? Time.unscaledDeltaTime : Time.deltaTime);
    }

    public void UpdateFromPlayerData(Vector3 position, Quaternion rotation) {
        float now = Time.unscaledTime;
        float interval = now - LastReceivedTime;
        if (HasSnapshot && interval >= 0.004f && interval <= 0.25f && Vector3.Distance(position, targetPosition) < teleportThreshold)
            NetworkVelocity = Vector3.ClampMagnitude((position - targetPosition) / interval, 35f);
        else NetworkVelocity = Vector3.zero;
        targetPosition = position;
        LastReceivedTime = now;
        HasSnapshot = true;
        teleporting = false;
        transform.rotation = rotation;
    }

    public void UpdateFromPlayerData(ref PlayerData data) {
        Vector3 previous = targetPosition;
        Vector3 previousVelocity = NetworkVelocity;
        bool valid = HasSnapshot;
        long ticks = lastSenderTicks;
        UpdateFromPlayerData(data.Position, data.Rotation);
        float interval = (float)((data.TimestampTicks - ticks) / 10000000.0);
        if (valid && ticks > 0 && interval >= 0.004f && interval <= 0.3f) {
            Vector3 measured = Vector3.ClampMagnitude((data.Position - previous) / interval, 25f);
            NetworkVelocity = Vector3.Lerp(previousVelocity, measured, 0.5f);
        }
        lastSenderTicks = data.TimestampTicks;
        HasPhysics = data.HasPhysics;
        AnchorOffset = data.AnchorOffset;
        SurfaceAnchored = data.SurfaceAnchored;
        MobPulled = data.MobPulled;
        GripPeer = data.GripPeer;
        GripLength = data.GripLength;
        if (data.HasPhysics) {
            NetworkVelocity = Vector3.ClampMagnitude(data.WorldVelocity, 100f);
            NetworkAcceleration = valid && interval >= 0.004f && interval <= 0.3f ? Vector3.Lerp(NetworkAcceleration, Vector3.ClampMagnitude((NetworkVelocity - previousVelocity) / interval, 100f), 0.35f) : Vector3.zero;
        }
    }

    public void Teleport(Vector3 position, Quaternion? rotation = null) {
        teleporting = true;
        LastTeleportTime = Time.unscaledTime;
        lastSenderTicks = 0;
        transform.position = position;
        targetPosition = position;
        LastReceivedTime = Time.unscaledTime;
        NetworkVelocity = Vector3.zero;
        NetworkAcceleration = Vector3.zero;
        smoothVelocity = Vector3.zero;
        HasSnapshot = true;
        if (rotation.HasValue) transform.rotation = rotation.Value;
        StartCoroutine(ResetTeleportFlag());
    }
    private IEnumerator ResetTeleportFlag() { yield return null; teleporting = false; }
}
