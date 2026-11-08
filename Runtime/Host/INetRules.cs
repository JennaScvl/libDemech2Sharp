namespace Demech2
{
    // A networked match (the game's), as the sim's branches ask it.
    public interface INetRules
    {
        // A match's rules (a live match, or the replay of one's recording): no machine's own perspective.
        bool NetRules { get; }

        // A live match.
        bool Active { get; }

        // A client: the host's mech state is final.
        bool FollowsHost { get; }

        // Damage is applied on this machine (the host's, or a reported hit replayed); a regenerated mech's spawn
        // protection; a hit's report bracketing (BeginDamage true for the outermost).
        bool DamagesHere { get; }
        bool SpawnProtected(Mech p_mech);
        bool BeginDamage();
        void MechDamaged(Mech p_victim, int p_attacker, int p_damage, int p_section);
        void EndDamage();

        // The mech's volleys come from the host's reports (a remote mech), and one reported as fired.
        bool FiresByEvent(Mech p_mech);
        void Fired(Mech p_mech, int p_slot, int p_target, int p_targetKind, int p_age = 0);

        // The host's rewound hits: the mechs as they stood p_ticksAgo for p_shooter's shot (whether they moved), and
        // back.
        bool RewindMechs(int p_ticksAgo, int p_shooter);
        void RestoreMechs();

        // A shot's heat on a mech, a game thing's damage, reported; a match without teams (ChooseNetworkWinner's
        // deathmatch); a success sent to the others (SendSuccessMsg).
        void Heated(Mech p_victim, int p_heat);
        void ThingDamaged(int p_owner, int p_thing, int p_kind, int p_damage, FixVec p_at);
        bool FreeForAll { get; }
        void SendSuccessMsg();

        // The player a reported target names; a PPC hit's lock jam reported.
        int PlayerOfNetTarget(int p_netId);
        void LockJam(Mech p_victim, int p_ticks);
    }
}
