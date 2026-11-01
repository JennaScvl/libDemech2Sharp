namespace Demech2
{
    // An objective of a star's mission (MissionObjective), from its mission table entry (SetUpStarMission).
    public sealed class MissionObjective
    {
        public int m_state;              // 0 new, 7 waiting on its event list, 3 going, 5 successful, 6 failed, 8 failed for another star
        public int m_priority;           // 1 primary, 2 secondary, 4 tertiary, 8 return
        public int m_type;               // one bit (ai.c's g_objectiveTypeNames)
        public bool m_allConditions;     // every condition must hold, not just one
        public readonly int[] m_conditionKinds = new int[8];      // 0 ends the list; 1 done, 2 successful, 3 failed
        public readonly int[] m_conditionObjectives = new int[8];
        public readonly int[] m_conditionStars = new int[8];
        public int m_startTime = -1, m_endTime = -1; // mission seconds
        public int m_timeLimit;          // seconds (negative: none)
        public bool m_active;
        public bool m_listed;            // on the objectives panel ('V')
        public bool m_mandatory;         // ('M')
        public int m_requiredCount;      // how many targets must be done; 0 for all of them
        public int m_engagement;
        public string m_successSound = "", m_failSound = ""; // SNDS names (and KEATING files), announced when done
        public string m_eventList = "";  // the entry's m_name: the event list it waits on
        public int m_targetStar, m_targetObjective; // what the types 0x10000 and up act on
        public string m_title = "";      // m_name: the objectives panel's text (the entry's m_title)
        public int m_targetCount;
        public readonly int[] m_targets = new int[40]; // AI target ids
    }
}
