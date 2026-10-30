using System;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using WKMPMod.Component;
using WKMPMod.UI;

namespace WKMPMod.Gameplay;

public sealed class ItemDescriptions : MonoBehaviour {
	private float nextRay;
	private static readonly RaycastHit[] hits = new RaycastHit[24];
	private static readonly System.Collections.Generic.Dictionary<string, string> buffNames = new System.Collections.Generic.Dictionary<string, string> { { "addSpeed", "Скорость перемещения" }, { "addClimb", "Скорость лазания" }, { "addJump", "Высота прыжка" }, { "addStaminaRegen", "Восстановление выносливости" }, { "addGripStrength", "Максимальная выносливость" }, { "addGravity", "Гравитация" }, { "damageResist", "Сопротивление урону" }, { "damageMult", "Получаемый урон" }, { "addReach", "Дальность захвата" } };
	private static readonly System.Collections.Generic.Dictionary<string, string> artifacts = new System.Collections.Generic.Dictionary<string, string> {
		{ "Item_Artifact_EVAGlove", "Удерживай кнопку руки для активации. Снижает гравитацию на 50%, пока действует эффект. Для запуска нужно больше 10 единиц заряда; активная перчатка расходует заряд. Восстанавливает заряд, когда этой рукой не держишься за поверхность." },
		{ "Item_Artifact_Rapier", "Оружие ближнего боя. Удерживай кнопку руки для подготовки атаки и отпусти для рывка в направлении взгляда. При подходящем столкновении во время рывка позволяет оттолкнуться от поверхности или противника. Заряженная атака расходует запас артефакта." },
		{ "Item_Artifact_Remote", "Удерживай кнопку руки, чтобы выбрать точку, и отпусти для создания зацепа на расстоянии до 5 м. Вмещает 4 заряда. Один заряд восстанавливается за 20 секунд." },
		{ "Item_Artifact_Rebar_Return", "Метательная арматура, которую можно вернуть. После броска наведи пустую руку на арматуру, зажми кнопку этой руки и отпусти, чтобы притянуть её обратно." },
		{ "Item_Artifact_Translocator", "Удерживай кнопку руки для выбора точки телепортации и отпусти, чтобы переместиться. Дальность до 20 м, расходуется заряд. Может телепортировать в подходящих противников. При исчерпании зарядов сам предмет не разрушается." },
		{ "Item_Artifact_Timepiece", "Удерживай кнопку руки, чтобы отматывать свой путь назад. Во время перемотки восстанавливает здоровье и выносливость. Запас истории ограничен 30 секундами; для начала перемотки нужен накопленный запас времени и завершённая перезарядка." }
	};
	public static string Description(Item_Object item) {
		if (item?.itemData == null) return "";
		string description = artifacts.TryGetValue(item.itemData.prefabName, out var artifact) ? artifact : CL_LocalizationManager.currentLocalization != null ? CL_LocalizationManager.GetLocalization("items", "desc-" + item.itemData.prefabName) : "";
		if (string.IsNullOrWhiteSpace(description)) description = BasicDescription(item.itemData);
		string title = RussianGame.Translate(item.itemData.itemName);
		return string.IsNullOrWhiteSpace(description) ? title : "<b>" + title + "</b>\n" + RussianGame.Translate(description);
	}
	private static string BasicDescription(Item item) {
		var hand = item.handItemAsset;
		if (hand is HandItem_Food food) return "Еда. Восстанавливает здоровье: " + food.healAmount.ToString("0.##") + ". Восстанавливает выносливость: " + food.gripAmountAdded.ToString("0.##") + "." + BuffDescription(food.useBuff ? food.buff : null);
		if (hand is HandItem_Buff buff) return "Расходуемый предмет." + (buff.healAmount > 0 ? " Восстанавливает здоровье: " + buff.healAmount.ToString("0.##") + "." : "") + BuffDescription(buff.buff);
		if (item.HasTag("flashlight")) return "Освещает окружение. Нажимай кнопку руки, чтобы восстановить заряд ручным генератором.";
		if (item.HasTag("hammer")) return "Инструмент и оружие ближнего боя. Забивает установленные крюки и разбивает подходящие препятствия.";
		if (item.HasTag("piton")) return "Создаёт точку захвата на подходящей поверхности.";
		if (item.HasTag("rope")) return "Метательная арматура с верёвкой, по которой можно подниматься.";
		if (item.HasTag("explosive")) return "Взрывоопасный метательный предмет. Взрыв может повредить существ и разрушаемые объекты.";
		if (item.HasTag("rebar")) return "Метательная арматура. Её можно использовать как оружие и закрепить в подходящей поверхности.";
		if (item.HasTag("currency")) return "Валюта для покупок. В коопе учитывается в общем запасе игроков.";
		if (item.HasTag("ammo")) return "Боеприпасы для подходящего оружия.";
		if (item.HasTag("disk")) return "Диск для терминалов. Вместимость: " + item.GetFirstDataStringByType("capacity") + ".";
		if (item.HasTag("note")) return "Записка с текстовой записью.";
		if (item.HasTag("junk")) return "Пустая упаковка или использованный предмет.";
		if (item.HasTag("gun")) return "Стрелковое оружие." + (string.IsNullOrEmpty(item.GetFirstDataStringByType("ammo")) ? "" : " Боеприпасов: " + item.GetFirstDataStringByType("ammo").Trim() + ".");
		return "";
	}
	private static string BuffDescription(BuffContainer buff) {
		if (buff == null) return "";
		if (!string.IsNullOrWhiteSpace(buff.desc)) return "\n" + RussianGame.Translate(buff.desc);
		return buff.buffs == null ? "" : string.Concat(buff.buffs.Where(value => buffNames.ContainsKey(value.id)).Select(value => "\n" + buffNames[value.id] + ": " + (value.maxAmount >= 0 ? "+" : "") + (value.maxAmount * 100).ToString("0.##") + "%"));
	}
	public static void Show(Item_Object item) {
		if (item?.itemData == null || item.GetComponentInParent<RemoteHand>(true) != null || item.GetComponentInParent<RPContainerRef>(true) != null || CL_UIManager.instance == null) return;
		string text = Description(item);
		var label = CL_UIManager.instance.descriptorText;
		if (label != null) { label.enableWordWrapping = true; label.enableAutoSizing = true; label.fontSizeMin = 12; label.fontSizeMax = 20; }
		if (!string.IsNullOrEmpty(text)) CL_UIManager.instance.SetDescription(text);
	}
	private void Update() {
		if (CL_GameManager.gMan == null || CL_GameManager.gMan.isPaused || ManualSaves.IsOpen || LanMenu.IsOpen || ManualSaves.Loading || Time.unscaledTime < nextRay) return;
		nextRay = Time.unscaledTime + .06f;
		var player = ENT_Player.GetPlayer(); if (player == null || player.IsDead()) return;
		var camera = Inventory.IsShowingInventory() ? player.inventoryCam : Camera.main; if (camera == null) return;
		Vector3 point = Cursor.lockState == CursorLockMode.Locked ? new Vector3(Screen.width * .5f, Screen.height * .5f, 0) : Input.mousePosition;
		var ray = camera.ScreenPointToRay(point);
		int count = Physics.RaycastNonAlloc(ray, hits, Inventory.IsShowingInventory() ? 20 : 7, ~0, QueryTriggerInteraction.Ignore);
		Array.Sort(hits, 0, count, HitComparer.Instance);
		for (int i = 0; i < count; i++) {
			var item = hits[i].collider.GetComponentInParent<Item_Object>();
			if (item == null) { if (!Inventory.IsShowingInventory() && !hits[i].transform.IsChildOf(player.transform)) return; continue; }
			Show(item); return;
		}
	}
	private sealed class HitComparer : System.Collections.Generic.IComparer<RaycastHit> { public static readonly HitComparer Instance = new HitComparer(); public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance); }
}

[HarmonyPatch(typeof(Item_Object), nameof(Item_Object.OnHover))]
public static class Patch_ItemDescriptionHover { private static void Postfix(Item_Object __instance) { ItemDescriptions.Show(__instance); } }
