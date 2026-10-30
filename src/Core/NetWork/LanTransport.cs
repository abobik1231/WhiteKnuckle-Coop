using Newtonsoft.Json;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WKMPMod.NetWork;

internal sealed class LanMember {
	public ulong Id;
	public Dictionary<string, string> Data = new Dictionary<string, string>();
}

internal sealed class LanRoom {
	public int Protocol = 1;
	public ulong HostId;
	public int Capacity;
	public string GameVersion = "";
	public string ModVersion = "";
	public Dictionary<string, string> Data = new Dictionary<string, string>();
	public List<LanMember> Members = new List<LanMember>();
}

internal sealed class LanTransport : IDisposable {
	public const int DefaultPort = 47653;
	private const int MaxFrame = 1024 * 1024;
	private readonly object gate = new object();
	private readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();
	private readonly ConcurrentQueue<Action> events = new ConcurrentQueue<Action>();
	private readonly ConcurrentDictionary<ulong, byte[]> latestPositions = new ConcurrentDictionary<ulong, byte[]>();
	private readonly ConcurrentDictionary<(ulong Sender, ushort Type, ulong Entity), byte[]> latestStates = new ConcurrentDictionary<(ulong, ushort, ulong), byte[]>();
	private readonly ConcurrentQueue<(ulong Sender, byte[] Data)> reliablePackets = new ConcurrentQueue<(ulong Sender, byte[] Data)>();
	private readonly CancellationTokenSource stop = new CancellationTokenSource();
	private readonly ulong localId;
	private readonly bool isHost;
	private readonly LanRoom room;
	private readonly Timer heartbeat;
	private TcpListener listener;
	public Action<ulong, byte[]> Packet;
	public Action<LanMember> Joined;
	public Action<ulong> Left;
	public Action<ulong> Ready;
	public Action<ulong, Dictionary<string, string>> MemberChanged;
	public Action<Dictionary<string, string>> RoomChanged;
	public Action<string> Error;

	private enum Kind : byte { Hello = 1, Welcome, Packet, Room, Member, Joined, Left, Ping, Reject, RealtimePacket, Ready }
	private sealed class Control {
		public int Protocol = 1;
		public ulong Id;
		public string GameVersion = "";
		public string ModVersion = "";
		public string Message = "";
		public LanRoom Room;
		public LanMember Member;
		public Dictionary<string, string> Data;
	}
	private sealed class Frame {
		public Kind Kind;
		public byte[] Data;
		public Frame(Kind kind, byte[] data) { Kind = kind; Data = data; }
	}

	private LanTransport(ulong id, bool host, LanRoom state) {
		localId = id;
		isHost = host;
		room = state;
		heartbeat = new Timer(_ => Tick(), null, 2000, 2000);
	}

