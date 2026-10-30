using System;

namespace WKMPMod.Gameplay;

public sealed class GrabLease {
	public ulong Mob { get; private set; }
	public ulong Victim { get; private set; }
	public double Expires { get; private set; }
	public bool Expire(double now) { if (Mob == 0 || now < Expires) return false; Clear(); return true; }
	public bool Acquire(ulong mob, ulong victim, double now) {
		if (mob == 0 || victim == 0 || !double.IsFinite(now)) return false;
		Expire(now);
		if (Mob != 0 && (Mob != mob || Victim != victim)) return false;
		Mob = mob; Victim = victim; Expires = now + 1.25; return true;
	}
	public bool Release(ulong mob, ulong victim) { if (Mob != mob || Victim != victim || mob == 0) return false; Clear(); return true; }
	public void Clear() { Mob = 0; Victim = 0; Expires = 0; }
}
