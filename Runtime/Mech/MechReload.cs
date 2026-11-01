using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // NetMech's regenerate (demech2 MW2/src/mechreload.c: ReloadPlayerMech): the mech as it stood at the start, put back
    // (its objects' tree is the game's: RememberMechSegments, RestoreMechSegments through m_host). Not in the original: a
    // MekNet match's rollback (the move run again from the host's state: g_resimulating) and the host's reported heat.
    public sealed partial class Mech
    {
        // A client's regenerated mech: its power-up timer doesn't run it (no moves the host would roll back); it waits
        // for the host's state to say it's running (ApplyNetState).
        public bool m_awaitHostPowerUp;

        // ReloadPlayerMech (mechreload.c, 0x1007fbe0) with p_force: the mech stopped and its steering cleared, its
        // configuration loaded again (Mercenaries: its fire groups kept, MechConfig.NetRespawnKeepsGroups), its tree as
        // remembered at the start (its place and heading with it), the player's flags cleared of destroyed (~6, ~0x4001) and
        // hidden as shut down (0x10), its star's mission restarted (RestartStarMission), its motion reset (ResetMotion),
        // then FirstMech. The local player's cockpit panels reset (ResetCockpitPanels), and it isn't lost or destroyed.
        // Not g_mechPoweredUp: the damage is the host's for everyone (NetMatchDriver), so it isn't cleared for the
        // local mech's restart.
        // Not the original's: p_spawn, another mech's remembered start to reload at (a MekNet free-for-all's random spawn).
        public void ReloadPlayerMech(Mech p_spawn = null)
        {
            if (!m_host.HasRememberedStart || !NetMoves) return;
            m_speed.m_target = m_speed.m_value = 0;
            m_torsoTwist.m_target = m_torsoTwist.m_value = 0;
            m_turnRate.m_target = m_turnRate.m_value = 0;
            m_torsoPitch.m_target = m_torsoPitch.m_value = 0;
            m_steering["drive.throttle"] = 0;
            m_steering["drive.throttle.hold"] = 1;
            foreach (string sink in new[]
                     {
                         "jets.on", "jets.left", "jets.right", "jets.fore",
                         "jets.aft", "arms.fire", "arms.next", "drive.steer.dec", "drive.steer.inc",
                         "reverse", "nav.next", "mech.autopilot",
                     })
            {
                m_steering[sink] = 0;
            }

            // InitMechArrays, LoadMechConfig from the remembered name, id and configuration (the gpspec's).
            MechConfig old = m_config;
#if UNITY_EDITOR || MEKENGINE_MERCS
            MechConfig.NetRespawnKeepsGroups = WeaponDef.Mercs;
            try { m_config = Host.World.LoadMechConfig(m_player, IsLocal, SimMain.PlayerSide(m_player.m_index), Host.World.Temperature); }
            finally { MechConfig.NetRespawnKeepsGroups = false; }
            // The groups aren't set: the slots' own carried over (a fresh config's object has none).
            if (WeaponDef.Mercs && old != null && m_config != null)
                for (int i = 0; i < m_config.m_weapons.Length && i < old.m_weapons.Length; i++) m_config.m_weapons[i].m_group = old.m_weapons[i].m_group;
#else
            Config = Host.World.LoadMechConfig(player, IsLocal, SimMain.PlayerSide(player.m_index), Host.World.Temperature);
#endif
            AutoGroupWeapons();
            m_narced = false;
            m_topSpeed = m_config != null ? m_config.m_topSpeed : 0;
            m_jumpFuel = m_config != null ? m_config.m_jumpFuel : -2;
            m_jumpJets = m_config != null ? m_config.m_jumpJets : 0;
            m_jumpThrust = m_config != null ? m_config.m_jumpThrust : 0;
            m_cooling = m_config != null ? m_config.m_cooling : 0;
            m_flags = m_config != null ? m_config.m_flags : 0;
            m_aiValue = m_config != null ? m_config.m_value : 0;
            m_pendingNetHeat = 0;

            m_host.RestoreMechSegments();
            m_host.GetRememberedStart(p_spawn, out m_positionX, out m_positionY, out m_positionZ, out m_heading);
            m_targetInfo.Reset();
            m_routeInfo.Reset();
            m_killer = -1;
            m_ejectStarted = m_localMechDestroyed = false;
            // A fresh mech: sensors whole, no PPC lock jam (the HUD's arc glitch off).
            m_sensorsDamaged = false;
            m_lockJamUntil = 0;

            PlayerFlags = (PlayerFlags & ~6 & ~0x4001) | 0x10;
            if (Host.Net.NetRules) Objectives.RestartStarMission(SimMain.g_world, m_player.m_team);
            if (m_host.Motion != null) GPAnim.ResetMotion(m_host.Motion);
            FirstMech();

            if (IsLocal)
            {
                Cockpit.Current?.ResetCockpitPanels();
                Debris.g_localMechLost = false;
                SimMain.g_localMechDestroyed = false;
            }
        }

        public bool NetMoves => m_type != 3 && m_type != 7 && m_torsoTwist != null;

        // ---- Rollback (a MekNet client: NetMatchDriver) -----------------------------------------------------------------
        // Each step's end is kept: the state, and what the next step's move reads that the late update set (the ramps'
        // targets, the turn, the jets' buttons, the jump fuel). A rollback sets the host's state at a past step, then runs
        // only the move (UpdateMechClass) on to now, each step with the inputs kept then; Resimulating keeps the move's
        // sounds, shakes, damage and knockdowns out of it.

        public static bool g_resimulating;

        // The host's heat (a reported hit's) for the next step: UpdateMechClass clears DeltaHeat as it starts.
        public int m_pendingNetHeat;

        public void AddNetHeat(int p_heat) => m_pendingNetHeat += p_heat;
    }
}
