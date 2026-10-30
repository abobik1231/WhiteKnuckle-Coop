using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.RemotePlayer;
using RemoteBody = WKMPMod.Component.RemotePlayer;

namespace WKMPMod.Gameplay;

[DefaultExecutionOrder(1000)]
public sealed class PlayerChain : MonoBehaviour {
	public const string EnabledKey = "chain enabled";
	public const string LengthKey = "chain length";
	private static readonly AccessTools.FieldRef<ENT_Player, Vector3> Velocity = AccessTools.FieldRefAccess<ENT_Player, Vector3>("vel");
	private static PlayerChain instance;
	private sealed class Edge { public float Length; public float ResumeAt; public Vector3 LastLocal; public Vector3 LastOther; public bool Ready; }
	private readonly Dictionary<string, ChainVisual> visuals = new Dictionary<string, ChainVisual>();
	private readonly Dictionary<ulong, RemoteBody> bodies = new Dictionary<ulong, RemoteBody>();
	private readonly Dictionary<ulong, Edge> edges = new Dictionary<ulong, Edge>();
	private readonly List<ulong> order = new List<ulong>();
	private readonly HashSet<string> usedEdges = new HashSet<string>();
	private ENT_Player localPlayer;
	private float nextMembershipRefresh;
	private Material material;
	public static bool SupportingPlayer { get; private set; }
	public static Vector3 LocalAnchor(ENT_Player player) => player.cCon != null ? player.transform.TransformPoint(player.cCon.center) : player.transform.position;
	public static bool SurfaceHeld(ENT_Player.Hand hand) => hand.interactState == ENT_Player.InteractType.hanging && hand.handhold != null && !MobileHeld(hand);
	public static bool MobileHeld(ENT_Player.Hand hand) => hand.handhold != null && (hand.handhold.GetComponent<ChainGrip>() != null || hand.handhold.GetComponentInParent<WKMPMod.Component.RPContainerRef>() != null);
	public static bool RoomEnabled => MPSteamworks.Instance.IsLan && MPSteamworks.Instance.LobbyData != null && bool.TryParse(MPSteamworks.Instance.LobbyData.GetValueOrDefault(EnabledKey), out var value) && value;
	public static float RoomLength => MPSteamworks.Instance.LobbyData != null && float.TryParse(MPSteamworks.Instance.LobbyData.GetValueOrDefault(LengthKey), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? Mathf.Clamp(value, 3f, 12f) : 6f;
	private void Awake() { instance = this; }
	public static void CapturePhysics(ENT_Player player, ref PlayerData data) {
		data.HasPhysics = true;
		data.WorldVelocity = player.cCon != null ? Vector3.ClampMagnitude(player.cCon.velocity, 100f) : Velocity(player) * TetherMath.MovementScale;
		data.AnchorOffset = LocalAnchor(player) - player.transform.position;
		data.SurfaceAnchored = player.hands != null && player.hands.Any(SurfaceHeld) && !CoopGrabAuthority.LocalPulled;
		data.MobPulled = CoopGrabAuthority.LocalPulled;
		GetGrip(player, out data.GripPeer, out data.GripLength);
	}
	private static void GetGrip(ENT_Player player, out ulong peer, out float length) {
		peer = 0; length = float.PositiveInfinity;
		if (player.hands == null) return;
		ulong local = MPSteamworks.UserSteamId;
		foreach (var hand in player.hands) {
			ulong target = 0; float limit = float.PositiveInfinity;
			if (hand.interactState == ENT_Player.InteractType.hanging && hand.handhold != null) {
				var grip = hand.handhold.GetComponent<ChainGrip>();
				if (grip != null && RoomEnabled) {
					target = grip.First == local ? grip.Second : grip.Second == local ? grip.First : 0;
					float remaining = grip.First == local ? 1f - grip.Fraction : grip.Fraction;
					limit = Mathf.Clamp(remaining * RoomLength + 1.1f, 1.25f, RoomLength);
				} else {
					var reference = hand.handhold.GetComponentInParent<WKMPMod.Component.RPContainerRef>();
					if (reference != null) { target = reference.container.PlayerId; limit = 1.6f; }
				}
			}
			if (target != 0 && target != local && limit < length) { peer = target; length = limit; }
		}
		if (peer == 0) length = 0;
	}
	private bool Prepare() {
		if (!MPCore.CanSync || !MPSteamworks.Instance.IsLan || UI.ManualSaves.Loading || UI.ManualSaves.GroupLoading) { Clear(); return false; }
		var player = ENT_Player.GetPlayer();
		if (player == null || player.IsDead()) { Clear(); return false; }
		localPlayer = player;
		if (Time.unscaledTime >= nextMembershipRefresh) {
			nextMembershipRefresh = Time.unscaledTime + 0.2f;
			order.Clear(); order.Add(MPSteamworks.Instance.HostSteamId);
			order.AddRange(MPSteamworks.Instance.Members.Select(member => member.Id.Value).Where(id => id != order[0]).OrderBy(id => id));
			foreach (ulong id in bodies.Keys.Where(id => !order.Contains(id)).ToArray()) { bodies.Remove(id); edges.Remove(id); }
		}
		return true;
	}
	private RemoteBody GetBody(ulong id) {
		if (bodies.TryGetValue(id, out var body) && body != null) return body;
		if (!RPManager.Instance.Players.TryGetValue(id, out var container) || container.PlayerObject == null) return null;
		body = container.PlayerObject.GetComponentInChildren<RemoteBody>(true); bodies[id] = body; return body;
	}
	private bool Anchor(ulong id, bool visual, out Vector3 position) {
		if (id == MPSteamworks.UserSteamId) { position = LocalAnchor(localPlayer); return true; }
		var body = GetBody(id);
		if (body == null || !body.HasSnapshot || !body.gameObject.activeInHierarchy || !CoopPresence.Alive(id) || Time.unscaledTime - body.LastReceivedTime > 0.35f) { position = default; return false; }
		Vector3 offset = body.HasPhysics ? body.AnchorOffset : CoopPresence.Views.TryGetValue(id, out var view) ? view.AnchorOffset : Vector3.zero;
		float prediction = Mathf.Clamp(Time.unscaledTime - body.LastReceivedTime + MPSteamworks.Instance.LanOneWayLatency(id), 0, 0.2f);
		position = (visual ? body.transform.position : body.NetworkPosition + body.NetworkVelocity * prediction + body.NetworkAcceleration * (0.5f * prediction * prediction)) + offset;
		return true;
	}
	public static CollisionFlags MovePlayer(CharacterController controller, Vector3 motion) {
		var player = ENT_Player.GetPlayer();
		if (instance == null || player == null || controller != player.cCon || !instance.Prepare() || Time.fixedDeltaTime <= 0) return controller.Move(motion);
		var velocity = motion / Time.fixedDeltaTime;
		CoopPlayerGrip.Apply(player, ref velocity);
        instance.Constrain(ref velocity);
		Velocity(player) = velocity / TetherMath.MovementScale;
		return controller.Move(velocity * Time.fixedDeltaTime);
	}
	private void Constrain(ref Vector3 velocity) {
		SupportingPlayer = false;
		ulong local = MPSteamworks.UserSteamId;
		int localIndex = order.IndexOf(local);
		if (localIndex < 0) return;
		bool held = localPlayer.hands != null && localPlayer.hands.Any(SurfaceHeld);
		bool anchored = held && !CoopGrabAuthority.LocalPulled;
		GetGrip(localPlayer, out var gripPeer, out var gripLength);
		Vector3 current = LocalAnchor(localPlayer);
		for (int i = 0; i < order.Count; i++) {
			ulong id = order[i]; if (id == local || !Anchor(id, false, out var other)) continue;
			var body = GetBody(id);
			float length = RoomEnabled && Math.Abs(i - localIndex) == 1 ? RoomLength : float.PositiveInfinity;
			if (gripPeer == id) length = Mathf.Min(length, gripLength);
			if (body.HasPhysics && body.GripPeer == local && body.GripLength >= 1.25f) length = Mathf.Min(length, body.GripLength);
			if (!float.IsFinite(length)) { edges.Remove(id); continue; }
			if (!edges.TryGetValue(id, out var edge)) edges[id] = edge = new Edge();
			float distance = Vector3.Distance(current, other);
			if (!edge.Ready || (current - edge.LastLocal).sqrMagnitude > 16 || (other - edge.LastOther).sqrMagnitude > 16 || Time.unscaledTime - body.LastTeleportTime < 0.4f || distance > Mathf.Max(50, length * 4)) {
				edge.Ready = true; edge.ResumeAt = Time.unscaledTime + 0.15f; edge.Length = Mathf.Max(length, Mathf.Min(distance, 20));
			}
			edge.LastLocal = current; edge.LastOther = other;
			if (Time.unscaledTime < edge.ResumeAt) continue;
			edge.Length = TetherMath.Reel(edge.Length, length, Time.fixedDeltaTime);
			bool otherAnchored = body.HasPhysics ? body.SurfaceAnchored : CoopPresence.Views.TryGetValue(id, out var presence) && presence.Anchored;
			if (held && !otherAnchored && other.y < current.y - 0.4f && distance >= edge.Length - 0.1f) SupportingPlayer = true;
			if (anchored) continue;
			float prediction = Mathf.Clamp(Time.unscaledTime - body.LastReceivedTime + MPSteamworks.Instance.LanOneWayLatency(id), 0, 0.2f);
			Vector3 otherVelocity = body.NetworkVelocity + body.NetworkAcceleration * prediction;
			ChainConstraint.Solve(other - current, velocity - otherVelocity, edge.Length, Time.fixedDeltaTime, out var delta, out _);
			velocity += delta * (otherAnchored ? 1f : 0.5f);
		}
	}
	private void FixedUpdate() { if (!Prepare()) SupportingPlayer = false; ValidateGrips(); }
	private void ValidateGrips() {
		if (localPlayer?.hands == null) return;
		foreach (var hand in localPlayer.hands) {
			if (hand.handhold == null || hand.interactState != ENT_Player.InteractType.hanging) continue;
			var grip = hand.handhold.GetComponent<ChainGrip>(); if (grip == null) continue;
			ulong local = MPSteamworks.UserSteamId;
			ulong peer = grip.First == local ? grip.Second : grip.Second == local ? grip.First : 0;
			if (!RoomEnabled || peer == 0 || !Anchor(peer, false, out _) || Vector3.Distance(hand.GetHoldWorldPosition(), localPlayer.camTransform.position) > Mathf.Max(3, localPlayer.GetInteractDistance() * 1.5f)) hand.DropHand();
		}
	}
	private void LateUpdate() {
		if (!Prepare()) return;
		usedEdges.Clear();
		if (RoomEnabled) for (int i = 1; i < order.Count; i++) {
			if (!Anchor(order[i - 1], true, out var a) || !Anchor(order[i], true, out var b) || Vector3.Distance(a, b) > 50) continue;
			string key = order[i - 1].ToString() + ":" + order[i]; usedEdges.Add(key);
			if (!visuals.TryGetValue(key, out var visual)) {
				if (material == null) {
					var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
					material = new Material(shader) { color = new Color(0.24f, 0.27f, 0.3f) };
					if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.8f);
					if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.5f);
				}
				visual = new ChainVisual(transform, material, Mathf.Clamp(Mathf.CeilToInt(RoomLength / 0.17f), 16, 80));
				visual.SetPlayers(order[i - 1], order[i]); visuals.Add(key, visual);
			}
			visual.Update(a, b, RoomLength);
		}
		foreach (var key in visuals.Keys.Where(key => !usedEdges.Contains(key)).ToArray()) { visuals[key].Dispose(); visuals.Remove(key); }
	}
	private void Clear() {
		SupportingPlayer = false;
		if (visuals.Count > 0) { foreach (var visual in visuals.Values) visual.Dispose(); visuals.Clear(); }
		bodies.Clear(); edges.Clear(); order.Clear(); nextMembershipRefresh = 0;
	}
	private void OnDestroy() { Clear(); if (material != null) Destroy(material); if (instance == this) instance = null; }
	public sealed class ChainVisual {
		private const int Around = 16;
		private const int Tube = 6;
		private readonly GameObject root;
		private readonly Mesh mesh;
		private readonly int links;
		private readonly Vector3[] vertices;
		private readonly Vector3[] normals;
		private readonly Vector3[] ringVertices;
		private readonly Vector3[] ringNormals;
		private readonly CapsuleCollider[] grips;
		private readonly Vector3[] path = new Vector3[65];
		private readonly float[] arc = new float[65];
		private Vector3 firstUp = Vector3.up;
		public Mesh Geometry => mesh;
		public IReadOnlyList<CapsuleCollider> Grips => grips;
		public ChainVisual(Transform parent, Material material, int count) {
			links = Mathf.Clamp(count, 2, 80);
			root = new GameObject("Player Chain");
			root.transform.SetParent(parent, false);
			mesh = new Mesh { name = "Interlocking Chain" };
			mesh.MarkDynamic();
			root.AddComponent<MeshFilter>().sharedMesh = mesh;
			root.AddComponent<MeshRenderer>().sharedMaterial = material;
			int ringSize = Around * Tube;
			ringVertices = new Vector3[ringSize];
			ringNormals = new Vector3[ringSize];
			for (int segment = 0; segment < Around; segment++) {
				float angle = segment * Mathf.PI * 2 / Around;
				Vector3 center = new Vector3(Mathf.Sin(angle) * 0.065f, 0, Mathf.Cos(angle) * 0.115f);
				Vector3 radial = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)).normalized;
				for (int side = 0; side < Tube; side++) {
					float around = side * Mathf.PI * 2 / Tube;
					int index = segment * Tube + side;
					ringNormals[index] = radial * Mathf.Cos(around) + Vector3.up * Mathf.Sin(around);
					ringVertices[index] = center + ringNormals[index] * 0.018f;
				}
			}
			vertices = new Vector3[links * ringSize];
			normals = new Vector3[vertices.Length];
			var triangles = new int[links * Around * Tube * 6];
			int cursor = 0;
			for (int link = 0; link < links; link++) for (int segment = 0; segment < Around; segment++) for (int side = 0; side < Tube; side++) {
				int a = link * ringSize + segment * Tube + side;
				int b = link * ringSize + (segment + 1) % Around * Tube + side;
				int c = link * ringSize + (segment + 1) % Around * Tube + (side + 1) % Tube;
				int d = link * ringSize + segment * Tube + (side + 1) % Tube;
				triangles[cursor++] = a; triangles[cursor++] = b; triangles[cursor++] = c;
				triangles[cursor++] = a; triangles[cursor++] = c; triangles[cursor++] = d;
			}
			mesh.vertices = vertices;
			mesh.triangles = triangles;
			grips = new CapsuleCollider[Mathf.Clamp(count / 3, 4, 26)];
			for (int i = 0; i < grips.Length; i++) {
				var grip = new GameObject("Chain Grip " + i);
				grip.transform.SetParent(root.transform, false);
				grip.AddComponent<ChainGrip>();
				var collider = grip.AddComponent<CapsuleCollider>();
				collider.radius = 0.09f;
				collider.direction = 2;
				collider.isTrigger = true;
				grips[i] = collider;
				var hold = grip.AddComponent<CL_Handhold>();
				hold.secure = true;
				hold.dropOnInactive = true;
				hold.useSharedHandholdMaterial = false;
				hold.allowAirbornJump = false;
				hold.jumpMult = 0;
				hold.climbMult = 0;
				hold.hangVelAdjust = 0;
				hold.velocityMult = 0;
				hold.activeEvent = new UnityEngine.Events.UnityEvent();
				hold.stopEvent = new UnityEngine.Events.UnityEvent();
				hold.hammerEvent = new UnityEngine.Events.UnityEvent();
				grip.GetComponent<ObjectTagger>().AddTag("Handhold");
				var player = ENT_Player.GetPlayer();
				if (player != null && player.cCon != null) Physics.IgnoreCollision(player.cCon, collider);
			}
		}
		public void SetPlayers(ulong first, ulong second) {
			for (int i = 0; i < grips.Length; i++) {
				var grip = grips[i].GetComponent<ChainGrip>(); grip.First = first; grip.Second = second; grip.Fraction = (i + 0.5f) / grips.Length;
				bool available = MPSteamworks.UserSteamId == first || MPSteamworks.UserSteamId == second;
				grips[i].enabled = available; grips[i].GetComponent<CL_Handhold>().active = available;
			}
		}
		private float CurveLength(Vector3 start, Vector3 end, float sag) {
			Vector3 previous = start; float length = 0;
			for (int i = 1; i <= 32; i++) { var point = ChainConstraint.Point(start, end, sag, i / 32f); length += Vector3.Distance(previous, point); previous = point; }
			return length;
		}
		private void BuildPath(Vector3 start, Vector3 end, float length) {
			float low = 0, high = Mathf.Max(length, 0.1f);
			if (Vector3.Distance(start, end) < length) for (int i = 0; i < 10; i++) {
				float mid = (low + high) * 0.5f;
				if (CurveLength(start, end, mid) < length) low = mid; else high = mid;
			} else high = 0;
			float sag = (low + high) * 0.5f;
			path[0] = start; arc[0] = 0;
			for (int i = 1; i < path.Length; i++) { path[i] = ChainConstraint.Point(start, end, sag, i / 64f); arc[i] = arc[i - 1] + Vector3.Distance(path[i - 1], path[i]); }
		}
		private Vector3 At(float fraction) {
			float distance = Mathf.Clamp01(fraction) * arc[64]; int low = 0, high = 64;
			while (high - low > 1) { int mid = (low + high) / 2; if (arc[mid] < distance) low = mid; else high = mid; }
			float t = arc[high] > arc[low] ? (distance - arc[low]) / (arc[high] - arc[low]) : 0;
			return Vector3.Lerp(path[low], path[high], t);
		}
		public void Update(Vector3 start, Vector3 end, float length) {
			Matrix4x4 worldToLocal = root.transform.worldToLocalMatrix;
			BuildPath(start, end, length);
			for (int i = 0; i < grips.Length; i++) {
				Vector3 a = At((float)i / grips.Length);
				Vector3 b = At((float)(i + 1) / grips.Length);
				Quaternion rotation = b == a ? Quaternion.identity : Quaternion.LookRotation(b - a);
				grips[i].transform.SetPositionAndRotation((a + b) * 0.5f, rotation);
				grips[i].height = Mathf.Max(0.18f, Vector3.Distance(a, b) + 0.1f);
			}
			Vector3 up = firstUp;
			for (int link = 0; link < links; link++) {
				float t = (link + 0.5f) / links;
				Vector3 position = At(t);
				Vector3 tangent = At(t + 0.002f) - At(t - 0.002f);
				if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.forward;
				up = Vector3.ProjectOnPlane(up, tangent.normalized);
				if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(Vector3.right, tangent.normalized);
				if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(Vector3.forward, tangent.normalized);
				up.Normalize(); if (link == 0) firstUp = up;
				Quaternion rotation = Quaternion.LookRotation(tangent, up) * Quaternion.AngleAxis(link % 2 * 90f, Vector3.forward);
				for (int v = 0; v < ringVertices.Length; v++) {
					int index = link * ringVertices.Length + v;
					vertices[index] = worldToLocal.MultiplyPoint3x4(position + rotation * ringVertices[v]);
					normals[index] = worldToLocal.MultiplyVector(rotation * ringNormals[v]);
				}
			}
			mesh.vertices = vertices;
			mesh.normals = normals;
			mesh.RecalculateBounds();
		}
		public void Dispose() {
			var player = ENT_Player.GetPlayer();
			if (player != null && player.hands != null) foreach (var hand in player.hands) if (hand.handhold != null && hand.handhold.transform.IsChildOf(root.transform)) hand.DropHand();
			Destroy(root); Destroy(mesh);
		}
	}
}

public sealed class ChainGrip : MonoBehaviour {
	public ulong First;
	public ulong Second;
	public float Fraction;
}


