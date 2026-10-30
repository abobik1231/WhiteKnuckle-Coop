using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WKMPMod.Gameplay;

public sealed class ManualPlayerSave {
	public string Profile;
	public string Name;
	public bool Host;
	public string Session;
	public float Health;
	public float LeftGrip;
	public float RightGrip;
	public float Height;
	public float Ascent;
	public List<string> Settings;
	public string Mechanisms;
	public bool KeepLocalInventory;
	public string WorldLayout;
	public string Currency;
	public int? Roaches;
	public string Artifacts;
	public List<string> OtherArtifacts;
	public string LoadToken;
}

public sealed class ManualRunSave {
	public int Format = 1;
	public string Id;
	public string GameVersion;
	public string CreatedUtc;
	public float Height;
	public int PlayerCount;
	public string GameMode;
	public bool Chain;
	public float ChainLength;
	public string Title;
	public bool? EnemySync;
	public bool? WorldSync;
	public string Mechanisms;
	public string WorldLayout;
	public string Currency;
	public int? Roaches;
	public List<ManualPlayerSave> Players = new List<ManualPlayerSave>();
}

public static class ManualSaveStore {
	public static string Pack(string text) {
		using var memory = new MemoryStream();
		using (var gzip = new GZipStream(memory, CompressionLevel.Optimal, true))
		using (var writer = new StreamWriter(gzip, Encoding.UTF8)) writer.Write(text);
		return Convert.ToBase64String(memory.ToArray());
	}
	public static string Unpack(string packed) {
		if (packed == null || packed.Length > 900000) throw new InvalidDataException("Сохранение слишком большое.");
		using var memory = new MemoryStream(Convert.FromBase64String(packed));
		using var gzip = new GZipStream(memory, CompressionMode.Decompress);
		using var output = new MemoryStream();
		var buffer = new byte[8192];
		int count;
		while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0) {
			if (output.Length + count > 32 * 1024 * 1024) throw new InvalidDataException("Сохранение повреждено или слишком большое.");
			output.Write(buffer, 0, count);
		}
		return Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF');
	}
	public static string Write(string directory, ManualRunSave save) {
		Directory.CreateDirectory(directory);
		if (!Guid.TryParseExact(save.Id, "N", out _)) throw new InvalidDataException("Неверный номер сохранения.");
		string path = Path.Combine(directory, save.Id + ".wksave");
		string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try {
			using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
				using (var gzip = new GZipStream(file, CompressionLevel.Optimal, true))
				using (var writer = new StreamWriter(gzip, Encoding.UTF8)) writer.Write(JsonConvert.SerializeObject(save));
				file.Flush(true);
			}
			Read(temporary);
			File.Move(temporary, path);
		} finally { if (File.Exists(temporary)) File.Delete(temporary); }
		return path;
	}
	private static string ReadJson(string path) {
		using var file = File.OpenRead(path);
		if (file.Length > 32 * 1024 * 1024) throw new InvalidDataException("Сохранение слишком большое.");
		using var gzip = new GZipStream(file, CompressionMode.Decompress);
		using var output = new MemoryStream();
		var buffer = new byte[8192];
		int count;
		while ((count = gzip.Read(buffer, 0, buffer.Length)) > 0) {
			if (output.Length + count > 64 * 1024 * 1024) throw new InvalidDataException("Сохранение слишком большое.");
			output.Write(buffer, 0, count);
		}
		return Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF');
	}
	public static ManualRunSave Read(string path) {
		var save = JsonConvert.DeserializeObject<ManualRunSave>(ReadJson(path));
		if (save == null || save.Format != 1 || save.Players == null || save.Players.Count == 0 || save.Players.Count > 16) throw new InvalidDataException("Неподдерживаемое сохранение.");
		return save;
	}
	public static string Edit(string path, string title, bool chain, float length, bool enemies, bool? world = null) {
		Read(path);
		if (float.IsNaN(length) || float.IsInfinity(length) || length < 3 || length > 12) throw new InvalidDataException("Длина цепи должна быть от 3 до 12 метров.");
		using var source = new StringReader(ReadJson(path));
		using var reader = new JsonTextReader(source) { DateParseHandling = DateParseHandling.None };
		var document = JObject.Load(reader);
		document["Title"] = (title ?? "").Trim().Substring(0, Math.Min((title ?? "").Trim().Length, 80));
		document["Chain"] = chain;
		document["ChainLength"] = length;
		document["EnemySync"] = enemies;
		if (world.HasValue) document["WorldSync"] = world.Value;
		string temporary = path + ".edit-" + Guid.NewGuid().ToString("N");
		string backups = Path.Combine(Path.GetDirectoryName(path), "Backups");
		Directory.CreateDirectory(backups);
		string backup = Path.Combine(backups, Path.GetFileNameWithoutExtension(path) + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".wksave");
		try {
			using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
			using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
			using (var writer = new StreamWriter(gzip, Encoding.UTF8)) writer.Write(document.ToString(Formatting.None));
			Read(temporary);
			File.Replace(temporary, path, backup);
		} finally { if (File.Exists(temporary)) File.Delete(temporary); }
		return backup;
	}
	public static string MoveToDeleted(string path, bool confirmed) {
		if (!confirmed) return null;
		Read(path);
		string folder = Path.Combine(Path.GetDirectoryName(path), "Deleted");
		Directory.CreateDirectory(folder);
		string destination = Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "-" + Guid.NewGuid().ToString("N") + ".wksave");
		File.Move(path, destination);
		return destination;
	}
}
