using UnityEngine;

namespace WKMPMod.Component;

public class NetworkedEnemy : MonoBehaviour {
	private const float PositionEpsilonSqr = 0.01f; // 位置变化阈值平方 (0.1m)
	private const float RotationEpsilonDegrees = 1.0f; // 旋转变化阈值 (度)
	private const float HealthEpsilon = 0.01f; // 生命值变化阈值

	public ulong networkId;
	public bool ServerAssigned;
	public float lastHealth = float.NaN;
	public Vector3 lastPosition;
	public Quaternion lastRotation;
	private float refreshAt;
	public float SendInterval;
	private float lastSent = float.NegativeInfinity;
	public float LastSentTime => lastSent;
	public bool ReadyToSend => Time.unscaledTime - lastSent >= Mathf.Max(0, SendInterval - 0.005f);
	private bool remote;
	public float LastRemoteUpdate { get; private set; }
	private Vector3 target;
	private Quaternion targetRotation;
	private Vector3 smoothVelocity;
	private Vector3 remoteVelocity;
	private Vector3 previousSample;
	private Rigidbody remoteRigid;
	private bool previousKinematic;
	private Animator animator;
	private float nextAnimatorSearch;
	private float firstSeen;
	private Renderer[] hiddenRenderers;
	private Collider[] hiddenColliders;
	private bool[] rendererStates;
	private bool[] colliderStates;
	public Animator CachedAnimator {
		get {
			if (animator == null && Time.unscaledTime >= nextAnimatorSearch) { animator = GetComponentInChildren<Animator>(); nextAnimatorSearch = Time.unscaledTime + 3f; }
			return animator;
		}
	}

	// 缓存组件, 避免频繁 GetComponent / 反射
	public GameEntity Entity { get; private set; }

	private void Awake() {
		firstSeen = Time.unscaledTime;
		Entity = GetComponent<GameEntity>();
		lastPosition = transform.position;
		lastRotation = transform.rotation;
		lastHealth = currentHealth;
	}

	/// <summary>
	/// 获取当前实体生命值
	/// </summary>
	public float currentHealth {
		get {
			if (Entity == null) return float.NaN;
			return Entity.health; // 如果原版 health 是私有的, 可在此处统一使用 AccessTools 读取
		}
		set {
			if (Entity != null) Entity.health = value;
		}
	}

	/// <summary>
	/// 检查敌人状态是否有足够明显的变化需要同步
	/// </summary>
	public bool HasMeaningfulChange() {
		if (Entity is DEN_Hunter || Time.unscaledTime >= refreshAt) return true;
		// 位置变化
		if ((transform.position - lastPosition).sqrMagnitude > PositionEpsilonSqr) return true;

		// 旋转变化
		if (Quaternion.Angle(transform.rotation, lastRotation) > RotationEpsilonDegrees) return true;

		// 生命值变化
		float hp = currentHealth;
		if (float.IsNaN(hp) != float.IsNaN(lastHealth)) return true;
		if (!float.IsNaN(hp) && Mathf.Abs(hp - lastHealth) > HealthEpsilon) return true;

		return false;
	}

	/// <summary>
	/// 检查敌人是否已被移除/死亡
	/// </summary>
	public bool IsRemoved() {
		if (this == null || gameObject == null || !gameObject.activeInHierarchy) return true;
		float hp = currentHealth;
		return Entity != null && Entity.dead || !float.IsNaN(hp) && Entity != null && Entity.maxHealth > 0 && hp <= 0f;
	}

	/// <summary>
	/// 刷新上次同步的快照状态
	/// </summary>
	public void RememberState() {
		lastSent = Time.unscaledTime;
		refreshAt = Time.unscaledTime + 2f;
		lastPosition = transform.position;
		lastRotation = transform.rotation;
		lastHealth = currentHealth;
	}

	/// <summary>
	/// 应用网络发来的远程状态
	/// </summary>
	public void ApplyRemoteState(Vector3 position, Quaternion rotation, float health) {
		ShowUnmatched();
		float elapsed = Time.unscaledTime - LastRemoteUpdate;
		remoteVelocity = remote && elapsed > 0.015f && elapsed < 0.6f && (position - previousSample).sqrMagnitude < 225 ? Vector3.ClampMagnitude((position - previousSample) / elapsed, 50) : Vector3.zero;
		previousSample = position;
		LastRemoteUpdate = Time.unscaledTime;
		if (!remote || (transform.position - position).sqrMagnitude > 225) transform.SetPositionAndRotation(position, rotation);
		if (!remote) { remoteRigid = GetComponent<Rigidbody>(); if (remoteRigid != null) previousKinematic = remoteRigid.isKinematic; }
		remote = true;
		target = position;
		targetRotation = rotation;
		if (remoteRigid != null && !remoteRigid.isKinematic) remoteRigid.isKinematic = true;
		if (!float.IsNaN(health)) {
			currentHealth = health;
		}
		if (!gameObject.activeSelf) gameObject.SetActive(true);
		RememberState();
	}
	private void LateUpdate() {
		if (!WKMPMod.Core.MPCore.IsInLobby || !WKMPMod.World.EnemySyncModule.Instance.IsEnabled) ShowUnmatched();
		else if (WKMPMod.NetWork.MPSteamworks.Instance.IsLan && !WKMPMod.NetWork.MPSteamworks.IsHost && !ServerAssigned && Time.unscaledTime - firstSeen > 5 && hiddenRenderers == null) {
			hiddenRenderers = GetComponentsInChildren<Renderer>(true); rendererStates = new bool[hiddenRenderers.Length];
			for (int i = 0; i < hiddenRenderers.Length; i++) { rendererStates[i] = hiddenRenderers[i].enabled; hiddenRenderers[i].enabled = false; }
			hiddenColliders = GetComponentsInChildren<Collider>(true); colliderStates = new bool[hiddenColliders.Length];
			for (int i = 0; i < hiddenColliders.Length; i++) { colliderStates[i] = hiddenColliders[i].enabled; hiddenColliders[i].enabled = false; }
		}
		if (remote && (!WKMPMod.Core.MPCore.IsInLobby || !WKMPMod.World.EnemySyncModule.Instance.IsEnabled || Time.unscaledTime - LastRemoteUpdate > 5f)) { if (remoteRigid != null) remoteRigid.isKinematic = previousKinematic; remote = false; ServerAssigned = false; }
		if (!remote || !WKMPMod.Core.MPCore.CanSync || WKMPMod.NetWork.MPSteamworks.IsHost) return;
		float age = Time.unscaledTime - LastRemoteUpdate;
		Vector3 predicted = target + (age < 0.25f ? Vector3.ClampMagnitude(remoteVelocity * Mathf.Clamp(age, 0, 0.1f), 0.75f) : Vector3.zero);
		transform.position = Vector3.SmoothDamp(transform.position, predicted, ref smoothVelocity, 0.045f, float.MaxValue, Time.unscaledDeltaTime);
		transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 1 - Mathf.Exp(-25 * Time.unscaledDeltaTime));
	}
	private void ShowUnmatched() {
		if (hiddenRenderers == null) return;
		for (int i = 0; i < hiddenRenderers.Length; i++) if (hiddenRenderers[i] != null) hiddenRenderers[i].enabled = rendererStates[i];
		for (int i = 0; i < hiddenColliders.Length; i++) if (hiddenColliders[i] != null) hiddenColliders[i].enabled = colliderStates[i];
		hiddenRenderers = null; hiddenColliders = null;
	}
}

