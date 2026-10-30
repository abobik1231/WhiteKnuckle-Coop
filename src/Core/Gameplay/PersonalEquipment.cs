using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using WKMPMod.Core;
using WKMPMod.NetWork;
using WKMPMod.UI;
using WKMPMod.World;

namespace WKMPMod.Gameplay;

public sealed class PersonalEquipment : MonoBehaviour {
	private sealed class Snapshot { public string Inventory; public string Artifacts; }
	private static string live;
	private static int seed;
	private float next;
	private float sceneReady;
	private void Awake() { SceneManager.sceneLoaded += SceneLoaded; sceneReady = Time.unscaledTime + 15; }
	private void OnDestroy() { SceneManager.sceneLoaded -= SceneLoaded; }
	private void SceneLoaded(Scene scene, LoadSceneMode mode) { sceneReady = Time.unscaledTime + 15; }
	private static string FileFor(int worldSeed) => Path.Combine(Path.GetDirectoryName(ManualSaves.SaveDirectory), "Equipment", ManualSaves.ProfileId + "." + worldSeed + ".json");
	public static void Capture() {
		var player = ENT_Player.GetPlayer(); if (player == null || player.IsDead() || Inventory.instance == null || ManualSaves.Loading || !WorldLoader.isLoaded || DroppedItemModule.Instance.PendingPickups) return;
		try {
			var inventory = Inventory.instance; inventory.RescanInventory(); var pouches = inventory.GetAllPouches();
			Item Copy(Item item) { if (item == null) return null; item.Save(); return item.GetClone(); }
			var data = new ENT_Player.SaveData { inventory = inventory.bagItems.Where(item => !pouches.Any(pouch => pouch?.pouchItems?.Contains(item) == true)).Select(Copy).ToArray(), handInventory = inventory.itemHands.Select(hand => Copy(hand.currentItem)).ToArray() };
			(data.pouchInventory, data.pocketInventory) = inventory.SavePouches(); seed = WorldLoader.instance.seed;
			live = JsonConvert.SerializeObject(new Snapshot { Inventory = JsonUtility.ToJson(data), Artifacts = BoundArtifacts.Capture() });
			string file = FileFor(seed); Directory.CreateDirectory(Path.GetDirectoryName(file)); string temp = file + ".tmp"; File.WriteAllText(temp, live); if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
		} catch (Exception ex) { MPMain.LogWarning("[Equipment] Snapshot: " + ex.Message); }
	}
	private void Update() { if (!MPCore.IsInLobby || !MPSteamworks.Instance.IsLan || !MPCore.IsInitialized || ManualSaves.GroupLoading || Time.unscaledTime < next || Time.unscaledTime < sceneReady) return; next = Time.unscaledTime + 3; Capture(); }
	public static string ApplyFallback(CL_SaveManager.SessionSave target, CL_SaveManager.SessionSave current) {
		Snapshot snapshot = null;
		try { string file = FileFor(target.worldSeed); string json = seed == target.worldSeed ? live : File.Exists(file) && new FileInfo(file).Length < 2000000 ? File.ReadAllText(file) : null; if (json != null) snapshot = JsonConvert.DeserializeObject<Snapshot>(json); } catch (Exception ex) { MPMain.LogWarning("[Equipment] Recovery: " + ex.Message); }
		var source = snapshot != null ? JsonUtility.FromJson<ENT_Player.SaveData>(snapshot.Inventory) : current.playerSave.playerData;
		target.playerSave.playerData.inventory = source.inventory; target.playerSave.playerData.handInventory = source.handInventory; target.playerSave.playerData.pouchInventory = source.pouchInventory; target.playerSave.playerData.pocketInventory = source.pocketInventory;
		return snapshot?.Artifacts ?? BoundArtifacts.Capture();
	}
}
