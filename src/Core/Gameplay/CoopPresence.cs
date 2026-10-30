using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.Data;
using WKMPMod.NetWork;
using WKMPMod.UI;

namespace WKMPMod.Gameplay;

public sealed class CoopPresence : MonoBehaviour {
	public sealed class State {
		public Quaternion CameraRotation;
		public Vector3 CameraOffset;
		public Vector3 AnchorOffset;
		public bool Alive;
		public bool Anchored;
		public float Received;
	}
	public static readonly Dictionary<ulong, State> Views = new Dictionary<ulong, State>();
	private float nextSend;
	public static bool Alive(ulong id) => !Views.TryGetValue(id, out var state) || state.Alive;
	private void LateUpdate() {
		if (!MPCore.IsInLobby) { if (Views.Count > 0) Views.Clear(); return; }
		if (ManualSaves.Loading || ManualSaves.GroupLoading || Time.unscaledTime < nextSend) return;
		var player = ENT_Player.GetPlayer();
		if (player?.cam == null) return;
		nextSend = Time.unscaledTime + 0.05f;
		var writer = MPWriterPool.GetWriter(MPSteamworks.UserSteamId, MPProtocol.BroadcastId, PacketType.PlayerView);
		writer.Put(ManualSaves.LoadGeneration);
		writer.Put(player.cam.transform.rotation);
		writer.Put(Vector3.ClampMagnitude(player.cam.transform.position - player.transform.position, 3f));
		writer.Put(!player.IsDead());
		writer.Put(!player.IsDead() && player.hands.Any(PlayerChain.SurfaceHeld));
		writer.Put(Vector3.ClampMagnitude(PlayerChain.LocalAnchor(player) - player.transform.position, 3f));
		MPSteamworks.Instance.Broadcast(writer, Steamworks.Data.SendType.Unreliable | Steamworks.Data.SendType.NoNagle);
	}
	public static void Receive(ulong sender, DataReader reader) {
		if (!MPSteamworks.Instance.HasConnection(sender) || ManualSaves.Loading || ManualSaves.GroupLoading || reader.GetULong() != ManualSaves.LoadGeneration) return;
		Views[sender] = new State { CameraRotation = CoopCoordinates.RemoteRotation(sender, reader.GetQuaternion()), CameraOffset = CoopCoordinates.RemoteDirection(sender, reader.GetVector3()), Alive = reader.GetBool(), Anchored = reader.GetBool(), AnchorOffset = CoopCoordinates.RemoteDirection(sender, reader.GetVector3()), Received = Time.unscaledTime };
	}
}
