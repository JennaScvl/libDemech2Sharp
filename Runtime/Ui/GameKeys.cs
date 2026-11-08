using System;

namespace Demech2
{
    // The game keys (demech2 MW2/src/inputmap.c: g_gameKeyNames; gamekeys.c: RunGameKey): numbered actions, run when a
    // bound input is pressed (GAMEKEY.MAP's names only seed the game's default bindings). Ported: the piloting
    // actions (the throttle steps, reverse, feet to torso, reset inputs), the mech's power (startup, shutdown, override)
    // and the cockpit's displays (Cockpit.RunGameKey: the mech view's modes, the target display, the HUD, the radar and
    // its zoom, the objectives), the weapons (groups, group fire, jettison), the targets (the cycles, the reticle, the
    // nearest enemy, inspecting), ejecting and self-destructing, the ordinance view, the autopilot and its heading, MASC,
    // the in-mission menus (SimMenus) and exiting. Once the mission has ended only EXIT_SIM runs (HandleGameKeys). What
    // isn't the sim's (the mission's end, the local steering, a movie's keys, the commands' lockstep) is the game's
    // (Host.Input).
    public static class GameKeys
    {
        // The commands, in table order: our id and the number Run takes (StreamingAssets/Tables/CommandKeys.json).
        static CommandRow[] Commands => SimTables.CommandKeys;

        // GAMEKEY.MAP's names for our ids (StreamingAssets/Tables/LegacyMapNames.json), read only to seed the defaults.

        static MapNameFile MapNames => SimTables.LegacyMapNames;

        // The id INPUT.MAP's block name stands for, null for none.
        public static string ChannelOfMapName(string p_mapName) => IdOfMapName(MapNames.channels, p_mapName);

        static string IdOfMapName(MapNameRow[] p_rows, string p_mapName)
        {
            foreach (MapNameRow row in p_rows)
            {
                if (string.Equals(row.mapName, p_mapName, StringComparison.OrdinalIgnoreCase)) return row.id;
            }

            return null;
        }

        // The number of the command with our id p_id, -1 for none.
        public static int NumberOf(string p_id)
        {
            foreach (CommandRow row in Commands)
            {
                if (row.id == p_id) return row.number;
            }

            return -1;
        }

        public const int MfdCycleA = 200, MfdCycleB = 201, MfdCycleC = 202, MfdCycleD = 203, MfdCycleE = 204, MfdCycleF = 205,
            MfdsReset = 206, SaveHud = 207, ToggleCompass = 208, ToggleWeapons = 209;
        public const int AddWeaponToGroup4 = 210, AddWeaponToGroup5 = 211, FireAllWeapons = 212, FireCycleWeaponGroup = 213,
            ToggleAms = 214;
        public const int StarmateSetAll = 215, StarmateSetPt2 = 216, StarmateSetPt3 = 217;
        public const int FireWeaponGroup4 = 218, FireWeaponGroup5 = 219;

        // g_localSteering.m_reverse: REVERSE_DIRECTION's toggle.
        public static bool Reverse
        {
            get => Host.Input.LocalSteering("reverse") != 0;
            set => Host.Input.SetLocalSteering("reverse", value ? 1 : 0);
        }

        // The game keys' state, for a new mission (g_feetToTorso is the mech's own now: MechPilot).
        public static void Load()
        {
        }

        // The command GAMEKEY.MAP calls p_name, -1 for none.
        public static int ActionOf(string p_name)
        {
            string id = IdOfMapName(MapNames.commandKeys, p_name);
            return id != null ? NumberOf(id) : -1;
        }

        // A button of the local player's steering (g_localSteering), for its mech to act on.
        static void Steer(string p_name) => Host.Input.SetLocalSteering(p_name, 1);

        // HandleDebugKey's views, while the mission timer is stopped: the free eye, the next and previous player tracked.
        static void RunDebugKey(int p_action)
        {
            switch (p_action)
            {
                case 0x0a:
                    Eyepoint.SetViewMode(Eyepoint.c_viewFreeEye);
                    break;
                case 0x0c:
                    Eyepoint.CycleTrackedPlayer(true, false);
                    break;
                case 0x0d:
                    Eyepoint.CycleTrackedPlayer(false, false);
                    break;
            }
        }

