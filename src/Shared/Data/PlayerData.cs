using System.Numerics;
using UnityEngine;
using WKMPMod.Data;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace WKMPMod.Data;

[Serializable]
public struct PlayerData : INetworkSerializable {
	public ulong CheckpointGeneration;
	private const float POSITION_CHANGE_THRESHOLD_SQR = 0.0025f;    // 0.05单位的平方
	private const float ROTATION_CHANGE_THRESHOLD_DEG = 0.5f;       // 最小旋转角度

	[Serializable]
	public struct HandData : INetworkSerializable {
		// 手部Id
		public byte handType;
		// 手部交互状态
		public byte interactState;
		// 位置
		public float PosX;
		public float PosY;
		public float PosZ;
		// 是否更新手部物品
		public bool handItemUpdate;
		// 手部持握物品
		public string itemPrefabName;
		// 被抓取/拖拽玩家的ID
		public ulong targetId;
		// 拖拽点
		public float desiredPosX;
		public float desiredPosY;
		public float desiredPosZ;

		public Vector3 Position {
			get => new Vector3(PosX, PosY, PosZ);
			set {
				PosX = value.x; PosY = value.y; PosZ = value.z;
			}
		}

		// 拖拽吸引点
		public Vector3 DesiredPosition {
			get => new Vector3(desiredPosX, desiredPosY, desiredPosZ);
			set {
				desiredPosX = value.x; desiredPosY = value.y; desiredPosZ = value.z;
			}
		}

		public void Serialize(DataWriter writer) {
			writer.Put(handType);
			writer.Put(Position);
			writer.Put((byte)interactState);
			switch (interactState) {
				// none: 无抓取时, 显示手持物
				case 0: {
					writer.Put(handItemUpdate);
					if (handItemUpdate) writer.Put(itemPrefabName);
					break;
				}
				// grab: 拖拽
				case 2: {
					writer.Put(targetId);
					writer.Put(DesiredPosition);
					break;
				}
				// hanging: 抓取
				case 4: {
					writer.Put(targetId);
					break;
				}
				default:
					break;
			}
		}
		public void Deserialize(DataReader reader) {
			handType = reader.GetByte();
			Position = reader.GetVector3();
			interactState = reader.GetByte();
			switch (interactState) {
				case 0: {
					handItemUpdate = reader.GetBool();
					if (handItemUpdate) itemPrefabName = reader.GetString();
					else itemPrefabName = null;
					break;
				}
				case 2: {
					targetId = reader.GetULong();
					DesiredPosition = reader.GetVector3();
					break;
				}
				case 4: {
					targetId = reader.GetULong();
					break;
				}
				default:
					break;
			}
		}

		public bool IsBeGrabbing(ulong targetPlayerId) {
			return interactState == 2 && targetId == targetPlayerId;
		}

		public bool IsBeHanging(ulong targetPlayerId) {
			return interactState == 4 && targetId == targetPlayerId;
		}

		public static bool operator ==(HandData left, HandData right) {
			// 1. 比较基础枚举值
			if (left.handType != right.handType ||
				left.interactState != right.interactState) {
				return false;
			}

			// 2. 使用 sqrMagnitude 比较手部基础位置
			// 展开计算避免创建 Vector3, 达到零 GC 且速度最快
			float dx = left.PosX - right.PosX;
			float dy = left.PosY - right.PosY;
			float dz = left.PosZ - right.PosZ;
			if ((dx * dx + dy * dy + dz * dz) >= POSITION_CHANGE_THRESHOLD_SQR) {
				return false;
			}

			// 3. 根据状态比较特有数据
			switch (left.interactState) {
				case 0:
					return left.itemPrefabName == right.itemPrefabName;

				case 2:
					if (left.targetId != right.targetId) return false;

					// 同样使用 sqrMagnitude 比较拖拽点位置
					float t_dx = left.desiredPosX - right.desiredPosX;
					float t_dy = left.desiredPosY - right.desiredPosY;
					float t_dz = left.desiredPosZ - right.desiredPosZ;
					return (t_dx * t_dx + t_dy * t_dy + t_dz * t_dz) < POSITION_CHANGE_THRESHOLD_SQR;

				case 4:
					return left.targetId == right.targetId;

				default:
					return true;
			}
		}

		public static bool operator !=(HandData left, HandData right) {
			return !(left == right);
		}
	}

	public ulong playId;
	public long TimestampTicks;

	// 直接存储原始字段以保证极致性能
	public float PosX, PosY, PosZ;
	public float RotX, RotY, RotZ, RotW;

	// 建议 HandData 也改为 struct
	public HandData LeftHand;
	public HandData RightHand;

	public bool IsTeleport;
	public bool HasPhysics;
	public Vector3 WorldVelocity;
	public Vector3 AnchorOffset;
	public bool SurfaceAnchored;
	public bool MobPulled;
	public ulong GripPeer;
	public float GripLength;
	public bool HasCoordinateFrame;
	public string CoordinateLevel;
	public string CoordinateBranch;
	public int CoordinateSeed;
	public Vector3 CoordinateOrigin;
	public Quaternion CoordinateRotation;
	public float CoordinateHeight;

	public Vector3 Position {
		get => new Vector3(PosX, PosY, PosZ);
		set { PosX = value.x; PosY = value.y; PosZ = value.z; }
	}

