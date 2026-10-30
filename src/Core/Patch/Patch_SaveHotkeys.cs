using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WKMPMod.Patch;

[HarmonyPatch(typeof(CL_UIManager), "Update")]
public static class Patch_TimerHotkey {
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Swap(instructions, KeyCode.F1, KeyCode.F9);
	internal static IEnumerable<CodeInstruction> Swap(IEnumerable<CodeInstruction> instructions, KeyCode from, KeyCode to) {
		var code = new List<CodeInstruction>(instructions);
		var getKey = AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) });
		for (int i = 0; i + 1 < code.Count; i++) if (code[i].LoadsConstant((long)from) && code[i + 1].Calls(getKey)) { code[i].opcode = OpCodes.Ldc_I4; code[i].operand = (int)to; }
		foreach (var instruction in code) if (instruction.Calls(getKey)) instruction.operand = AccessTools.Method(typeof(WKMPMod.UI.SaveBindings), nameof(WKMPMod.UI.SaveBindings.NativeKeyDown));
		return code;
	}
}

[HarmonyPatch(typeof(ENT_Player), "LateUpdate")]
public static class Patch_InventoryHotkey {
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Patch_TimerHotkey.Swap(Patch_TimerHotkey.Swap(instructions, KeyCode.F2, KeyCode.F5), KeyCode.F4, KeyCode.F10);
}
