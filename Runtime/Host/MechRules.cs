namespace Demech2
{
    // The mechs' rules a host may change (Host.Mechs): damage, death, heat, reload, movement. Each default is demech2's.
    public class MechRules
    {
        // ReloadPlayerMech: the configuration loaded again (p_old the one it had); then the fresh mech's state.
        public virtual MechConfig ReloadConfig(Mech p_mech, MechConfig p_old) =>
            Host.World.LoadMechConfig(p_mech.m_player, p_mech.IsLocal, SimMain.PlayerSide(p_mech.m_player.m_index), Host.World.Temperature);
        public virtual void Reloaded(Mech p_mech) { }

        // DestroyCriticalSlot when the host does it (true); a weapon kind's "Critical hit : " line when the host's
        // (true); the weapon slots a bin's weapon index may name; a sensor slot destroyed.
        public virtual bool DestroyCriticalSlot(Mech p_mech, int p_attacker, int p_section, int p_slot, bool p_recursing) => false;
        public virtual bool CriticalWeaponPart(int p_kind, out int p_part)
        {
            p_part = 0;
            return false;
        }
        public virtual int BinWeaponSlots => 10;
        public virtual void SensorsHit(Mech p_mech) { }

        // DestroyMech when the host does it (true); KillMech's kill about to be counted.
        public virtual bool DestroyMech(Mech p_mech, int p_killer) => false;
        public virtual void Killing(Mech p_mech, int p_killer, int p_side, bool p_isMech) { }

        // CalculateHeat: the ticks above 100 before the mech is destroyed; whether the critical warning comes back at p_heat.
        public virtual int OverheatKillTicks => 4525;
        public virtual bool HeatWarningRearms(int p_heat) => true;

        // UpdateMech: the torso without a twist limit; jets that stop while the mech isn't running (and it collides
        // shut down); the lift's drag scale; the mech left the ground, or is on it (p_resim: a rollback's rerun); its
        // height after the move (p_y, 16.16); the heat a MASC malfunction adds to p_heat; each tick shut down.
        public virtual bool FreeTorsoTwist(Mech p_mech) => false;
        public virtual bool ShutdownStopsJets => false;
        public virtual int LiftDrag => 0xe24;
        public virtual void Airborne(Mech p_mech) { }
        public virtual void Grounded(Mech p_mech, bool p_resim) { }
        public virtual int PinnedHeight(Mech p_mech, int p_y) => p_y;
        public virtual int MascHeat(int p_heat) => p_heat >> 2;
        public virtual void ShutDownTick(Mech p_mech) { }

        // The target info the autopilot's route follows (and PUNCH_IN_AUTO_HDG sets): the main target's.
        public virtual PlayerTargetInfo Route(Mech p_mech) => p_mech.m_targetInfo;

        // Targeting: a nav targeted (p_index); a nav's place written to the target; whether the autopilot stops on a
        // target of kind p_kind (-1: none to test).
        public virtual void RouteNavTargeted(Mech p_mech, int p_index) { }
        public virtual void RouteNavPlaced(Mech p_mech) { }
        public virtual bool AutopilotDrops(Mech p_mech, int p_kind) => p_kind != 0x100;
    }
}
