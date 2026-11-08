namespace Demech2
{
    // The AI's rules a host may change (Host.Ai): pilot personalities, the maneuvers' tables and distances, the
    // weapons' fire. Each default is demech2's.
    public class AiRules
    {
        // InitializeAI for an AI player: the personality read from its parameters (p_stage 0 before the skills are
        // scaled, 1 after).
        public virtual void InitPersonality(Mech p_mech, int p_stage) { }

        // InitializeManeuvers' end; the table ChooseManeuver draws from (p_table the type's).
        public virtual void ManeuversInitialized(Mech p_mech) { }
        public virtual Mech.ManeuverTable ManeuverTableOf(Mech p_mech, Mech.ManeuverTable p_table) => p_table;

        // stupid: the distance it closes to; whether it asks for avoid (within 4500).
        public virtual int StupidApproach(Mech p_mech) => 15000;
        public virtual bool StupidAvoids(Mech p_mech) => p_mech.m_targetInfo.m_distance < 4500;

        // ClearAIFire: the group's trigger let go too.
        public virtual bool ClearsGroupTrigger(Mech p_mech) => false;

        // RunAIWeapons when the host runs it all (true, p_fired its result); the torso's pitch on the target; the fire
        // decision when the host makes it (true; p_roll the gunnery's roll, p_fired set when it fired); the trigger
        // DecideAIFire's fire pulls.
        public virtual bool RunWeapons(Mech p_mech, int p_heading, out bool p_fired)
        {
            p_fired = false;
            return false;
        }
        public virtual bool AimPitchOk(Mech p_mech) => true;
        public virtual bool Fire(Mech p_mech, int p_roll, ref bool p_fired) => false;
        public virtual string FireKey(Mech p_mech) => "arms.fire";

        // DecideAIFire's test of the selected weapon when the host's (true, p_fire its result).
        public virtual bool DecideFire(Mech p_mech, WeaponSlot p_slot, WeaponDef p_def, ref bool p_fire) => false;
    }
}