	public Quaternion Rotation {
		get => new Quaternion(RotX, RotY, RotZ, RotW);
		set { RotX = value.x; RotY = value.y; RotZ = value.z; RotW = value.w; }
	}

	public void Serialize(DataWriter writer) {
		writer.Put(playId).Put(TimestampTicks);
		writer.Put(CheckpointGeneration);
		writer.Put(Position).Put(Rotation);
		writer.PutIn(in LeftHand).PutIn(in RightHand);
		writer.Put(IsTeleport);
		writer.Put(HasPhysics);
		if (HasPhysics) writer.Put(WorldVelocity).Put(AnchorOffset).Put(SurfaceAnchored).Put(MobPulled).Put(GripPeer).Put(GripLength);
		writer.Put(HasCoordinateFrame);
		if (HasCoordinateFrame) { writer.Put(CoordinateHeight).Put(!string.IsNullOrEmpty(CoordinateLevel)); if (!string.IsNullOrEmpty(CoordinateLevel)) writer.Put(CoordinateLevel).Put(CoordinateOrigin).Put(CoordinateRotation).Put(CoordinateBranch ?? "").Put(CoordinateSeed); }
	}

	public void Deserialize(DataReader reader) {
		playId = reader.GetULong();
		TimestampTicks = reader.GetLong();
		CheckpointGeneration = reader.GetULong();
		Position = reader.GetVector3();
		Rotation = reader.GetQuaternion();

		reader.GetRef(ref LeftHand);
		reader.GetRef(ref RightHand);

		IsTeleport = reader.GetBool();
		HasPhysics = reader.GetBool();
		WorldVelocity = Vector3.zero; AnchorOffset = Vector3.zero; SurfaceAnchored = false; MobPulled = false; GripPeer = 0; GripLength = 0;
		if (HasPhysics) { WorldVelocity = reader.GetVector3(); AnchorOffset = reader.GetVector3(); SurfaceAnchored = reader.GetBool(); MobPulled = reader.GetBool(); GripPeer = reader.GetULong(); GripLength = reader.GetFloat(); }
		HasCoordinateFrame = reader.GetBool();
		CoordinateLevel = null; CoordinateBranch = null; CoordinateSeed = 0; CoordinateOrigin = Vector3.zero; CoordinateRotation = Quaternion.identity; CoordinateHeight = 0;
		if (HasCoordinateFrame) { CoordinateHeight = reader.GetFloat(); if (reader.GetBool()) { CoordinateLevel = reader.GetString(); CoordinateOrigin = reader.GetVector3(); CoordinateRotation = reader.GetQuaternion(); CoordinateBranch = reader.GetString(); CoordinateSeed = reader.GetInt(); } }
	}

	/// <summary>
	/// 检查当前 PlayerData 与上一帧的 PlayerData 是否存在显著变化. 
	/// 使用 in 传递结构体, 避免大结构体按值传递的内存拷贝. 
	/// </summary>
	/// <param name="newData">当前帧的新数据</param>
	/// <returns>如果有显著变化, 返回 true</returns>
	public bool UpdateIfChanged(in PlayerData newData, bool forceUpdate = false) {
		// 强制更新
		if (forceUpdate) {
			this = newData; // 极速内存拷贝, TimestampTicks 也会被一并更新
			return true;
		}
		// 如果不是强制更新, 进行严格的浮点数距离计算
		float dx = PosX - newData.PosX;
		float dy = PosY - newData.PosY;
		float dz = PosZ - newData.PosZ;

		if (IsTeleport != newData.IsTeleport || HasPhysics != newData.HasPhysics || SurfaceAnchored != newData.SurfaceAnchored || MobPulled != newData.MobPulled || GripPeer != newData.GripPeer || Mathf.Abs(GripLength - newData.GripLength) > 0.01f || (WorldVelocity - newData.WorldVelocity).sqrMagnitude > 0.01f || (AnchorOffset - newData.AnchorOffset).sqrMagnitude > 0.0001f || HasCoordinateFrame != newData.HasCoordinateFrame || CoordinateLevel != newData.CoordinateLevel || CoordinateBranch != newData.CoordinateBranch || CoordinateSeed != newData.CoordinateSeed || Mathf.Abs(CoordinateHeight - newData.CoordinateHeight) > 0.01f || (CoordinateOrigin - newData.CoordinateOrigin).sqrMagnitude > 0.0001f || !IsRotationSimilar(CoordinateRotation, newData.CoordinateRotation, ROTATION_CHANGE_THRESHOLD_DEG) || (dx * dx + dy * dy + dz * dz) >= POSITION_CHANGE_THRESHOLD_SQR ||
			!IsRotationSimilar(this.Rotation, newData.Rotation, ROTATION_CHANGE_THRESHOLD_DEG) ||
			LeftHand != newData.LeftHand ||
			RightHand != newData.RightHand) {
			this = newData; // 极速内存拷贝, TimestampTicks 也会被一并更新
			return true;
		}
		return false;
	}

	/// <summary>
	/// 内部工具函数: 优化版的旋转相似性检查
	/// </summary>
	private static bool IsRotationSimilar(Quaternion a, Quaternion b, float thresholdDegrees) {
		float cosThreshold = Mathf.Cos(thresholdDegrees * Mathf.Deg2Rad * 0.5f);
		float dot = Mathf.Abs(Quaternion.Dot(a, b));
		return dot > cosThreshold;
	}
}


