namespace Demech2
{
    // The weapons' rules a host may change (Host.Weapons): the table, the slots and groups, the fire keys, the shots'
    // launch and ammunition, the lock. Each default is demech2's.
    public class WeaponRules
    {
        // The weapon table in use; the def whose assets (sounds, animations) the running game shows for p_type.
        public virtual WeaponDef[] Defs => WeaponDef.Table;
        public virtual WeaponDef Local(int p_type) => WeaponDef.Of(p_type);

        // The select functions' test.
        public virtual bool Selectable(in WeaponDef p_def) => true;

        // A mech's weapon slots, and its fire groups.
        public virtual int SlotCount => 10;
        public virtual int GroupCount => 3;

        // A weapon's ammunition (-1 unlimited).
        public virtual int AmmoOf(MechConfig p_config, WeaponSlot p_slot) => p_slot.m_ammo;

        // UpdateWeaponFireState: fire keys held besides the base game's; the trigger left alone while the fire button
        // would become the group's; fire keys the host handles (p_stage 0 after the group's key, 1 after group 3's;
        // true when it took one); the group's fire.
        public virtual bool ExtraFireHeld(Mech p_mech) => false;
        public virtual bool KeepsFireTrigger(Mech p_mech) => false;
        public virtual bool FireKeys(Mech p_mech, int p_stage) => false;
        public virtual void FireGroup(Mech p_mech) => Mech.FireWeaponGroup(p_mech);

        // Each weapon's tick before its state runs.
        public virtual void SlotTick(Mech p_mech, WeaponSlot p_slot, WeaponDef p_def) { }

        // A reported volley's ticks begun elsewhere, still to run (kept, and taken once).
        public virtual void SetCatchUp(WeaponSlot p_slot, int p_ticks) { }
        public virtual int TakeCatchUp(WeaponSlot p_slot) => 0;

        // A volley's shot launched (p_first: the volley's first): the shots it took, 0 when the weapon can't fire again.
        public virtual int SpawnShot(Mech p_mech, WeaponSlot p_slot, WeaponDef p_def, bool p_first) => p_mech.SpawnShot(p_slot) ? 1 : 0;

        // The launch's sounds when the host plays them (true; p_first cleared once the volley's first sound went).
        public virtual bool LaunchSounds(Mech p_mech, WeaponSlot p_slot, WeaponDef p_def, ref bool p_first) => false;

        // JettisonAmmo when the host does it (true, p_jettisoned its result).
        public virtual bool Jettison(Mech p_mech, out bool p_jettisoned)
        {
            p_jettisoned = false;
            return false;
        }

        // The configuration's groups arranged when it's loaded.
        public virtual void ArrangeGroups(Mech p_mech) { }

        // UpdateWeaponLock: the lock kept as it is this tick (true); a target that locks at once.
        public virtual bool HoldsLock(Mech p_mech) => false;
        public virtual bool InstantLock(Mech p_mech, int p_target) => false;

        // A launched shot's additions; whether it launches spread (a missile: types 3 and 4).
        public virtual void ShotLaunched(Mech p_mech, Shot p_shot, WeaponDef p_def) { }
        public virtual bool LaunchesSpread(Shot p_shot) => p_shot.m_type == 3 || p_shot.m_type == 4;

        // LateUpdateMech: after the fire state ran (running); the group cycle key left to the fire state this tick.
        public virtual void AfterFireState(Mech p_mech) { }
        public virtual bool KeepsGroupCycle(Mech p_mech) => false;

        // IsOutOfAmmo's test of a weapon: ammunition left.
        public virtual bool Loaded(MechConfig p_config, WeaponSlot p_slot) => p_slot.m_ammo != 0;

        // ---- The shots ----

        // A missile type besides 3 and 4 (Shots.IsMissile); a slot's host data reset (ResetShotSlot).
        public virtual bool IsMissile(int p_type) => false;
        public virtual void ResetShot(Shot p_shot) { }

        // UpdateShot: the height (16.16) a shot below the ground p_ground (16.16) stops at; a hit on a mech (p_mech may
        // be null) before its damage; the heat's and damage's moment where the damage is applied (DamagesHere).
        public virtual long ShotGroundStop(long p_ground) => p_ground;
        public virtual void ShotHitMech(Shot p_shot, Mech p_mech, int p_victim) { }
        public virtual void ShotHeatedMech(Shot p_shot, Mech p_mech) { }

        // GuideMissileToTarget: the distance to the target (p_d, 16.16 units); the aim (p_dir, the unit direction);
        // the steering when the host's: 0 not (the turn d - v), 1 steered, 2 steered and the guide stops here.
        public virtual long MissileDistance(Shot p_shot, FixVec p_d) => p_d.ApproxLength;
        public virtual FixVec MissileAim(Shot p_shot, FixVec p_dir, long p_distance) => p_dir;
        public virtual int SteerMissile(Shot p_shot, FixVec p_dir, FixVec p_velocity, int p_dt) => 0;

        // DetonateShot: an impact with an explosion's sound besides a guided weapon's.
        public virtual bool ExplodesAsMissile(Shot p_shot) => false;

        // GameKeys: FIRE_CYCLE_WEAPON_GROUP fires and cycles the group; TOGGLE_AMS toggles the AMS.
        public virtual bool FireCyclesGroup => false;
        public virtual bool TogglesAms => false;
    }
}
