using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // The local mech's cockpit overlay (demech2 MW2/src/cockpitpanels.c, cockpitpanel.c, config.c LoadCptFile and
    // LoadHudFile, setres.c SetRes, world.c's cptfile/pitfile/vptfile/hudfile/window/anim2d records, mechclass.c
    // DrawMechCockpit): 26 panels drawn over the 3D cockpit, each by the mech's power state (starting up, running, the
    // rest), the HUD (compass, altimeter, crosshair, target marker), the map views, and the cockpit's warning sounds. The
    // panels' places come from the cockpit file the chassis streams name (CPIT) and the HUD file (HUD), authored on a
    // 320x200 screen and scaled to the page (HudPage, 640x480, fitted to the display).
    //
    // What the panels show comes from the mech (MechPilot: its MechConfig, heat, weapons, target and lock); the AI and the
    // network aren't ported. Not ported: the engine note (MIDI). The hit flash's palette
    // fade is a translucent plane (ScreenFlash).
    // CockpitDriver runs it each frame over the cockpit camera's view.
    public partial class Cockpit
    {
        // The panels, by index (c_panel...). A derived cockpit may number them differently (SetPanelLayout).
        protected const int PanelRadar = 0, PanelMechView = 2, PanelFirstWeapon = 3;
        protected int PanelTarget = 13, PanelTargetText = 14, PanelObjectives = 15, PanelNetwork = 16, PanelAutopilot = 17,
            PanelSpeed = 18, PanelMasc = 19, PanelHeat = 20, PanelHeatRate = 21, PanelJets = 22, PanelAltimeter = 23,
            PanelCompass = 24, PanelKills = 25, PanelCount = 26;

        // g_cockpitPanelPanes: the panels' rectangles on the 320x200 screen.
        static readonly int[,] DefaultPanelPanes =
        {
            { 13, 10, 80, 60 }, { 260, 145, 312, 197 }, { 258, 145, 309, 175 }, { 214, 15, 256, 23 },
            { 214, 25, 256, 33 }, { 214, 35, 256, 43 }, { 214, 45, 256, 53 }, { 214, 55, 256, 63 },
            { 268, 15, 319, 23 }, { 268, 25, 319, 33 }, { 268, 35, 319, 43 }, { 268, 45, 319, 53 },
            { 268, 55, 319, 63 }, { 10, 145, 62, 179 }, { 10, 183, 70, 199 }, { 30, 70, 300, 170 },
            { 30, 70, 300, 170 }, { 55, 190, 120, 199 }, { 275, 130, 317, 195 }, { 250, 188, 273, 199 },
            { 110, 175, 169, 189 }, { 170, 175, 209, 189 }, { 210, 175, 249, 189 }, { 1, 74, 35, 111 },
            { 115, 1, 205, 35 }, { 10, 136, 70, 145 },
        };

        // g_cockpitPanelTextOrigins: 16.16 fractions of the panels.
        static readonly int[,] DefaultTextOrigins =
        {
            { 0, 0 }, { 0, 0 }, { 0, 0 }, { 0x11ec, 0x2148 }, { 0x11ec, 0x2148 }, { 0x11ec, 0x2148 }, { 0x11ec, 0x2148 },
            { 0x11ec, 0x2148 }, { 0x11ec, 0x2148 }, { 0x11ec, 0x2148 }, { 0x11ec, 0x2148 }, { 0x11ec, 0x2148 },
            { 0x11ec, 0x2148 }, { 0, 0 }, { 0, 0 }, { 0, 0 }, { 0, 0 }, { 0, 0 }, { 0, 0xe666 }, { 0, 0 }, { 0, 0xa666 },
            { 0, 0xa666 }, { 0, 0xa666 }, { 0, 0 }, { 0, 0 }, { 0, 0 },
        };

        // g_cockpitPanelLightUpTimes: the clock times the panels light up at on startup; the weapon panels in turn.
        static readonly int[] LightUpTimes =
        {
            0x16a, 0xb5, 0xb5, 0x21f, 0x23d, 0x25b, 0x279, 0x297, 0x32e, 0x310, 0x2f2, 0x2d4, 0x2b5, 0xb5, 0xb5, 0xb5, 0xb5,
            0xb5, 0xb5, 0xb5, 0xb5, 0xb5, 0xb5, 0, 0, 0,
        };

        // A cockpit panel (CockpitPanel): a named rectangle with handlers by the power state.
        public sealed class Panel
        {
            public bool Enabled;
            public int Damage;           // m_damage: hits step it; the panel flickers or shows static as it rises
            // Not the original's: what the panel draws by, its worst (static) while impact arcs crawl over the mech.
            public int ShownDamage => arcGlitch ? 3 : Damage;
            public int LightUpTime;      // m_lightUpTime
            public int Weapon = -1;      // m_weapon
            public string Name = "";     // m_name (31 characters)
            public Pane Target;          // *m_target: the transitions move it for a frame
            public int TextX, TextY;     // *m_textOrigin
            public RectTransition Transition;
            public int LastPowerState;   // m_lastPowerState
            public int X, Y, Width, Height;
            public Action<Panel> DrawStartup, Draw, DrawShutdown, DrawStatic;
            // A multi-function display's mode, view slot and gauges (a derived cockpit's).
            public int Mode, MfdSlot = -1;
            public MfdGauges Gauges;

            public void SetName(string p_name) => Name = p_name.Length > 31 ? p_name.Substring(0, 31) : p_name;

            // SetCockpitPanelTarget.
            public void SetTarget(Pane p_target)
            {
                Target = p_target;
                X = p_target.X0;
                Y = p_target.Y0;
                Width = p_target.X1 - p_target.X0 + 1;
                Height = p_target.Y1 - p_target.Y0 + 1;
            }
        }

        // An MFD's gauges (InitMfdGauges 0x100291cc): pixels in its pane.
        public sealed class MfdGauges
        {
            public int ThrottleSizeX, ThrottleSizeY, ThrottleLeft, ThrottleTop, ThrottleRight, ThrottleBottom, ThrottleBarLeft, ThrottleZeroY;
            public int JetsSizeX, JetsSizeY, JetsLeft, JetsTop, JetsRight, JetsBottom, JetsBarX, JetsBarBottom, JetsMaxFill;
            public int HeatX, HeatY, HeatWidth, HeatHeight;
            public int DialWidth;
            public int RateX, RateY, RateWidth, RateHeight;
        }

        public static Cockpit Current { get; private set; }

        // The mech whose cockpit this is.
        public ICockpitMech Mech => mech;

        // The frame's length (seconds), for the eased gauges.
        public static float DeltaTime = 1f / 30f;

        protected readonly ICockpitSurface hud;
        protected readonly ICockpitMech mech;
        protected readonly ICockpitWorld world;
        protected readonly ICockpitView view;

        protected readonly Panel[] panels;
        protected readonly Pane[] panelPanes;
        protected readonly int[,] textOrigins;
        readonly Pane[] gaugePanes = new Pane[5];
        protected readonly Pane[] panes = new Pane[11];   // g_panes: the render targets
        protected readonly bool[] panelEnabled;
        RectTransition targetTransition, mechViewTransition;

        // The HUD file (LoadHudFile): the gauge positions, the outline shape and the outline's parts.
        readonly (int x, int y)[] hudGaugePositions = { (0x73, 0x10), (8, 0x4a), (4, 0x28), (4, 0x4a), (0, 0), (0, 0) };
        protected readonly int[] hudLayoutValues = new int[3];
        protected readonly Pane[] outlinePartRects = new Pane[16];

        // The 2D animations (anim2d records: LoadAnim2d); the first is the panels' static.
        sealed class Anim2d
        {
            public int State, FrameCount, FrameTime, StartTime, Flags, ResourceId;
            public HudShape Shape;
        }

        const int Anim2dNew = 0, Anim2dDone = 1, Anim2dPlaying = 2, Anim2dHeld = 3;
        readonly List<Anim2d> anim2ds = new List<Anim2d>();

        // g_cockpitPowerState, and the display options.
        protected int powerState;
        public bool ShowHud = true;

        // Not the original's (HudSettings.RadarBackdrop): the radar's ellipse darkened behind the map, for contrast.
        public bool RadarBackdrop;
        protected bool showCrosshair = true, showTargetMarker = true, showCompass = true, showAltimeter = true;
        public bool ShowObjectives;
        public int MechViewMode = 1;  // g_mechViewMode
        public int TargetPanelMode = 1; // g_targetPanelMode
        protected const bool Radar = true;      // g_difficulty->m_radar: set outside network games

        // PlayCockpitWarnings' state.
        int lastWarningPowerState;
        bool lockedTonePlayed, lockingTonePlayed;

        // g_overrideShutdown (the override key), g_punchInAutoHeadingRequested.
        public static bool OverrideShutdown, PunchInAutoHeadingRequested;

        // g_cockpitPanels[c_panelRadar]->m_damage.
        public int RadarDamage => panels[PanelRadar].Damage;

        // Not the original's: impact arcs (ImpactArcs) on the local mech glitch every panel, cosmetically.
        static bool arcGlitch;

        // The dev console's arc glitch (DevConsole "glitch"): on until this render clock.
        static int arcGlitchTestUntil = int.MinValue;

        public static void TestArcGlitch(float p_seconds) =>
            arcGlitchTestUntil = Host.Cockpit.RenderClock + (int)(p_seconds * Clock.TicksPerSecond);

        // The damage animations' own state from before the arc glitch, put back when it ends.
        bool savedMapDamageAnimShown, savedMechViewStatic, savedTargetPanelStatic;
        int savedSatelliteStaticState, savedStaticCleanUntil, savedStaticNoiseUntil;

        // The arc glitch on or off; off restores what the panels' real damage had drawn, so they carry on from there.
        void SetArcGlitch(bool p_on)
        {
            if (p_on == arcGlitch) return;
            arcGlitch = p_on;
            if (p_on)
            {
                savedMapDamageAnimShown = mapDamageAnimShown;
                savedMechViewStatic = mechViewStatic;
                savedTargetPanelStatic = targetPanelStatic;
                savedSatelliteStaticState = satelliteStaticState;
                savedStaticCleanUntil = staticCleanUntil;
                savedStaticNoiseUntil = staticNoiseUntil;
            }
            else
            {
                mapDamageAnimShown = savedMapDamageAnimShown;
                mechViewStatic = savedMechViewStatic;
                targetPanelStatic = savedTargetPanelStatic;
                satelliteStaticState = savedSatelliteStaticState;
                staticCleanUntil = savedStaticCleanUntil;
                staticNoiseUntil = savedStaticNoiseUntil;
            }
        }

        // The arc glitch's re-roll, ten times a second of the render clock.
        static int ArcGlitchStep => Host.Cockpit.RenderClock * 10 / Clock.TicksPerSecond;

        // p_text with every non-space character a random one, re-rolled each arc-glitch step (p_salt: per panel).
        static string ScrambleText(string p_text, int p_salt)
        {
            var random = new System.Random(ArcGlitchStep * 31 + p_salt);
            char[] chars = p_text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] != ' ') chars[i] = (char)random.Next('!', '_' + 1);
            }

            return new string(chars);
        }

        // The local player's heading and torso twist in whole degrees (UpdateCockpit).
        int headingDegrees, torsoTwistDegrees;

        // Player::m_targetInfo, as UpdateCockpit reads it each frame.
        int targetId;               // m_target: 0x100 | nav, 0x200 | player, 0x400 | thing; 0x1000 lost
        Vector3 targetPosition;     // units
        int routeId = -1;           // the route's nav (ICockpitMech.Route) and its place
        Vector3 routePosition;
        int targetHeading, targetPitch, targetRange; // 16.16 degrees, units

        // The cockpit of p_mech (the local player's, of p_world), drawn on p_surface over p_view: its layout read from
        // the chassis streams and scaled, its panels set up (InitCockpitPanels).
        public Cockpit(ICockpitSurface p_surface, ICockpitWorld p_world, ICockpitMech p_mech, ICockpitView p_view)
        {
            hud = p_surface;
            world = p_world;
            mech = p_mech;
            view = p_view;
            Current = this;
            arcGlitch = false;
            SetPanelLayout();

            panels = new Panel[PanelCount];
            panelPanes = new Pane[PanelCount];
            panelEnabled = new bool[PanelCount];
            textOrigins = (int[,])TextOriginTable.Clone();
            TimedOverlays.Reset();
            OverrideShutdown = false;
            LoadLayout();
            SetRes();
            InitCockpitPanels();
        }

        // The cockpit is gone (the mission ended).
        public void Release()
        {
            if (Current == this) Current = null;
            Host.Cockpit.EngineNoteStop();
        }

        // ---- Layout -----------------------------------------------------------------------------------------------

        // The chassis streams' cockpit records, as the world stream runs them for each player's gpspec in turn: the cockpit
        // file (every mech's, the last standing), the pit and viewport streams' windows and 2D animations, and the HUD
        // file of the local player's (the first HUD file after its gpspec).
        void LoadLayout()
        {
            for (int i = 0; i < PanelCount; i++)
            {
                int[,] defaults = PanelPaneTable;
                panelPanes[i] = new Pane(defaults[i, 0], defaults[i, 1], defaults[i, 2], defaults[i, 3]);
            }

            // InitPanes: every render target the screen (the window records set theirs).
            for (int i = 0; i < panes.Length; i++) panes[i] = new Pane(0, 0, ScreenScale.g_screenWidthMinus1, ScreenScale.g_screenHeightMinus1);

            bool loadHudFile = false;
            foreach (Player player in world.CockpitPlayers)
            {
                if (IsLocalPlayer(player)) loadHudFile = true;
                Host.Cockpit.RunCockpitStream(this, player.m_chassis, ref loadHudFile);
            }
        }

        // The gpspec not marked remote (AI byte 0); in a networked match every pilot's is cleared, so the local slot's
        // (SimMain.g_localPlayerId).
        protected static bool IsLocalPlayer(Player p_player) =>
            Host.Net.NetRules ? p_player.m_index == SimMain.g_localPlayerId : p_player.m_aiMode == 0;

        // The chassis stream's window record (the game walks the stream: RunCockpitStream): as many 0x20-byte entries as
        // fit, each an index and a 320x200 rectangle; p_offset is the record's header in p_data.
        public void LoadWindowRecord(byte[] p_data, int p_offset, int p_size)
        {
            for (int at = 8; at + 0x14 <= p_size; at += 0x20)
            {
                int index = BitConverter.ToInt32(p_data, p_offset + at);
                if (index < 0 || index > 8) continue;
                int x = BitConverter.ToInt32(p_data, p_offset + at + 4), y = BitConverter.ToInt32(p_data, p_offset + at + 8);
                panes[index] = new Pane(x, y, x + BitConverter.ToInt32(p_data, p_offset + at + 0xc) - 1,
                    y + BitConverter.ToInt32(p_data, p_offset + at + 0x10) - 1);
            }
        }

        // LoadCptFile: five gauge rectangles, fifteen panel rectangles (x, y, width, height; CptRectCount in all) and a
        // point.
        public void LoadCptFile(byte[] data)
        {
            int count = CptRectCount;
            if (data == null || data.Length < count * 8 + 4) return;
            for (int i = 0; i < count; i++)
            {
                int x = BitConverter.ToInt16(data, i * 8), y = BitConverter.ToInt16(data, i * 8 + 2);
                int width = BitConverter.ToInt16(data, i * 8 + 4), height = BitConverter.ToInt16(data, i * 8 + 6);
                var pane = new Pane(x, y, x + width - 1, y + height - 1);
                if (i < 5) gaugePanes[i] = pane;
                else panelPanes[i - 5] = pane;
            }
        }

        // LoadHudFile: five gauge positions, three values (the first the damage outline's shape) and fifteen rectangles
        // of the outline's parts, any out of 0-100 cleared.
        public void LoadHudFile(byte[] data)
        {
            if (data == null || data.Length < (10 + 3 + 60) * 4) return;
            int at = 0;
            int Next()
            {
                int value = BitConverter.ToInt32(data, at);
                at += 4;
                return value;
            }

            for (int i = 0; i < 5; i++) hudGaugePositions[i] = (Next(), Next());
            for (int i = 0; i < 3; i++) hudLayoutValues[i] = Next();
            for (int i = 0; i < 15; i++)
            {
                int left = Next(), top = Next(), right = Next(), bottom = Next();
                if (left < 0 || left > 100 || top < 0 || top > 100 || right < 0 || right > 100 || bottom < 0 || bottom > 100)
                {
                    left = top = right = bottom = 0;
                }

                outlinePartRects[i] = new Pane(left, top, right, bottom);
            }
        }

        // LoadAnim2d: up to seven, of type 1 and a resource.
        public void LoadAnim2d(int p_flags, int p_frameTime, int p_type, int p_resourceId)
        {
            if (anim2ds.Count >= 7 || p_type != 1 || p_resourceId < 1) return;
            anim2ds.Add(new Anim2d { State = Anim2dNew, Flags = p_flags, FrameTime = p_frameTime, ResourceId = p_resourceId });
        }

        // SetRes: the tables authored on the 320x200 screen scaled to the page.
        void SetRes()
        {
            for (int i = 0; i < 8; i++) panes[i] = ScreenScale.LowResToScreen(panes[i]);
            for (int i = 0; i < 5; i++) gaugePanes[i] = ScreenScale.LowResToScreen(gaugePanes[i]);
            for (int i = 0; i < 6; i++) hudGaugePositions[i] = ScreenScale.LowResPointToScreen(hudGaugePositions[i].x, hudGaugePositions[i].y);
            ScaleCockpitLayout();
        }

        // ScaleCockpitLayout: the panels' rectangles, text positions and transitions.
        void ScaleCockpitLayout()
        {
            var center = new Pane(0x8000, 0x8000, 0x8000, 0x8000);
            var whole = new Pane(0, 0, 0x10000, 0x10000);
            targetTransition = new RectTransition(0xb5, center, whole);
            mechViewTransition = new RectTransition(0xb5, center, whole);
            for (int i = 0; i < PanelCount; i++)
            {
                Pane target = ScreenScale.LowResToScreen(panelPanes[i]);
                panelPanes[i] = target;
                (textOrigins[i, 0], textOrigins[i, 1]) = ScreenScale.ScalePointToFrame(target, textOrigins[i, 0], textOrigins[i, 1]);
                RectTransition transition = TransitionOf(i);
                if (transition != null)
                {
                    transition.First = ScreenScale.ScaleRectToFrame(target, transition.First);
                    transition.Second = ScreenScale.ScaleRectToFrame(target, transition.Second);
                }
            }
        }

        // g_cockpitPanelTransitions: the mech view's and the target panel's (SharesMechViewTransition).
        protected RectTransition TransitionOf(int p_panel) =>
            SharesMechViewTransition(p_panel) ? mechViewTransition
            : p_panel == PanelTarget ? targetTransition : null;

        // InitCockpitPanels: the panels, all enabled but the second (and the target panel without the radar), the
        // weapon panels laid out, the handlers, and the gauges, the damage panel, the HUD and the map views set up.
        void InitCockpitPanels()
        {
            for (int i = 0; i < PanelCount; i++) panelEnabled[i] = true;
            if (InitOwnPanels()) return;

            panelEnabled[1] = false;
            if (!Radar) panelEnabled[PanelTarget] = false;

            for (int i = 0; i < PanelCount; i++)
            {
                var panel = new Panel { LightUpTime = LightUpTable[i], Transition = TransitionOf(i), Enabled = panelEnabled[i] };
                panel.SetTarget(panelPanes[i]);
                panel.TextX = textOrigins[i, 0];
                panel.TextY = textOrigins[i, 1];
                panels[i] = panel;
            }

            LayoutWeaponPanels();
            panels[PanelMechView].Draw = DrawMechViewPanel;
            panels[PanelMechView].DrawStatic = DrawMechViewStatic;
            panels[PanelMechView].DrawStartup = DrawMechViewStartup;
            panels[PanelMechView].DrawShutdown = DrawMechViewShutdown;
            panels[PanelTarget].Draw = DrawTargetPanel;
            panels[PanelTarget].DrawStatic = DrawTargetStatic;
            panels[PanelTargetText].Draw = DrawTargetPanelText;
            panels[PanelTarget].DrawStartup = DrawTargetPanelStartup;
            panels[PanelTarget].DrawShutdown = DrawTargetPanelShutdown;
            panels[PanelObjectives].Draw = DrawObjectivesPanel;
            panels[PanelNetwork].Draw = DrawNetworkPanel;
            panels[PanelAutopilot].Draw = DrawAutopilotPanel;
            panels[PanelSpeed].Draw = DrawSpeedPanel;
            panels[PanelMasc].Draw = DrawMascPanel;
            panels[PanelHeat].Draw = DrawHeatPanel;
            panels[PanelHeatRate].Draw = DrawHeatRatePanel;
            panels[PanelJets].Draw = DrawJetsPanel;
            if (PanelKills >= 0) panels[PanelKills].Draw = DrawKillsPanel;
            ScaleBarGauges();
            InitDamagePanel();
            InitHudGauges();
            if (Radar) InitCockpitViews();
        }

        // ResetCockpitPanels (cockpitpanels.c, 0x1006ff7b): the weapon panels laid out again for the reloaded mech's
        // configuration (MechPilot.ReloadPlayerMech), each panel's light-up time, transition and damage reset, the enabled
        // ones enabled.
        public void ResetCockpitPanels()
        {
            SetArcGlitch(false);
            int lastRight = LastRightWeaponPanel;
            for (int i = 3; i <= lastRight && i < PanelCount; i++) panels[i].Weapon = -1;
            LayoutWeaponPanels();
            for (int i = 0; i < PanelCount; i++)
            {
                panels[i].LightUpTime = LightUpTable[i];
                panels[i].Transition = TransitionOf(i);
                panels[i].Damage = 0;
                if (panelEnabled[i]) panels[i].Enabled = true;
            }
        }

        // LayoutWeaponPanels: the weapons on hardpoints 5, 3 and 7 on the left panels (3 to 7), those on 4, 1 and 6 on
        // the right (8 to 12), those on 2 and 0 filling the rest, alternating; then the weapons reordered to match (the
        // left at even indices, the right at odd ones), their bins renumbered, and the slots left empty cleared.
        // A derived cockpit may have more panels a side (FirstRightWeaponPanel, LastRightWeaponPanel), and renumber only
        // assigned panels without the bins (LayoutRenumbersBins).
        protected void LayoutWeaponPanels()
        {
            MechConfig config = mech.Config;
            if (config == null) return;
            WeaponSlot[] weapons = config.m_weapons;
            int count = weapons.Length;
            int firstRight = FirstRightWeaponPanel, lastRight = LastRightWeaponPanel, afterRight = lastRight + 1;
            bool alternate = true;
            for (int i = 0; i < count; i++) weapons[i].m_index = i;

            int rightStart = firstRight;
            for (int i = 0; i < count; i++)
            {
                int h = weapons[i].m_hardpoint;
                if (h == 4 || h == 1 || h == 6) rightStart++;
            }

            void Assign(int p_panel, int p_weapon)
            {
                // More weapons than panels (a networked match's fourteen in a classic cockpit): the rest get no panel.
                if (p_panel >= PanelCount) return;
                panels[p_panel].SetName(WeaponDef.Of(weapons[p_weapon].m_type).m_name ?? "");
                panels[p_panel].Weapon = p_weapon;
                panels[p_panel].DrawStartup = DrawWeaponPanelStartup;
                panels[p_panel].Draw = DrawWeaponPanel;
            }

            int left = 3;
            for (int i = 0; i < count; i++)
            {
                int h = weapons[i].m_hardpoint;
                if (h == 5 || h == 3 || h == 7) Assign(left++, i);
                if (left == firstRight) left = rightStart;
            }

            int right = firstRight;
            for (int i = 0; i < count; i++)
            {
                int h = weapons[i].m_hardpoint;
                if (h == 4 || h == 1 || h == 6) Assign(right++, i);
                if (right > lastRight) right = left;
            }

            int panel;
            for (int i = 0; i < count; i++)
            {
                int h = weapons[i].m_hardpoint;
                if (h == 2 || h == 0)
                {
                    if (alternate && right > left)
                    {
                        panel = right;
                        if (panel == afterRight)
                        {
                            panel = left;
                            left++;
                        }
                        else right++;

                        alternate = false;
                    }
                    else
                    {
                        panel = left;
                        if (panel == firstRight)
                        {
                            panel = right;
                            right++;
                        }
                        else left++;

                        alternate = true;
                    }

                    if (panel < PanelCount) Assign(panel, i);
                }
            }

            // In a networked match the weapons keep the .mek's order on every machine (the slots the match's state
            // names); the panels point at them where they are.
            if (Host.Net.NetRules) return;

            // The weapons in the panels' order: the left ones at even indices, the right at odd.
            var reordered = new WeaponSlot[count];
            for (int i = 0; i < count; i++) reordered[i] = new WeaponSlot();
            int dst = 0;
            for (int i = 3; i < firstRight; i++)
            {
                if (panels[i].Weapon == -1) continue;
                if (dst < count) reordered[dst] = weapons[panels[i].Weapon].Clone();
                if (i != firstRight - 1) dst += 2;
            }

            dst = 1;
            for (int i = firstRight; i <= lastRight; i++)
            {
                if (panels[i].Weapon == -1) continue;
                if (dst < count) reordered[dst] = weapons[panels[i].Weapon].Clone();
                if (i != lastRight) dst += 2;
            }

            int index = 0;
            for (int i = 3; i < firstRight; i++)
            {
                if (panels[i].Weapon != -1)
                {
                    WeaponSlot slot = weapons[panels[i].Weapon];
                    if (LayoutRenumbersBins)
                    {
                        for (int j = 0; j < slot.m_binCount && j < 10; j++) config.m_ammoBins[slot.m_bins[j]].m_weapon = index;
                    }
                }

                if (LayoutRenumbersBins || panels[i].Weapon != -1)
                {
                    panels[i].Weapon = index;
                    index += 2;
                }
            }

            index = 1;
            for (int i = firstRight; i <= lastRight; i++)
            {
                if (panels[i].Weapon != -1)
                {
                    WeaponSlot slot = weapons[panels[i].Weapon];
                    if (LayoutRenumbersBins)
                    {
                        for (int j = 0; j < slot.m_binCount && j < 10; j++) config.m_ammoBins[slot.m_bins[j]].m_weapon = index;
                    }

                    panels[i].Weapon = index;
                    index += 2;
                }
            }

            for (int i = 0; i < count; i++)
            {
                weapons[i] = reordered[i];
                // A blank of the reorder (IsBlankSlot) has no type.
                if (IsBlankSlot(weapons[i])) weapons[i].m_type = -1;
            }

            // With ten weapons the indices can pass the slots; those panels show nothing.
            for (int i = 3; i <= lastRight; i++)
            {
                if (panels[i].Weapon >= count) panels[i].Weapon = -1;
            }
        }

        // ---- The frame --------------------------------------------------------------------------------------------

        // DrawMechCockpit, UpdateMenus, then DrawTimedOverlays: the frame's overlay, between the surface's Begin and End.
        public void Frame()
        {
            Art.ResetTextColors();
            // The glitch: a derived cockpit's causes (ArcGlitchCause) and the test. Off from the moment the mech dies
            // (out, flags 6; wrecked or ejecting, power 4/5), not only once the cockpit has seen its wreck.
            Mech pilot = SimMain.MechOf(SimMain.g_localPlayerId);
            bool dead = SimMain.g_localMechDestroyed || (mech.PlayerFlags & 6) != 0 || mech.PowerState == 4 || mech.PowerState == 5;
            SetArcGlitch(!dead && (ArcGlitchCause(pilot) || Host.Cockpit.RenderClock < arcGlitchTestUntil));
            hud.Glitching = arcGlitch;
            DrawSatelliteFrame();
            UpdateCockpit();
            SimMenus.Draw(hud);
            TimedOverlays.Draw(hud);
            EndStretch();
        }

        // UpdateCockpit: the heading and twist the panels show, the target's bearing (from the heading, and from the torso)
        // and distance, the override's sound, the warnings, then each panel's handler for the power state.
        void UpdateCockpit()
        {
            PlayerTargetInfo target = mech.Target;
            targetId = target.m_target;
            targetPosition = target.m_position;
            routeId = mech.Route.m_target;
            routePosition = mech.Route.m_position;
            targetHeading = target.m_heading;
            targetPitch = target.m_pitch;
            targetRange = (int)target.m_range;
            powerState = mech.PowerState;
            if (powerState != 2) panels[PanelNetwork].Draw?.Invoke(panels[PanelNetwork]);
            if (powerState == 4 || powerState == 5) return;
            if (!ShowHud) return;

            headingDegrees = ((mech.HeadingFixed >> 16) % 360 % 360 + 360) % 360;
            torsoTwistDegrees = (mech.TorsoTwist >> 16) % 360 % 360;
            int pitch = (targetPitch + mech.TorsoPitch) % 0x1680000;
            int bearing = (targetHeading >> 16) % 360 - headingDegrees;
            if (bearing > 180) bearing -= 360;
            else if (bearing < -180) bearing += 360;
            int twistBearing = bearing - torsoTwistDegrees;
            if (twistBearing > 180) twistBearing -= 360;
            else if (twistBearing < -180) twistBearing += 360;

            Vector3Int position = mech.Position;
            int distance = ApproxLen.ApproximateVectorLength((int)targetPosition.x - position.x,
                (int)targetPosition.y - mech.ObjectY, (int)targetPosition.z - position.z);

            // The keys' requests go to the pilot's mech as commands (PilotCommands; MechPilot.UpdateLocalMech acts on them
            // at its step, running for the heading, overheating for the override).
            if (PunchInAutoHeadingRequested)
            {
                Host.Cockpit.IssuePilotCommand(PilotCommand.PunchInAutoHeading, 0);
                PunchInAutoHeadingRequested = false;
            }

            if (OverrideShutdown) Host.Cockpit.IssuePilotCommand(PilotCommand.OverrideShutdown, 0);
            OverrideShutdown = false;
            PlayCockpitWarnings();
            switch (powerState)
            {
                case 2:
                    if (Radar) RunMapView();
                    foreach (Panel panel in panels) panel.Draw?.Invoke(panel);
                    DrawHud(headingDegrees, torsoTwistDegrees, bearing, twistBearing, pitch, distance, Eyepoint.g_cockpitEyeSteady);
                    Host.Cockpit.EngineNoteUpdate((uint)mech.Throttle, mech.PowerState);
                    break;
                case 1:
                    if (Radar) PowerUpMapView();
                    foreach (Panel panel in panels) panel.DrawStartup?.Invoke(panel);
                    Host.Cockpit.EngineNoteStart();
                    break;
                default:
                    if (Radar) PowerDownMapView();
                    foreach (Panel panel in panels) panel.DrawShutdown?.Invoke(panel);
                    Host.Cockpit.EngineNoteStop();
                    break;
            }

            if (!Eyepoint.g_inCockpitView) Host.Cockpit.EngineNoteMute();
        }

        // PlayCockpitWarnings: in the cockpit, the lock tones (locked with a ready weapon, else locking) and the power
        // state's changes: running, and off or destroyed.
        void PlayCockpitWarnings()
        {
            if (SimMain.g_hitFadePending)
            {
                SimMain.g_hitFadeCount++;
                ScreenFlash.FlashZappedPaletteLevel(SimMain.g_hitFadeCount * 3);
                SimMain.g_hitFadePending = false;
            }

            if (Eyepoint.g_inCockpitView)
            {
                if ((mech.Flags & 0x80) != 0)
                {
                    if (!lockedTonePlayed && SelectedWeaponState() == WeaponSlot.Ready && mech.PowerState == 2)
                    {
                        lockedTonePlayed = true;
                        CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xfe), 100, 0x5f, 5, 0x32);
                    }
                }
                else
                {
                    lockedTonePlayed = false;
                    if ((mech.Flags & 0x40) != 0)
                    {
                        if (!lockingTonePlayed && mech.PowerState == 2)
                        {
                            lockingTonePlayed = true;
                            CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xcf), 100, 0x1f, 5, 0x32);
                        }
                    }
                    else lockingTonePlayed = false;
                }

                if (mech.PowerState != lastWarningPowerState)
                {
                    switch (mech.PowerState)
                    {
                        case 2:
                            CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xce), 100, 0x2f, 5, 0x32);
                            break;
                        case 0:
                        case 4:
                            CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xf6), 100, 0x40, 5, 0x32);
                            break;
                    }
                }
            }

            lastWarningPowerState = mech.PowerState;
        }

        WeaponSlot SelectedWeapon()
        {
            MechConfig config = mech.Config;
            if (config == null || mech.SelectedWeapon < 0 || mech.SelectedWeapon >= config.m_weapons.Length) return null;
            return config.m_weapons[mech.SelectedWeapon];
        }

        int SelectedWeaponState() => SelectedWeapon()?.m_state ?? WeaponSlot.Empty;

        // DrawPanelAnim / DrawAnim2d: animation p_index's current frame, by the clock, once or looping.
        protected void DrawAnim(Pane p_target, int p_index, int p_x, int p_y)
        {
            if (p_index < 0 || p_index >= anim2ds.Count) return;
            Anim2d anim = anim2ds[p_index];
            if (anim == null || anim.State == Anim2dDone) return;
            if (anim.Shape == null)
            {
                anim.Shape = Art.ShapeOf(anim.ResourceId + Art.g_artResolution);
                if (anim.Shape == null)
                {
                    anim2ds[p_index] = null;
                    return;
                }

                anim.FrameCount = anim.Shape.FrameCount;
            }

            if (anim.State == Anim2dNew) anim.State = Anim2dPlaying;
            int frame;
            if (anim.StartTime != 0)
            {
                frame = (Host.Cockpit.RenderClock - anim.StartTime) / Math.Max(1, anim.FrameTime);
                if (frame < 0) frame = 0;
            }
            else
            {
                frame = 0;
                anim.StartTime = Host.Cockpit.RenderClock;
            }

            int shown = 0;
            if ((anim.Flags & 1) != 0)
            {
                if (frame >= anim.FrameCount)
                {
                    if ((anim.Flags & 2) != 0)
                    {
                        shown = anim.FrameCount - 1;
                        anim.State = Anim2dHeld;
                    }
                    else anim.State = Anim2dDone;
                }
                else shown = frame;
            }
            else shown = frame % anim.FrameCount;

            if (anim.State != Anim2dDone) hud.Shape(p_target, anim.Shape, shown, p_x, p_y);
            else if ((anim.Flags & 8) != 0) anim2ds[p_index] = null;
        }

        // OutlinePane: the pane's edge, a pixel in.
        protected void OutlinePane(Pane p_target, int p_color)
        {
            int width = p_target.X1 - p_target.X0, height = p_target.Y1 - p_target.Y0;
            hud.Line(p_target, 0, 0, width, 0, p_color);
            hud.Line(p_target, width, 0, width, height, p_color);
            hud.Line(p_target, width, height, 0, height, p_color);
            hud.Line(p_target, 0, height, 0, 0, p_color);
        }

        // GetTextWidth.
        static int TextWidth(string p_text, VfxFont p_font)
        {
            int width = 0;
            foreach (char c in p_text) width += p_font.CharacterWidth(c);
            return width;
        }

        // DamageCockpitPanels: each panel has a two in ten chance (p_heavy: five) of being damaged a step more.
        public void DamageCockpitPanels(bool p_heavy)
        {
            int chance = p_heavy ? 5 : 2;
            foreach (Panel panel in panels)
            {
                if (SimRandom.Cosmetic.RandomIntBelow(10) < chance) panel.Damage++;
            }
        }

        // ---- Game keys --------------------------------------------------------------------------------------------

        // ---- A derived cockpit's own layout and keys (each default the base game's) ----------------------------------

        // The panels' numbering (PanelTarget and on, PanelCount), set before anything is made.
        protected virtual void SetPanelLayout() { }

        // The panels' 320x200 rectangles, text origins and light-up times; the cockpit file's rectangle count.
        protected virtual int[,] PanelPaneTable => DefaultPanelPanes;
        protected virtual int[,] TextOriginTable => DefaultTextOrigins;
        protected virtual int[] LightUpTable => LightUpTimes;
        protected virtual int CptRectCount => 20;

        // The panels the mech view's transition is drawn on.
        protected virtual bool SharesMechViewTransition(int p_panel) => p_panel == PanelMechView;

        // InitCockpitPanels when the derived cockpit makes its own panels (true).
        protected virtual bool InitOwnPanels() => false;

        // LayoutWeaponPanels: the right side's first and last weapon panels; whether the reorder renumbers every left
        // panel and the bins; a reordered blank slot (it gets no type).
        protected virtual int FirstRightWeaponPanel => 8;
        protected virtual int LastRightWeaponPanel => 12;
        protected virtual bool LayoutRenumbersBins => true;
        protected virtual bool IsBlankSlot(WeaponSlot p_slot) => p_slot.m_type == 0 && p_slot.m_ammo == 0;

        // The frame's arc glitch from the derived cockpit's causes (p_pilot the local mech, or null).
        protected virtual bool ArcGlitchCause(Mech p_pilot) => false;

        // RunGameKey's cases of a derived cockpit (true when p_key is one).
        protected virtual bool RunOwnGameKey(int p_key) => false;

        // DrawWeaponPanel: whether a weapon panel shows; its text.
        protected virtual bool WeaponPanelShown(Panel p_panel) => p_panel.Enabled;
        protected virtual string WeaponPanelText(Panel p_panel, WeaponSlot p_weapon) =>
            p_weapon.m_ammo < 0 ? p_panel.Name : $"{p_panel.Name} {p_weapon.m_ammo}";

        // The objectives panel's words (the base game's given).
        protected virtual string ObjectiveWord(string p_word) => p_word;

        // The HUD: a derived cockpit's compass and crosshair (no altimeter, the pitch ignored by the lock and the
        // compass's arrows, the tape the other way, the route's nav marked); the map's route nav; the map's heading.
        protected virtual bool RouteHud => false;
        protected virtual bool MapDrawsRoute => false;
        protected virtual double MapHeadingDegrees(int p_heading) => p_heading / 65536.0;

        // RunGameKey's cockpit cases. Returns whether p_key is one.
        public bool RunGameKey(int p_key)
        {
            if (RunOwnGameKey(p_key)) return true;
            bool powered = (mech.PlayerFlags & 0x2000) != 0;
            switch (p_key)
            {
                case 0x2:
                    CycleMechViewMode();
                    if (powered) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xdc), 100, 0x40, 0, 0x32);
                    return true;
                case 0x3:
                    MechViewMode = MechViewMode == 2 ? 0 : 2;
                    if (powered) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xdc), 100, 0x40, 1, 0x32);
                    return true;
                case 0x4:
                    MechViewMode = MechViewMode == 3 ? 0 : 3;
                    if (powered) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xdc), 100, 0x40, 2, 0x32);
                    return true;
                case 0x5:
                    MechViewMode = MechViewMode == 4 ? 0 : 4;
                    if (powered) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xdc), 100, 0x40, 3, 0x32);
                    return true;
                case 0x6:
                    MechViewMode = MechViewMode == 5 ? 0 : 5;
                    if (powered) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xdc), 100, 0x40, 4, 0x32);
                    return true;
                case 0x7:
                    MechViewMode = MechViewMode == 1 ? 0 : 1;
                    if (powered) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xdc), 100, 0x40, 6, 0x32);
                    return true;
                case 0x8:
                    TargetPanelMode++;
                    if (TargetPanelMode > 2) TargetPanelMode = 0;
                    if (powered) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xdc), 100, 0x40, 7, 0x32);
                    return true;
                case 0x13:
                    ShowHud = !ShowHud;
                    if (powered) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xdc), 100, 0x40, 8, 0x32);
                    return true;
                case 0x2e:
                    CycleCockpitView();
                    return true;
                case 0x2f:
                    ZoomMapView(1);
                    return true;
                case 0x30:
                    ZoomMapView(2);
                    return true;
                case 0x31:
                    ZoomMapView(0);
                    return true;
                case 0x32:
                    ToggleSatelliteView();
                    return true;
                case 0x41:
                    ShowObjectives = !ShowObjectives;
                    return true;
            }

            return false;
        }
    }
}
