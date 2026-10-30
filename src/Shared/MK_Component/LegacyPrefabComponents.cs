using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace WKMPMod.MK_Component {
	public class MK_RemotePlayer : MonoBehaviour {
		public ulong playerId;
		public float teleportThreshold = 50f;
		public float maxSmoothDistance = 10f;
	}
	public class MK_RemoteProp : MonoBehaviour { public MK_RemotePlayer entity; }
	public class MK_ENT_Hitbox : MonoBehaviour {
		public MK_RemotePlayer entity;
		public List<string> passTags = new List<string>();
		public List<DamageEffectData> damageEffects = new List<DamageEffectData>();
	}
	[System.Serializable]
	public class DamageEffectData {
		public string id;
		public List<string> requiredTags;
		public float damageMultiplier;
		public UnityEvent damageEvent;
	}
}

namespace WKMPMod.Components {
	public class RemoteTag : MonoBehaviour {
		public ulong PlayerId;
		public string PlayerName = "";
		public string _message = "";
	}
	public class LookAt : MonoBehaviour {
		public bool maintainScreenSize = true;
		public float baseScale = 0.1f;
		public float userScale = 1f;
	}
	public class SimpleArmIK : MonoBehaviour {
		public Transform target;
		public float originalLength = 1f;
		public float minScale = 0.1f;
		public float maxScale = 10f;
	}
}
