using System;
using System.Numerics;

namespace WKMPMod.Gameplay;

public static class TetherMath {
	public const float MovementScale = 175f;
	public static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
	public static Vector3 Impulse(Vector3 separation, Vector3 relativeVelocity, float length, float dt, float share = 1f) {
		if (!Finite(separation) || !Finite(relativeVelocity) || !float.IsFinite(length) || !float.IsFinite(dt) || !float.IsFinite(share) || length < 0.5f || dt < 0.001f || dt > 0.1f || share <= 0) return Vector3.Zero;
		float distance = separation.Length();
		if (distance < 0.001f || distance > Math.Max(50f, length * 4)) return Vector3.Zero;
		var direction = separation / distance;
		float outward = -Vector3.Dot(relativeVelocity, direction);
		float slack = Math.Max(0, length - distance);
		if (distance + outward * dt <= length && distance <= length) return Vector3.Zero;
		float recovery = Math.Min(Math.Max(0, distance - length) * 5f, 2f);
		float impulse = Math.Max(0, outward + recovery - slack / dt);
		return direction * impulse * Math.Clamp(share, 0, 1);
	}
	public static float Reel(float current, float target, float dt) {
		if (!float.IsFinite(current) || !float.IsFinite(target) || !float.IsFinite(dt) || dt <= 0) return target;
		float difference = target - current;
		float step = (difference < 0 ? 1.2f : 4f) * Math.Min(dt, 0.1f);
		return Math.Abs(difference) <= step ? target : current + Math.Sign(difference) * step;
	}
}
