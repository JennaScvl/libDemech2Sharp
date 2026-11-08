using UnityEngine;

namespace Demech2
{
    // What the sim asks of the game's world and records: the career record (the local player's), the scene's static
    // objects behind the game things, the planet's settings, the squad's speech, Mech Soccer's ball.
    public interface IWorldHost
    {
        // ---- g_careerRecord (the game's): a shot fired (the local player's, its star's); a hit on p_victim (by the
        // local player, its team; on them) by g_lastHitShooter; a kill (killer, victim); a game thing's kill by
        // p_shooter of a thing on p_side; the mission's outcome; a kill counted by side (p_direct: the local player's
        // own); a wingman lost (or ejected); the kills table and the network winner (ChooseNetworkWinner). ----
        void CountShotFired(bool p_local, bool p_star);
        void CountHit(int p_victim);
        void CountKill(int p_killer, int p_victim);
        void CountThingKill(int p_shooter, int p_side);
        void SetOutcome(int p_outcome);
        void CountKillBySide(bool p_direct, bool p_mech, int p_side);
        void CountWingmanLost(bool p_ejected);
        int[,] CareerKills { get; }
        int CareerWinner { set; }

        // g_killCount (the local view's).
        int KillCount { get; set; }

        // LoadMechConfig (mekfile.c) for p_player: the local one's when p_local, else of side p_side; null without its
        // file.
        MechConfig LoadMechConfig(Player p_player, bool p_local, int p_side, int p_temperature);

        // Not in the original: Mech Soccer's start countdown holds every mech's power.
        bool PowerLocked { get; }

        // ---- world.c: the planet's settings ----
        int Temperature { get; }
        int JumpJetDrag { get; }
        int SlideSlope { get; }
        bool HostileAtmosphere { get; }

        // ---- geocache.c: the static objects behind the game things ----

        // GetStaticObjectPosition of thing p_index's object (its shape's center, else its object's place, units;
        // zeros once destroyed); its scene object (GetStaticSceneObject) or null; its shape (GetStaticObjectShape) or
        // null once destroyed; a thing still standing and its object's place.
        Vector3Int ThingPosition(int p_index);
        Transform ThingObject(int p_index);
        Shape ThingShape(int p_index);
        bool ThingStanding(int p_index, out Vector3Int p_at);

        // DestroyThingObject (KillGameThing's): the thing's object destroyed and its replacement loaded where it
        // stood. Not the original's: a tunnel door's thing takes no damage.
        void DestroyThingObject(int p_index);
        bool IsTunnelDoor(int p_index);

        // ---- objective.c's ends: the mission resolved once its speech is done; the inert-mech diagnosis log ----
        bool MissionResolvable { get; }
        void ResolveMission();
        bool InertDiagEnabled { get; }
        void DebugDumpMechs(string p_when);

        // ---- The squad's speech: the game's line for a lancemate report or event (true when it said one) ----
        bool SquadEventSpeech(int p_event, int p_slot);

        // ---- Not in the original: Mech Soccer's ball takes a shot's segment, and a splash pushes it ----
        bool SoccerActive { get; }
        bool ShotHitsBall(Shot p_shot, FixVec p_from, FixVec p_to, out FixVec p_at, out FixVec p_back);
        void SplashBall(FixVec p_at, long p_radius, int p_rate);

        // A player shut down out of the radar (not transmitting).
        bool HiddenShutDown(Player p_player);
    }
}