	public static ulong NewId() {
		ulong value = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0) & 0x7fffffffffffffffUL;
		return value < 2 ? value + 2 : value;
	}

	public static LanTransport Host(int port, ulong id, LanRoom state) {
		var transport = new LanTransport(id, true, state);
		try {
			transport.listener = new TcpListener(IPAddress.Any, port);
			transport.listener.Start();
			_ = transport.AcceptLoop();
			return transport;
		} catch {
			transport.Dispose();
			throw;
		}
	}

	public static async Task<LanTransport> Connect(string address, int port, ulong id, string gameVersion, string modVersion, CancellationToken cancellation) {
		var client = new TcpClient { NoDelay = true };
		try {
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
			timeout.CancelAfter(10000);
			using var registration = timeout.Token.Register(() => client.Close());
			await client.ConnectAsync(address, port).ConfigureAwait(false);
			var peer = new Peer(id, client);
			await Write(peer, Kind.Hello, Encode(new Control { Id = id, GameVersion = gameVersion, ModVersion = modVersion })).ConfigureAwait(false);
			var response = await Read(peer.Stream, timeout.Token).ConfigureAwait(false);
			if (response == null) throw new IOException("Host closed the connection during handshake.");
			var welcome = Decode(response.Data);
			if (response.Kind == Kind.Reject) throw new IOException(welcome.Message);
			if (response.Kind != Kind.Welcome || welcome.Room == null || welcome.Room.Protocol != 1)
				throw new IOException("Incompatible LAN protocol.");
			var transport = new LanTransport(id, false, welcome.Room);
			peer.Id = welcome.Room.HostId;
			lock (transport.gate) transport.peers.Add(peer.Id, peer);
			transport.StartPeer(peer);
			return transport;
		} catch {
			client.Close();
			throw;
		}
	}

	public LanRoom Snapshot() {
		lock (gate) return JsonConvert.DeserializeObject<LanRoom>(JsonConvert.SerializeObject(room));
	}

	public void Poll() {
		long started = Stopwatch.GetTimestamp();
		for (int i = 0; i < 32 && events.TryDequeue(out var action); i++) action();
		PollPositions();
		for (int i = 0; i < 48 && reliablePackets.TryDequeue(out var packet); i++) {
			if (MemberExists(packet.Sender)) Packet?.Invoke(packet.Sender, packet.Data);
			if ((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency >= 3) break;
		}
		foreach (var pair in latestStates) {
			if ((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency >= 4) break;
			if (latestStates.TryRemove(pair.Key, out var packet) && MemberExists(pair.Key.Sender)) Packet?.Invoke(pair.Key.Sender, packet);
		}
		PollPositions();
	}
	private static bool RealtimeIdentity(ReadOnlySpan<byte> packet, out ushort type, out ulong entity) {
		type = packet.Length >= 18 ? BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(16)) : ushort.MaxValue;
		entity = 0;
		if (type == 32 || type == 55 || type == 64 || type == 6) return true;
		if (type == 19 && packet.Length >= 36 && packet[18] == 1 && packet[27] == 1) { entity = BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(28)); return true; }
		if ((type == 56 || type == 67) && packet.Length >= 27 && packet[18] == 1) { entity = BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(19)); return true; }
		return false;
	}
	private void PollPositions() {
		foreach (var pair in latestPositions)
			if (latestPositions.TryRemove(pair.Key, out var packet) && MemberExists(pair.Key)) Packet?.Invoke(pair.Key, packet);
	}
	private bool MemberExists(ulong id) { lock (gate) return room.Members.Any(member => member.Id == id); }

	public void Send(ulong target, byte[] data, int offset, int length, bool reliable) {
		if (length < 18 || length > MaxFrame - 9) return;
		var payload = new byte[length + 8];
		BinaryPrimitives.WriteUInt64LittleEndian(payload, target);
		Buffer.BlockCopy(data, offset, payload, 8, length);
		foreach (var peer in PeerList()) {
			if (!isHost || target == 0 || peer.Id == target) Queue(peer, reliable ? Kind.Packet : Kind.RealtimePacket, payload, reliable);
		}
	}

	public void SetRoom(Dictionary<string, string> data) {
		if (!isHost) throw new InvalidOperationException("Only the host can change room settings.");
		lock (gate) room.Data = new Dictionary<string, string>(data);
		Broadcast(Kind.Room, new Control { Data = data });
	}

	public void SetMember(Dictionary<string, string> data) {
		lock (gate) {
			var member = room.Members.FirstOrDefault(item => item.Id == localId);
			if (member == null) { member = new LanMember { Id = localId }; room.Members.Add(member); }
			member.Data = new Dictionary<string, string>(data);
		}
		Broadcast(Kind.Member, new Control { Id = localId, Data = data });
	}
	public void MarkReady() { if (!isHost) Broadcast(Kind.Ready, new Control()); }

	private async Task AcceptLoop() {
		while (!stop.IsCancellationRequested) {
			try { _ = Accept(await listener.AcceptTcpClientAsync().ConfigureAwait(false)); }
			catch (Exception ex) {
				if (!stop.IsCancellationRequested) events.Enqueue(() => Error?.Invoke("LAN listener: " + ex.Message));
				break;
			}
		}
	}

	private async Task Accept(TcpClient client) {
		Peer peer = null;
		try {
			client.NoDelay = true;
			peer = new Peer(0, client);
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
			timeout.CancelAfter(10000);
			using var registration = timeout.Token.Register(() => client.Close());
			var first = await Read(peer.Stream, timeout.Token).ConfigureAwait(false);
			if (first == null || first.Kind != Kind.Hello) throw new IOException("Invalid LAN handshake.");
			var hello = Decode(first.Data);
			string rejection = null;
			lock (gate) {
				if (hello.Protocol != 1 || hello.ModVersion != room.ModVersion) rejection = "Both players need the same mod version.";
				else if (hello.GameVersion != room.GameVersion) rejection = "Both players need the same game version.";
				else if (hello.Id < 2 || room.Members.Any(item => item.Id == hello.Id)) rejection = "Duplicate player ID. Restart the joining game.";
				else if (room.Members.Count >= room.Capacity) rejection = "The room is full.";
				else {
					peer.Id = hello.Id;
					peers.Add(peer.Id, peer);
					room.Members.Add(new LanMember { Id = peer.Id });
				}
			}
			if (rejection != null) {
				await Write(peer, Kind.Reject, Encode(new Control { Message = rejection })).ConfigureAwait(false);
				client.Close();
				return;
			}
			await Write(peer, Kind.Welcome, Encode(new Control { Room = Snapshot() })).ConfigureAwait(false);
			registration.Dispose();
			var member = new LanMember { Id = peer.Id };
			events.Enqueue(() => Joined?.Invoke(member));
			Broadcast(Kind.Joined, new Control { Member = member }, peer.Id);
			StartPeer(peer);
		} catch (Exception ex) {
			if (!stop.IsCancellationRequested) events.Enqueue(() => Error?.Invoke("LAN handshake: " + ex.Message));
			if (peer != null) Drop(peer); else client.Close();
		}
	}

	private void StartPeer(Peer peer) {
		_ = ReadLoop(peer);
		_ = WriteLoop(peer);
		SendPing(peer);
	}
	private void SendPing(Peer peer) { var data = new byte[9]; BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(1), Stopwatch.GetTimestamp()); Queue(peer, Kind.Ping, data); }
	public float OneWayLatency(ulong id) {
		lock (gate) {
			if (peers.TryGetValue(id, out var peer)) return Volatile.Read(ref peer.Latency);
			return !isHost && peers.TryGetValue(room.HostId, out var host) ? Math.Min(0.25f, Volatile.Read(ref host.Latency) * 2f) : 0.035f;
		}
	}

	private async Task ReadLoop(Peer peer) {
		try {
			while (!stop.IsCancellationRequested && !peer.Closed) {
				var frame = await Read(peer.Stream, stop.Token).ConfigureAwait(false);
				if (frame == null) break;
				Interlocked.Exchange(ref peer.LastReceived, DateTime.UtcNow.Ticks);
				if (frame.Kind == Kind.Ping) {
					if (frame.Data.Length == 9) {
						if (frame.Data[0] == 0) { frame.Data[0] = 1; Queue(peer, Kind.Ping, frame.Data); }
						else {
							double elapsed = (Stopwatch.GetTimestamp() - BinaryPrimitives.ReadInt64LittleEndian(frame.Data.AsSpan(1))) / (double)Stopwatch.Frequency;
							if (elapsed >= 0 && elapsed < 2) Volatile.Write(ref peer.Latency, Math.Clamp((float)elapsed * 0.5f, 0.001f, 0.25f));
						}
					}
					continue;
				}
				if (frame.Kind == Kind.Packet || frame.Kind == Kind.RealtimePacket) { ReceivePacket(peer, frame.Data, frame.Kind == Kind.Packet); continue; }
				if (isHost && frame.Kind == Kind.Ready) { events.Enqueue(() => Ready?.Invoke(peer.Id)); continue; }
				var control = Decode(frame.Data);
				if (frame.Kind == Kind.Member) {
					ulong id = isHost ? peer.Id : control.Id;
					var values = control.Data ?? new Dictionary<string, string>();
					lock (gate) {
						var member = room.Members.FirstOrDefault(item => item.Id == id);
						if (member == null) throw new IOException("Unknown LAN member.");
						member.Data = values;
					}
					events.Enqueue(() => MemberChanged?.Invoke(id, values));
					if (isHost) Broadcast(Kind.Member, new Control { Id = id, Data = values }, peer.Id);
				} else if (!isHost && frame.Kind == Kind.Room) {
					var values = control.Data ?? new Dictionary<string, string>();
					lock (gate) room.Data = values;
					events.Enqueue(() => RoomChanged?.Invoke(values));
				} else if (!isHost && frame.Kind == Kind.Joined && control.Member != null) {
					lock (gate) room.Members.Add(control.Member);
					events.Enqueue(() => Joined?.Invoke(control.Member));
				} else if (!isHost && frame.Kind == Kind.Left) {
					lock (gate) room.Members.RemoveAll(item => item.Id == control.Id);
					events.Enqueue(() => Left?.Invoke(control.Id));
				} else throw new IOException("Unexpected LAN message.");
			}
		} catch (Exception ex) {
			if (!stop.IsCancellationRequested && !peer.Closed) events.Enqueue(() => Error?.Invoke("LAN connection: " + ex.Message));
		} finally { Drop(peer); }
	}

	private void ReceivePacket(Peer peer, byte[] envelope, bool reliable) {
		if (envelope.Length < 26) throw new IOException("Truncated LAN packet.");
		ulong target = BinaryPrimitives.ReadUInt64LittleEndian(envelope);
		ulong sender = BinaryPrimitives.ReadUInt64LittleEndian(envelope.AsSpan(8));
		if (isHost && sender != peer.Id) throw new IOException("LAN sender does not match its connection.");
		if (isHost && target != localId) {
			foreach (var other in PeerList())
				if (other.Id != peer.Id && (target == 0 || target == other.Id)) Queue(other, reliable ? Kind.Packet : Kind.RealtimePacket, envelope, reliable);
		}
		if (target == 0 || target == localId) {
			if (events.Count + reliablePackets.Count >= 4096) { Drop(peer); return; }
			var packet = new byte[envelope.Length - 8];
			Buffer.BlockCopy(envelope, 8, packet, 0, packet.Length);
			if (!reliable && RealtimeIdentity(packet, out var type, out var entity)) {
				if (type == 32) latestPositions[sender] = packet;
				else if (latestStates.Count < 512 || latestStates.ContainsKey((sender, type, entity))) latestStates[(sender, type, entity)] = packet;
			} else reliablePackets.Enqueue((sender, packet));
		}
	}

	private void Queue(Peer peer, Kind kind, byte[] data, bool reliable = true) {
		if (peer.Closed) return;
		bool overflow = false;
		lock (peer.QueueGate) {
			if (!reliable && kind == Kind.RealtimePacket && RealtimeIdentity(data.AsSpan(8), out var type, out var entity)) {
				var key = (BinaryPrimitives.ReadUInt64LittleEndian(data), BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(8)), type, entity);
				if (peer.Latest.ContainsKey(key)) { peer.Latest[key] = new Frame(kind, data); return; }
				if (peer.Latest.Count >= 256) return;
				peer.Latest.Add(key, new Frame(kind, data));
			} else {
				overflow = peer.Reliable.Count >= 2048 || peer.QueuedBytes + data.Length > 8 * MaxFrame;
				if (!overflow) { peer.Reliable.Enqueue(new Frame(kind, data)); peer.QueuedBytes += data.Length; }
			}
		}
		if (overflow) {
			events.Enqueue(() => Error?.Invoke("LAN send queue is full. Connection closed to prevent memory growth."));
			Drop(peer);
		} else peer.Signal.Release();
	}

	private async Task WriteLoop(Peer peer) {
		try {
			int reliableBurst = 0;
			while (!stop.IsCancellationRequested && !peer.Closed) {
				await peer.Signal.WaitAsync(stop.Token).ConfigureAwait(false);
				Frame frame = null;
				lock (peer.QueueGate) {
					if (peer.Latest.Count > 0 && (peer.Reliable.Count == 0 || reliableBurst >= 2)) {
						var first = peer.Latest.First();
						foreach (var candidate in peer.Latest) if (candidate.Key.Type == 32) { first = candidate; break; }
						frame = first.Value; peer.Latest.Remove(first.Key); reliableBurst = 0;
					}
					else if (peer.Reliable.Count > 0) { frame = peer.Reliable.Dequeue(); peer.QueuedBytes -= frame.Data.Length; reliableBurst++; }
				}
				if (frame != null) await Write(peer, frame.Kind, frame.Data).ConfigureAwait(false);
			}
		} catch (Exception ex) {
			if (!stop.IsCancellationRequested && !peer.Closed) events.Enqueue(() => Error?.Invoke("LAN send: " + ex.Message));
		} finally { Drop(peer); }
	}

	private void Broadcast(Kind kind, Control control, ulong except = 0) {
		var bytes = Encode(control);
		foreach (var peer in PeerList()) if (peer.Id != except) Queue(peer, kind, bytes);
	}
	private Peer[] PeerList() { lock (gate) return peers.Values.ToArray(); }
	private void Tick() {
		if (stop.IsCancellationRequested) return;
		foreach (var peer in PeerList()) {
			if (DateTime.UtcNow.Ticks - Interlocked.Read(ref peer.LastReceived) > TimeSpan.FromSeconds(20).Ticks) Drop(peer);
			else SendPing(peer);
		}
	}
	private void Drop(Peer peer) {
		if (Interlocked.Exchange(ref peer.IsClosed, 1) != 0) return;
		peer.Client.Close();
		peer.Signal.Release();
		latestPositions.TryRemove(peer.Id, out _);
		bool removed;
		lock (gate) {
			removed = peers.Remove(peer.Id);
			room.Members.RemoveAll(item => item.Id == peer.Id);
		}
		if (removed && !stop.IsCancellationRequested) {
			if (isHost) Broadcast(Kind.Left, new Control { Id = peer.Id });
			events.Enqueue(() => Left?.Invoke(peer.Id));
		}
	}

	public void Dispose() {
		if (stop.IsCancellationRequested) return;
		stop.Cancel();
		heartbeat.Dispose();
		listener?.Stop();
		foreach (var peer in PeerList()) Drop(peer);
		while (events.TryDequeue(out _)) { }
		latestPositions.Clear();
		latestStates.Clear();
		while (reliablePackets.TryDequeue(out _)) { }
	}

	private static byte[] Encode(Control value) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value));
	private static Control Decode(byte[] data) => JsonConvert.DeserializeObject<Control>(Encoding.UTF8.GetString(data)) ?? throw new IOException("Invalid LAN control message.");
	private static async Task Write(Peer peer, Kind kind, byte[] data) {
		if (data.Length >= MaxFrame) throw new IOException("LAN message exceeds size limit.");
		var frame = new byte[data.Length + 5];
		BinaryPrimitives.WriteInt32LittleEndian(frame, data.Length + 1);
		frame[4] = (byte)kind;
		Buffer.BlockCopy(data, 0, frame, 5, data.Length);
		await peer.Stream.WriteAsync(frame, 0, frame.Length).ConfigureAwait(false);
	}
	private static async Task<Frame> Read(NetworkStream stream, CancellationToken cancellation) {
		var header = new byte[4];
		if (!await Exact(stream, header, cancellation).ConfigureAwait(false)) return null;
		int size = BinaryPrimitives.ReadInt32LittleEndian(header);
		if (size < 1 || size > MaxFrame) throw new IOException("Invalid LAN frame size.");
		var payload = new byte[size];
		if (!await Exact(stream, payload, cancellation).ConfigureAwait(false)) throw new IOException("Truncated LAN frame.");
		return new Frame((Kind)payload[0], payload.Skip(1).ToArray());
	}
	private static async Task<bool> Exact(Stream stream, byte[] bytes, CancellationToken cancellation) {
		int offset = 0;
		while (offset < bytes.Length) {
			int read = await stream.ReadAsync(bytes, offset, bytes.Length - offset, cancellation).ConfigureAwait(false);
			if (read == 0) { if (offset == 0) return false; throw new IOException("Truncated LAN header."); }
			offset += read;
		}
		return true;
	}
	private sealed class Peer {
		public ulong Id;
		public readonly TcpClient Client;
		public readonly NetworkStream Stream;
		public float Latency = 0.035f;
		public readonly object QueueGate = new object();
		public readonly Queue<Frame> Reliable = new Queue<Frame>();
		public readonly Dictionary<(ulong Target, ulong Sender, ushort Type, ulong Entity), Frame> Latest = new Dictionary<(ulong, ulong, ushort, ulong), Frame>();
		public readonly SemaphoreSlim Signal = new SemaphoreSlim(0);
		public int IsClosed;
		public int QueuedBytes;
		public long LastReceived = DateTime.UtcNow.Ticks;
		public bool Closed => Volatile.Read(ref IsClosed) != 0;
		public Peer(ulong id, TcpClient client) { Id = id; Client = client; Stream = client.GetStream(); }
	}
}
