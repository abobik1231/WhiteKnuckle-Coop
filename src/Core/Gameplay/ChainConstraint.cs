using UnityEngine;

namespace WKMPMod.Gameplay;

public static class ChainConstraint {
	public static void Solve(Vector3 separation, Vector3 relativeVelocity, float length, float deltaTime, out Vector3 velocityChange, out Vector3 correction) {
		correction = Vector3.zero;
		var change = TetherMath.Impulse(new System.Numerics.Vector3(separation.x, separation.y, separation.z), new System.Numerics.Vector3(relativeVelocity.x, relativeVelocity.y, relativeVelocity.z), length, deltaTime);
		velocityChange = new Vector3(change.X, change.Y, change.Z);
	}
	public static float Sag(float distance, float length) => Mathf.Min(2.5f, Mathf.Sqrt(Mathf.Max(0, length * length - distance * distance)) * 0.5f);
	public static Vector3 Point(Vector3 start, Vector3 end, float sag, float t) => Vector3.Lerp(start, end, t) + Vector3.down * (4f * t * (1f - t) * sag);
}
