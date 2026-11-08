namespace Demech2
{
    // The objectives' rules a host may change (Host.Objectives): a team near its target, a table's slots, the
    // announcements, the missions and objectives a type 0x10000 and up acts on, the loops' counter. Each default is
    // demech2's.
    public class ObjectiveRules
    {
        // IsTeamNearTarget when the host decides (true, p_near its result).
        public virtual bool TeamNearTarget(int p_target, int p_team, out bool p_near)
        {
            p_near = false;
            return false;
        }

        // Star p_star's objective p_index (AnnounceObjective's); its announcement when the host's (true); the mission
        // result's when the host's (true).
        public virtual MissionObjective Slot(World p_world, int p_star, int p_index) => p_world.m_missions[p_star].m_objectives[p_index];
        public virtual bool Announce(MissionObjective p_objective, int p_state) => false;
        public virtual bool AnnounceMissionResult(World p_world, int p_star, int p_status) => false;

        // A slot past a mission's objectives the host keeps (true, p_state its state).
        public virtual bool SpareState(MissionTable p_mission, int p_objective, out int p_state)
        {
            p_state = 0;
            return false;
        }

        // The mission and the objective a type 0x10000 and up acts on.
        public virtual MissionTable TargetMissionOf(World p_world, MissionObjective p_objective) => Objectives.MissionOf(p_world, p_objective.m_targetStar);
        public virtual MissionObjective TargetObjectiveOf(World p_world, MissionObjective p_objective) => Objectives.TargetObjectiveOf(p_world, p_objective);

        // The loops' counter: where UpdateObjective's starts, a loop's end value (kept by the host); objective p_index's
        // announcement cleared when the host's (true).
        public virtual int LoopStart(World p_world) => 0;
        public virtual void LoopEnded(World p_world, int p_counter) { }
        public virtual bool ClearAnnounced(World p_world, int p_index) => false;

        // MissionSpeech.SayLine with text: the host's lines carry their text.
        public virtual bool LinesCarryText => false;
    }
}