        // RunGameKey's piloting cases, for an action's number.
        public static void RunGameKey(int action)
        {
            // Not in the original: a movie takes only the views' keys.
            if (Host.Input.MovieBlocksKey(action)) return;

            // HandleGameKeys: once the mission has ended only EXIT_SIM runs.
            if (Host.Input.MissionEnded)
            {
                if (action == 0x59) Host.Input.QuitMission();
                // A network game's lost player, spectating: SPACEBAR advances the viewpoint.
                else if (action == 0x4f && Eyepoint.g_spectating) Eyepoint.CycleTrackedPlayer(true, false);
                // Offered the regenerate: SPACEBAR asks the host (it reloads the mech).
                else if (action == 0x4f && Host.Input.RegenerateOffered) Host.Input.RequestRespawn();
                return;
            }

            // Not in the original: while a menu pauses the sim only the menu keys run (the game's console too).
            if (Host.Input.ConsoleBlocking) return;
            if (SimMenus.Paused && action != 0x33 && action != 0x34 && (action < 0x42 || action > 0x44)) return;

            int step = -1;
            switch (action)
            {
                case 0x01:
                    InputMap.Set("torso.pitch.center", 1);
                    InputMap.Set("torso.yaw.center", 1);
                    InputMap.Set("head.pitch.center", 1);
                    InputMap.Set("head.yaw.center", 1);
                    InputMap.Set("cam.pitch.center", 1);
                    InputMap.Set("cam.yaw.center", 1);
                    break;
                case 0x11:
                    Host.Cockpit.IssuePilotCommand(PilotCommand.FeetToTorso, 0);
                    break;
                case 0x3f:
                    Reverse = !Reverse;
                    break;
                case 0x3d:
                case 0x3e:
                {
                    // STARTUP_MECH and SHUTDOWN_MECH alike: a shut-down mech powers up, any other shuts down (not while
                    // self-destructing).
                    ICockpitMech pilot = Cockpit.Current?.Mech;
                    if (pilot == null) break;
                    if (pilot.PowerState == 3) Host.Cockpit.IssuePilotCommand(PilotCommand.PowerUp, 0);
                    else if (pilot.PowerState != 7) Host.Cockpit.IssuePilotCommand(PilotCommand.PowerDown, 0);
                    break;
                }
                case 0x40:
                    Cockpit.OverrideShutdown = true;
                    break;
                case 0x33:
                    SimMenus.RequestMenu(4);
                    break;
                case 0x34:
                    SimMenus.ToggleMenu(5);
                    break;
                case 0x42:
                    if (Cockpit.Current != null) Cockpit.Current.ShowObjectives = false;
                    SimMenus.ToggleMenu(1);
                    break;
                case 0x43:
                    if (Cockpit.Current != null) Cockpit.Current.ShowObjectives = false;
                    SimMenus.ToggleMenu(7);
                    break;
                case 0x44:
                    if (Cockpit.Current != null) Cockpit.Current.ShowObjectives = false;
                    SimMenus.ToggleMenu(8);
                    break;
                case 0x45:
                case 0x46:
                case 0x47:
                case 0x48:
                case 0x49:
                    if (Cockpit.Current != null) Cockpit.Current.ShowObjectives = false;
                    break;
                case 0x4a:
                    Steer("mech.autopilot");
                    break;
                case 0xa6:
                    SystemSettings.SetInfrared(!SystemSettings.InfraredOn);
                    break;
                case 0xa7:
                {
                    // ENHANCED_VISION: on only while the local player is powered up (flag 0x2000), with its line.
                    Player local = SimMain.PlayerOf(SimMain.g_localPlayerId);
                    if (SystemSettings.Wireframe != 1 && local != null && (local.m_flags & 0x2000) != 0)
                    {
                        SystemSettings.SetWireframe(1);
                        MissionSpeech.PlayCockpitSound(0x1b, 1);
                    }
                    else SystemSettings.SetEnhancedVision(false);

                    break;
                }
                case 0x55:
                    Cockpit.PunchInAutoHeadingRequested = true;
                    break;
                case 0x56:
                    Host.Cockpit.IssuePilotCommand(PilotCommand.ToggleMasc, 0);
                    break;
                case 0x59:
                    Host.Input.QuitMission();
                    break;
                case 0x09:
                {
                    // COCKPIT_VIEW: back to the cockpit (out of the satellite view), the pilot's look and the zoom reset;
                    // from the cockpit, the track view on the local player.
                    Player local = SimMain.PlayerOf(SimMain.g_localPlayerId);
                    if (local == null || (local.m_flags & 2) != 0) break;
                    if (Eyepoint.GetViewMode() != Eyepoint.c_viewCockpit || Cockpit.IsSatelliteView)
                    {
                        Cockpit.Current?.LeaveSatelliteView();
                        Eyepoint.SetViewMode(Eyepoint.c_viewCockpit);
                        InputMap.Set("head.pitch.center", 1);
                        InputMap.Set("head.yaw.center", 1);
                        InputMap.Set("cam.zoom.center", 1);
                    }
                    else
                    {
                        Eyepoint.SetViewMode(Eyepoint.c_viewTrack);
                        Eyepoint.CycleTrackedPlayer(false, true);
                    }

                    break;
                }
                case 0x0e:
                    if (Shots.TrackLastShot()) Eyepoint.SetViewMode(Eyepoint.c_viewOrdinance);
                    break;
                case 0x17:
                    InputMap.Set("cam.zoom.center", 1);
                    break;
                case 0x12:
                    Steer("lock.inspect");
                    break;
                case 0x14:
                    Host.Cockpit.IssuePilotCommand(PilotCommand.Jettison, 0);
                    break;
                case 0x24:
                    Steer("thing.next");
                    break;
                case 0x25:
                    Steer("thing.prev");
                    break;
                case 0x26:
                    Steer("thing.clear");
                    break;
                case 0x27:
                    Host.Input.DropTargets();
                    break;
                case 0x28:
                    Steer("nav.next");
                    break;
                case 0x29:
                    Steer("nav.prev");
                    break;
                case 0x2a:
                    Steer("nav.clear");
                    break;
                case 0x2b:
                    Steer("piece.next");
                    break;
                case 0x2c:
                    Steer("piece.prev");
                    break;
                case 0x2d:
                    Steer("piece.clear");
                    break;
                case 0x3b:
                    Host.Cockpit.IssuePilotCommand(PilotCommand.Eject, 0);
                    break;
                case 0x92:
                    // CRACK_MODE: time compression, once the "meepmeep" cheat enables the key.
                    if (!Host.Input.TimeCompressionKey) break;
                    Host.Input.TimeCompression = !Host.Input.TimeCompression;
                    if (Host.Input.TimeCompression) TimedOverlays.ShowInGameMessage("Time compression enabled", 1, 0xb50, 0x32);
                    else TimedOverlays.ShowInGameMessage("Time compression disabled", 1, 0x16a, 0x32);
                    break;
                case 0x3c:
                {
                    Player local = SimMain.PlayerOf(SimMain.g_localPlayerId);
                    if (local == null || (local.m_flags & 0x2000) == 0) break;
                    bool on = !SimMain.g_autoEject;
                    Host.Cockpit.SetAutoEject(on);
                    TimedOverlays.ShowInGameMessage(on ? "Automatic ejection ON" : "Automatic ejection OFF", 1, 0x16a, 0x32);
                    break;
                }
                case 0x4b:
                    Steer("arms.chain.toggle");
                    break;
                case 0x4c:
                    Steer("lock.next");
                    break;
                case 0x4d:
                    Steer("lock.prev");
                    break;
                case 0x4e:
                    Steer("lock.clear");
                    break;
                case 0x50:
                    Steer("lock.nearest");
                    break;
                case 0x51:
                    Steer("lock.friend");
                    break;
                case 0x52:
                    Steer("lock.reticle");
                    break;
                case 0x53:
                    Steer("lock.lastshot");
                    break;
                case 0x54:
                    Steer("next_objective");
                    break;
                case 0x57:
                    Steer("mech.selfdestruct");
                    break;
                case 0x97:
                case 0x98:
                case 0x99:
                    Host.Cockpit.IssuePilotCommand(PilotCommand.SetWeaponGroup, action - 0x97);
                    break;
                case 0x9a:
                    Steer("arms.group.next");
                    break;
                case 0x9b:
                    Steer("arms.fire.group");
                    break;
                case 0x9c:
                    Steer("arms.fire.group1");
                    break;
                case 0x9d:
                    Steer("arms.fire.group2");
                    break;
                case 0x9e:
                    Steer("arms.fire.group3");
                    break;
                // Groups 4 and 5 (SetSelectedWeaponGroup unclamped), fire all, fire and cycle the group, the AMS: the
                // host's weapon rules' (Host.Weapons).
                case AddWeaponToGroup4:
                case AddWeaponToGroup5:
                    Host.Cockpit.IssuePilotCommand(PilotCommand.SetWeaponGroup, action - AddWeaponToGroup4 + 3);
                    break;
                // Fire groups 4 and 5.
                case FireWeaponGroup4:
                    Steer("arms.fire.group4");
                    break;
                case FireWeaponGroup5:
                    Steer("arms.fire.group5");
                    break;
                // Fire all.
                case FireAllWeapons:
                    Steer("arms.fire.all");
                    break;
                case FireCycleWeaponGroup:
                    if (Host.Weapons.FireCyclesGroup)
                    {
                        Steer("arms.fire.group");
                        Steer("arms.group.next");
                    }

                    break;
                case ToggleAms:
                    if (Host.Weapons.TogglesAms) Host.Cockpit.IssuePilotCommand(PilotCommand.ToggleAms, 0);
                    break;
                // The host's SquadRollCall (the mate it picks for the STARMATE_* order keys).
                case StarmateSetAll:
                case StarmateSetPt2:
                case StarmateSetPt3:
                    Host.Input.SquadRollCall(action - StarmateSetAll);
                    break;
                default:
                    if (action >= 0x1a && action <= 0x23) step = action - 0x1a;
                    else if (Cockpit.Current == null || !Cockpit.Current.RunGameKey(action))
                    {
                        if (Host.Input.TimerStopped) RunDebugKey(action);
                    }

                    break;
            }

            if (step != -1)
            {
                InputMap.Set("drive.throttle", step * 113);
                InputMap.Set("drive.throttle.hold", 1);
            }
        }
    }
}
