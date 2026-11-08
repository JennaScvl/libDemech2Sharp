using UnityEngine;

namespace Demech2
{
    // The map views (demech2 MW2/src/cockpit.c, mapview.c, mapicon.c, navreached.c; targeting.c's layouts; screenscale.c's
    // gauge geometry): cockpit view 1, the small map in the top left, 2, the large map across the middle, and 4, the
    // satellite view over the whole screen, each a top-down view of the player's surroundings with icons for the navs,
    // the units and the target, the field of view and the range. The small and large maps turn with the mech's heading;
    // the satellite view is north up, over the world seen straight down (not the original's: as the world draws, without
    // fog, rather than its faces recolored and shaded by height). Its static stretches the view drawn small over the
    // screen; the icons and readouts stay sharp.
    public partial class Cockpit
    {
        // A cockpit view's layout (CockpitLayout).
        protected sealed class CockpitLayout
        {
            public Pane Viewport;          // 16.16 fractions of the screen until loaded
            public Pane SavedViewport;
            public int PaneSlot;
            public int[] Sounds = { -1, -1 };
            public RectTransition Transition;
            public int Range, FormattedRange = -1, StartRange, MinRange, MaxRange, Zoom;
            public int Font = 1;
            public string RangeLabel, RangeText = "", HeadingLabel = "Bearing: ", HeadingText = "";
            public int FormattedHeading = -1;
            public string ShortUnit = "m", LongUnit = "km";
            public (int x, int y) RangeTextOrigin, HeadingTextOrigin;
            public int[,] Icons = MapIcons;
            public int[] Anims = { -1, -1, -1, -1, -1 };
            public bool Ellipse;           // m_gauges: the ellipse's (2, 4, 6, 8) or the rectangle's (0, 3, 5, 7)
        }

        // The layouts' icons by row and side: 0 the center, 1 units, 2 the live target, 3 a dead one, 4 navs (unreached,
        // reached), 5 a nav as the target, 6 the target off the view.
        static readonly int[,] MapIcons =
        {
            { 0xac, 0xac, 0xac }, { 0x7c, 0x79, 0x7f }, { 0x91, 0x8e, 0x94 }, { 0x9a, 0x97, 0x9d }, { 0x85, 0x88, 0x85 },
            { 0xa0, 0x118, 0xa0 }, { 0xa6, 0xa3, 0xa9 },
        };

        // g_mapColors: [11] the field of view, [12] the gauge's outline.
        static readonly int[] MapColors = { 0xe, 0xa, 6, 0xf, 0xb, 0xf5, 2, 3, 0xf9, 0xff, 0xf0, 1, 2 };

        protected readonly CockpitLayout[] layouts = new CockpitLayout[6];
        protected int cockpitLayoutIndex, requestedCockpitView, previousCockpitView;
        int mapViewMode, previousMapViewMode;
        bool hudSettingsSaved, savedShowCrosshair = true, savedShowTargetMarker = true, savedShowHud = true;
        int satelliteStaticState, staticCleanUntil, staticNoiseUntil;
        bool mapDamageAnimShown;

        // g_satelliteClean (set by nothing), g_stretchPending: the satellite view drawn small this frame, to stretch
        // (mapTextureRect) over the screen.
        bool satelliteClean, stretchPending;
        Pane mapTextureRect;

        // g_mapFollowsFreeEye: the maps over the free eye (the "tinkerbell" cheat, not ported).
        public static bool MapFollowsFreeEye;

        // g_mapShadeBase (the planet's map shade base, 0 by default, less 1000): how far below the ground the satellite
        // view sees.
        protected const int MapShadeBase = -1000;

        // IsSatelliteView: the local cockpit in the satellite view.
        public static bool IsSatelliteView => Current != null && Current.cockpitLayoutIndex == 4;

        // LeaveSatelliteView.
        public void LeaveSatelliteView()
        {
            if (cockpitLayoutIndex == 4) ToggleSatelliteView();
        }

        // The satellite view's frame callback (SwitchCockpitView's 3): DrawCockpitView in place of the scene, then the
        // stretch's end (Blit): the HUD back as it was.
        void DrawSatelliteFrame()
        {
            if (cockpitLayoutIndex == 4) DrawCockpitView();
        }

        void EndStretch()
        {
            if (!stretchPending) return;
            ShowHud = savedShowHud;
            stretchPending = false;
        }

        // The map's projection (BeginMapView): units per pixel, the eye and its heading.
        protected int mapViewScale;
        protected Vector3 mapEye;
        protected float mapHeading;
        protected Pane mapPane;

        void CreateLayouts()
        {
            var center = new Pane(0x8000, 0x8000, 0x8000, 0x8000);
            var whole = new Pane(0, 0, 0x10000, 0x10000);
            var mapTransitionState = new RectTransition(0xb5, center, whole);
            layouts[1] = new CockpitLayout
            {
                Viewport = new Pane(0x51f, 0x51f, 0x428f, 0x570a),
                PaneSlot = 8,
                Transition = new RectTransition(0xb5, center, whole, mapTransitionState),
                Range = 0x30d40, StartRange = 0x30d40, MinRange = 0xc350, MaxRange = 0x61a80,
                RangeLabel = "R: ",
                RangeTextOrigin = (0, 0xa3d),
                Ellipse = true,
            };
            layouts[2] = new CockpitLayout
            {
                Viewport = new Pane(0x2148, 0, 0xdeb8, 0x10000),
                PaneSlot = 9,
                Transition = new RectTransition(0xb5, center, whole, mapTransitionState),
                Range = 0x30d40, StartRange = 0x30d40, MinRange = 0xc350, MaxRange = 0x61a80,
                RangeLabel = "R: ",
                RangeTextOrigin = (0x147b, 0x2148),
                Ellipse = true,
            };
            layouts[4] = new CockpitLayout
            {
                Viewport = whole,
                PaneSlot = 10,
                Sounds = new[] { 0xe8, 0xe8 },
                Transition = new RectTransition(0x21f, new Pane(0x599a, 0x599a, 0xa666, 0xa666), whole),
                Range = 0x186a0, StartRange = 0x186a0, MinRange = 0x186a, MaxRange = 0x186a0,
                RangeLabel = "Range: ",
                RangeTextOrigin = (0x28f, 0x28f),
                HeadingTextOrigin = (0x28f, 0xccd),
                Ellipse = false,
            };
        }

        // LoadCockpitLayout: the viewport on the screen (cockpit view 1's is the radar panel's), its text positions and
        // transition, then its range reset.
        void LoadCockpitLayout(int p_cockpit, CockpitLayout p_layout)
        {
            if (p_layout == null) return;
            cockpitLayoutIndex = p_cockpit;
            p_layout.Viewport = ScreenScale.ScaleRectToScreen(p_layout.Viewport);
            if (p_cockpit == 1)
            {
                panelPanes[0] = p_layout.Viewport;
                panels[PanelRadar].SetTarget(p_layout.Viewport);
            }

            panes[p_layout.PaneSlot] = p_layout.Viewport;
            p_layout.RangeTextOrigin = ScreenScale.ScalePointToFrame(p_layout.Viewport, p_layout.RangeTextOrigin.x, p_layout.RangeTextOrigin.y);
            p_layout.HeadingTextOrigin = ScreenScale.ScalePointToFrame(p_layout.Viewport, p_layout.HeadingTextOrigin.x, p_layout.HeadingTextOrigin.y);
            RectTransition transition = p_layout.Transition;
            if (transition != null)
            {
                if (p_cockpit == 4)
                {
                    transition.First = CenterRectOnScreen(ScaleRectToLowRes(transition.First));
                    transition.Second = CenterRectOnScreen(ScaleRectToLowRes(transition.Second));
                }
                else
                {
                    transition.First = ScreenScale.ScaleRectToFrame(p_layout.Viewport, transition.First);
                    transition.Second = ScreenScale.ScaleRectToFrame(p_layout.Viewport, transition.Second);
                }
            }

            ZoomMapView(0);
        }

        // ScaleRectToLowRes with the screen's pixel aspect: 320x200 coordinates.
        static Pane ScaleRectToLowRes(Pane p_rect)
        {
            int scale = Fixed.Div16(Art.g_pixelAspect, 0xd555);
            return new Pane(Fixed.Mul16(p_rect.X0, 319), Fixed.Mul16(Fixed.Mul16(p_rect.Y0, 199), scale),
                Fixed.Mul16(p_rect.X1, 319), Fixed.Mul16(Fixed.Mul16(p_rect.Y1, 199), scale));
        }

        // CenterRectOnScreen: a rectangle of p_rect's size, centered.
        static Pane CenterRectOnScreen(Pane p_rect)
        {
            int width = p_rect.X1 - p_rect.X0, height = p_rect.Y1 - p_rect.Y0;
            int left = (ScreenScale.g_screenWidthMinus1 - (width + 1) - 1) / 2, top = (ScreenScale.g_screenHeightMinus1 - (height + 1) - 1) / 2;
            return new Pane(left, top, width + left, height + top);
        }

        // InitCockpitViews: every layout placed, cockpit view 0 asking for view 1.
        protected void InitCockpitViews()
        {
            CreateLayouts();
            for (int i = 0; i < 6; i++) LoadCockpitLayout(i, layouts[i]);
            cockpitLayoutIndex = 0;
            requestedCockpitView = 1;
            previousCockpitView = 0;
        }

        // RunMapView: the map while running.
        void RunMapView()
        {
            previousMapViewMode = mapViewMode;
            mapViewMode = 4;
            if (cockpitLayoutIndex <= 2) DrawCockpitView();
            else if (cockpitLayoutIndex == 4 && requestedCockpitView <= 2) SwitchCockpitView();
        }

        // SwitchCockpitView: to the view asked for; 3 enters the satellite view (its sound and "Satellite link
        // established"), 5 leaves it. The satellite view keeps the crosshair and target marker off.
        bool SwitchCockpitView()
        {
            bool changed = false;
            if (cockpitLayoutIndex <= 2 && previousCockpitView == 4 && hudSettingsSaved)
            {
                showCrosshair = savedShowCrosshair;
                showTargetMarker = savedShowTargetMarker;
                hudSettingsSaved = false;
            }

            if (cockpitLayoutIndex == 4 && (Debris.g_localMechLost || (mech.PlayerFlags & 6) != 0)) requestedCockpitView = 5;
            if (cockpitLayoutIndex != requestedCockpitView)
            {
                changed = true;
                switch (requestedCockpitView)
                {
                    case 0:
                    case 1:
                    case 2:
                        previousCockpitView = cockpitLayoutIndex;
                        cockpitLayoutIndex = requestedCockpitView;
                        break;
                    case 4:
                        previousCockpitView = cockpitLayoutIndex;
                        cockpitLayoutIndex = 4;
                        if (!hudSettingsSaved)
                        {
                            savedShowCrosshair = showCrosshair;
                            showCrosshair = false;
                            savedShowTargetMarker = showTargetMarker;
                            showTargetMarker = false;
                            savedShowHud = ShowHud;
                            hudSettingsSaved = true;
                        }

                        break;
                    case 3:
                    {
                        requestedCockpitView = 4;
                        int sound = layouts[4].Sounds[0];
                        if (sound != -1) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(sound), 100, 0x40, 5, 0x50);
                        MissionSpeech.PlayCockpitSound(0x12, -1);
                        break;
                    }
                    case 5:
                    {
                        requestedCockpitView = previousCockpitView;
                        int sound = layouts[4].Sounds[1];
                        if (sound != -1) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(sound), 100, 0x40, 5, 0x50);
                        if (Eyepoint.GetViewMode() == Eyepoint.c_viewTrack) MissionSpeech.PlayCockpitSound(0x11, -1);
                        break;
                    }
                }
            }

            return changed;
        }

        // DrawCockpitView: the view switched as asked, then its map, damaged while the radar panel is.
        void DrawCockpitView()
        {
            SwitchCockpitView();
            if (panels[PanelRadar].ShownDamage != 0) DrawDamagedMapView();
            else DrawMapView();
        }

        // DrawMapView: the current view's map, looking down from its range on the player's eye object: the field of view,
        // the icons, the gauge's outline and, while running, the readouts.
        void DrawMapView()
        {
            if (cockpitLayoutIndex == 0) return;
            CockpitLayout layout = layouts[cockpitLayoutIndex];
            if (layout == null) return;

            Pane viewport = layout.Viewport;
            panes[layout.PaneSlot] = viewport;
            float angle = mech.TorsoTwist / 65536f, heading = mech.HeadingFixed / 65536f, poseHeading;
            switch (cockpitLayoutIndex)
            {
                case 1:
                case 2:
                    poseHeading = heading;
                    break;
                case 4:
                    poseHeading = 0;
                    angle += heading;
                    break;
                default:
                    poseHeading = 0;
                    angle = 0;
                    break;
            }

            // BeginMapView: the eye over the player's eye object (or the free eye), the range across the pane.
            Vector3 eyeUnits = MapFollowsFreeEye ? Eyepoint.g_eyepointUnits : view.EyeObjectUnits;
            mapEye = new Vector3(eyeUnits.x, layout.Range, eyeUnits.z);
            mapHeading = poseHeading;
            mapPane = viewport;
            mapViewScale = layout.Range / Mathf.Max(1, viewport.X1 - viewport.X0 + 1);

            if (cockpitLayoutIndex == 4)
            {
                if (stretchPending)
                {
                    // The static's small render stretched over the screen: through a texture.
                    hud.Wipe(viewport, world.GroundColor);
                    hud.Texture(viewport, view.RenderMapViewToPane(layout.PaneSlot, mapEye, layout.Range, layout.Range - MapShadeBase, viewport, mapTextureRect));
                }
                else
                {
                    // Not the original's: straight to the display under the HUD, cleared to the ground colour (the wipe).
                    view.RenderMapViewToScreen(layout.PaneSlot, mapEye, layout.Range, layout.Range - MapShadeBase, viewport,
                        Host.Cockpit.PaletteColor(world.GroundColor & 0xff));
                }
            }

            if (cockpitLayoutIndex == 1 && RadarBackdrop) FillGaugeEllipse(viewport, new Color32(0, 0, 0, 128));
            if (cockpitLayoutIndex != 4 || (mech.PlayerFlags & 0x16) == 0) DrawMapFieldOfView(layout, angle);
            DrawMapContents(layout);
            if (layout.Ellipse) DrawGaugeEllipse(viewport, MapColors[12]);
            if (mapViewMode == 4 && (cockpitLayoutIndex != 4 || satelliteStaticState <= 1)) DrawMapViewText(layout);
        }

        // ProjectMapPoint: a world point (units) on the map's pane, relative to it; whether it's below the eye and on
        // the pane.
        bool ProjectMapPoint(Vector3 p_units, out int p_x, out int p_y)
        {
            float radians = mapHeading * Mathf.Deg2Rad;
            Vector3 d = p_units - mapEye;
            float right = d.x * Mathf.Cos(radians) - d.z * Mathf.Sin(radians);
            float forward = d.x * Mathf.Sin(radians) + d.z * Mathf.Cos(radians);
            float depth = -d.y;
            int width = mapPane.X1 - mapPane.X0, height = mapPane.Y1 - mapPane.Y0;
            int scale = Mathf.Max(1, mapViewScale);
            p_x = (int)(right / scale) + width / 2;
            p_y = height - ((int)(forward * Art.g_pixelAspect / 65536f / scale) + height / 2);
            return depth > 0 && p_x >= 0 && p_x <= width && p_y >= 0 && p_y <= height;
        }

        // m_gauges[1]: whether a pane point is in the view (the ellipse's inside, or anywhere for the rectangle).
        static bool InsideGauge(CockpitLayout p_layout, Pane p_viewport, int p_x, int p_y) =>
            !p_layout.Ellipse || IsInsideGaugeEllipse(p_viewport, p_x, p_y);

        // DrawMapContents: the navs, the center icon and the units (not in the satellite view), and the target.
        protected void DrawMapContents(CockpitLayout p_layout)
        {
            Pane viewport = p_layout.Viewport;
            DrawMapNavPoints(p_layout);
            if (cockpitLayoutIndex != 4)
            {
                int x = (viewport.X1 - viewport.X0 + 1) >> 1, y = (viewport.Y1 - viewport.Y0 + 1) >> 1;
                HudShape shape = Art.Shape11(p_layout.Icons[0, 0] + Art.g_artResolution);
                if (shape != null)
                {
                    hud.Shape(viewport, shape, 0, x, y);
                    DrawMapUnits(p_layout);
                }
            }

            DrawMapTarget(p_layout);
        }

        // DrawMapUnits: the other live players (not the local one) and the standing game things the radar shows (flags 0x400 or 0x1000), by
        // side.
        void DrawMapUnits(CockpitLayout p_layout)
        {
            for (int i = 0; i < world.CockpitPlayers.Count; i++)
            {
                // The local player (a networked match's own slot, not always the first).
                if (i == SimMain.g_localPlayerId) continue;
                Player player = world.CockpitPlayers[i];
                int flags = player.m_flags;
                if ((flags & 0x1400) == 0 || (flags & 0x800) != 0 || (flags & 0x4000) != 0 || (flags & 6) != 0 ||
                    Host.World.HiddenShutDown(player)) continue;
                Vector3 at = Host.Cockpit.MechRenderPosition(i, out Vector3Int rendered) ? (Vector3)rendered : new Vector3(player.m_position.x, player.m_position.y, player.m_position.z);
                DrawMapIcon(p_layout, at,
                    p_layout.Icons[1, Mathf.Clamp(world.PlayerSide(i), 0, 2)]);
            }

            World scene = SimMain.g_world;
            if (scene == null) return;
            for (int i = 0; i < scene.m_gameThings.Count; i++)
            {
                GameThing thing = scene.m_gameThings[i];
                if ((thing.m_flags & 0x1400) == 0 || (thing.m_flags & 0x1e) != 0) continue;
                if (!Host.World.ThingStanding(i, out Vector3Int at)) continue;
                DrawMapIcon(p_layout, at, p_layout.Icons[1, Mathf.Clamp(SimMain.ThingSide(i), 0, 2)]);
            }
        }

        // DrawMapIcon: icon p_icon at a world point, if the view shows it.
        void DrawMapIcon(CockpitLayout p_layout, Vector3 p_units, int p_icon)
        {
            Pane viewport = p_layout.Viewport;
            if (!ProjectMapPoint(p_units, out int x, out int y) || !InsideGauge(p_layout, viewport, x, y)) return;
            HudShape shape = Art.Shape11(p_icon + Art.g_artResolution);
            if (shape != null) hud.Shape(viewport, shape, 0, x, y);
        }

        // DrawMapTarget: the player's target, in the view or at its edge; with MapDrawsRoute then the route's nav, the
        // same way, unless the first pass gave up.
        void DrawMapTarget(CockpitLayout p_layout)
        {
            if (DrawMapTargetPass(p_layout, targetId, targetPosition) && MapDrawsRoute) DrawMapTargetPass(p_layout, routeId, routePosition);
        }

        bool DrawMapTargetPass(CockpitLayout p_layout, int p_id, Vector3 p_position)
        {
            Pane viewport = p_layout.Viewport;
            bool visible = ProjectMapPoint(p_position, out int x, out int y);
            if (visible) visible = InsideGauge(p_layout, viewport, x, y);
            if (p_id == 0 || (p_id & 0x1000) != 0) return true;

            int kind = p_id & 0xf00, index = p_id & 0xff, side, row;
            switch (kind)
            {
                case 0x200:
                    side = world.PlayerSide(index);
                    int flags = index < world.CockpitPlayers.Count ? world.CockpitPlayers[index].m_flags : 0;
                    row = (flags & 0x1400) != 0 && (flags & 0x800) == 0 ? 2 : 3;
                    break;
                case 0x400:
                {
                    side = SimMain.ThingSide(index);
                    World scene = SimMain.g_world;
                    int thingFlags = scene != null && index < scene.m_gameThings.Count ? scene.m_gameThings[index].m_flags : 0;
                    row = (thingFlags & 0x1400) != 0 ? 2 : 3;
                    break;
                }
                case 0x100:
                    side = visible && index < world.CockpitNavs.Count && (world.CockpitNavs[index].m_flags & 0x20) != 0 ? 1 : 0;
                    row = 5;
                    break;
                default:
                    return false;
            }

            if (!visible) row = 6;
            int icon = p_layout.Icons[row, Mathf.Clamp(side, 0, 2)];
            if (icon == 0) return false;
            HudShape shape = Art.Shape11(Art.g_artResolution + icon);
            if (shape == null) return false;
            if (visible) hud.Shape(viewport, shape, 0, x, y);
            else
            {
                (x, y) = p_layout.Ellipse ? GetEllipseNeedleToward(viewport, x, y) : GetRectNeedleToward(viewport, x, y);
                hud.Shape(viewport, shape, 0, x, y);
            }

            return true;
        }

        // DrawMapNavPoints: the local team's navs (in use, not hidden), the reached ones marked.
        void DrawMapNavPoints(CockpitLayout p_layout)
        {
            Pane viewport = p_layout.Viewport;
            foreach (NavPoint nav in world.CockpitNavs)
            {
                if (nav.m_team != world.LocalStar || nav.m_used == 0 || (nav.m_flags & 1) != 0) continue;
                Vector3 position = nav.m_obj != null ? view.UnitsOf(nav.m_obj) : (Vector3)nav.m_position;
                if (!ProjectMapPoint(position, out int x, out int y) || !InsideGauge(p_layout, viewport, x, y)) continue;
                int icon = p_layout.Icons[4, (nav.m_flags & 0x20) != 0 ? 1 : 0];
                if (icon == -1) continue;
                HudShape shape = Art.Shape11(Art.g_artResolution + icon);
                if (shape != null) hud.Shape(viewport, shape, 0, x, y);
            }
        }

        // DrawMapFieldOfView: two lines from the center to the edge, the view's half width (the eyepoint's, by its zoom)
        // either side of p_heading.
        protected void DrawMapFieldOfView(CockpitLayout p_layout, float p_heading)
        {
            Pane viewport = p_layout.Viewport;
            p_heading = ((p_heading % 360f) + 360f) % 360f;
            int x = (viewport.X1 - viewport.X0 + 1) >> 1, y = (viewport.Y1 - viewport.Y0 + 1) >> 1;
            float halfFov = Mathf.Atan2(1f, Eyepoint.g_fovX) * Mathf.Rad2Deg;
            p_heading = 90f - p_heading;
            (int x, int y) end = p_layout.Ellipse ? GetEllipseNeedleAt(viewport, p_heading - halfFov) : GetRectNeedleAt(viewport, p_heading - halfFov);
            hud.Line(viewport, x, y, end.x, end.y, MapColors[11]);
            end = p_layout.Ellipse ? GetEllipseNeedleAt(viewport, halfFov + p_heading) : GetRectNeedleAt(viewport, halfFov + p_heading);
            hud.Line(viewport, x, y, end.x, end.y, MapColors[11]);
        }

        // DrawMapViewText: the range readout (half the range across, in m or km) and, in the satellite view, the heading.
        void DrawMapViewText(CockpitLayout p_layout)
        {
            Pane viewport = p_layout.Viewport;
            VfxFont font = Art.FontOf(p_layout.Font + Art.g_artResolution);
            if (font == null) return;
            if (p_layout.Range != p_layout.FormattedRange)
            {
                int range = p_layout.Range / 2;
                string units;
                if (range >= 100000)
                {
                    range = Fixed.Div16(range, 100000);
                    units = p_layout.LongUnit;
                }
                else
                {
                    range = Fixed.Div16(range, 100);
                    units = p_layout.ShortUnit;
                }

                p_layout.RangeText = p_layout.RangeLabel + (range / 65536.0).ToString("0.0").PadLeft(3) + units;
                p_layout.FormattedRange = p_layout.Range;
            }

            hud.String(viewport, p_layout.RangeTextOrigin.x, p_layout.RangeTextOrigin.y, font, p_layout.RangeText, Art.g_textColors);
            if (cockpitLayoutIndex == 4)
            {
                int heading = (mech.HeadingFixed % 0x1680000 + 0x1680000) % 0x1680000;
                if (p_layout.FormattedHeading != heading)
                {
                    double degrees = MapHeadingDegrees(heading);
                    p_layout.HeadingText = p_layout.HeadingLabel + degrees.ToString("0.0").PadLeft(3);
                    p_layout.FormattedHeading = heading;
                }

                hud.String(viewport, p_layout.HeadingTextOrigin.x, p_layout.HeadingTextOrigin.y, font, p_layout.HeadingText, Art.g_textColors);
            }
        }

        // CycleCockpitView: no map, the small map, the large map.
        void CycleCockpitView()
        {
            if (cockpitLayoutIndex <= 2 && requestedCockpitView <= 2) requestedCockpitView = (cockpitLayoutIndex + 1) % 3;
        }

        // ToggleSatelliteView: out of the satellite view, or into it from a cockpit view.
        void ToggleSatelliteView()
        {
            if (cockpitLayoutIndex == 4 && requestedCockpitView == 4) requestedCockpitView = 5;
            else if (cockpitLayoutIndex <= 2 && requestedCockpitView <= 2) requestedCockpitView = 3;
        }

        // ZoomMapView: 0 resets the range, 1 halves it (to the longest below the shortest), 2 doubles it (to the shortest
        // past the longest).
        void ZoomMapView(int p_zoom)
        {
            CockpitLayout layout = cockpitLayoutIndex >= 0 && cockpitLayoutIndex < 6 ? layouts[cockpitLayoutIndex] : null;
            if (layout == null) return;
            switch (p_zoom)
            {
                case 0:
                    layout.Range = layout.StartRange;
                    break;
                case 1:
                    layout.Range = Fixed.Div16(layout.Range, 0x20000);
                    if (layout.MinRange > layout.Range) layout.Range = layout.MaxRange;
                    break;
                case 2:
                    layout.Range = Fixed.Mul16(layout.Range, 0x20000);
                    if (layout.MaxRange < layout.Range) layout.Range = layout.MinRange;
                    break;
            }

            layout.Zoom = Fixed.Div16(layout.StartRange, layout.Range);
        }

        // DrawMapViewTransition: the view drawn into the transition's rectangle; false once it's over.
        bool DrawMapViewTransition(bool p_reverse, CockpitLayout p_layout, RectTransition p_transition, bool p_final)
        {
            bool result = true;
            Pane? rect = p_transition.Update(p_reverse);
            if (rect == null)
            {
                result = false;
                if (p_final) rect = p_reverse ? p_transition.First : p_transition.Second;
            }

            if (rect != null && p_final && cockpitLayoutIndex == 4)
            {
                // The satellite view drawn into the rectangle, then stretched over the screen (Blit).
                mapTextureRect = rect.Value;
                stretchPending = true;
                DrawMapView();
            }
            else if (rect != null)
            {
                p_layout.SavedViewport = p_layout.Viewport;
                p_layout.Viewport = rect.Value;
                DrawMapView();
                p_layout.Viewport = p_layout.SavedViewport;
            }

            return result;
        }

        // PowerUpMapView: the map's rectangle opens while the mech starts up, then the map draws.
        void PowerUpMapView()
        {
            if (cockpitLayoutIndex <= 2)
            {
                SwitchCockpitView();
                if (cockpitLayoutIndex > 2) return;
                if (cockpitLayoutIndex != 0)
                {
                    previousMapViewMode = mapViewMode;
                    mapViewMode = 1;
                }

                CockpitLayout layout = layouts[cockpitLayoutIndex];
                RectTransition transition = layout?.Transition;
                if (transition == null) return;
                if (previousMapViewMode != mapViewMode) transition.Start();
                if (!DrawMapViewTransition(false, layout, transition, false)) DrawCockpitView();
            }
            else if (cockpitLayoutIndex == 4 && requestedCockpitView <= 2) SwitchCockpitView();
        }

        // DrawSatelliteStatic: the satellite view clean for a random while, then broken up (its transition jittered) for
        // another, the HUD off.
        void DrawSatelliteStatic()
        {
            CockpitLayout layout = layouts[cockpitLayoutIndex];
            RectTransition transition = layout?.Transition;
            if (transition == null) return;
            if (SimMenus.GetMenuSlotState(4) != 0 || satelliteClean)
            {
                satelliteClean = false;
                DrawMapView();
                return;
            }

            switch (satelliteStaticState)
            {
                case 0:
                    staticCleanUntil = Host.Cockpit.RenderClock + SimRandom.Cosmetic.RandomIntBelow(transition.Duration);
                    satelliteStaticState = 1;
                    goto case 1;
                case 1:
                    if (staticCleanUntil > Host.Cockpit.RenderClock)
                    {
                        DrawMapView();
                        break;
                    }

                    satelliteStaticState = 2;
                    staticNoiseUntil = Host.Cockpit.RenderClock + SimRandom.Cosmetic.RandomIntBelow(transition.Duration);
                    transition.Start();
                    ShowHud = false;
                    goto case 2;
                case 2:
                    transition.StateElapsed = SimRandom.Cosmetic.RandomIntBelow(Mathf.Max(1, transition.Duration - Clock.g_deltaTime - 1));
                    DrawMapViewTransition(true, layout, transition, true);
                    if (staticNoiseUntil < Host.Cockpit.RenderClock) satelliteStaticState = 0;
                    break;
            }
        }

        // DrawDamagedMapView: the satellite view through its static; the maps under their damage animation (none in the
        // shipped layouts).
        void DrawDamagedMapView()
        {
            CockpitLayout layout = layouts[cockpitLayoutIndex];
            if (layout == null) return;
            if (cockpitLayoutIndex == 4)
            {
                DrawSatelliteStatic();
                return;
            }

            int anim = layout.Anims[2];
            if (anim == -1)
            {
                DrawMapView();
                return;
            }

            if (panels[PanelRadar].ShownDamage == 1) mapDamageAnimShown = SimRandom.Cosmetic.RandomIntBelow(10) < (mapDamageAnimShown ? 7 : 3);
            else if (panels[PanelRadar].ShownDamage == 2 || arcGlitch) mapDamageAnimShown = true;
            DrawMapView();
            if (mapDamageAnimShown)
            {
                DrawAnim(layout.Viewport, anim, 0, 0);
                if (layout.Ellipse) DrawGaugeEllipse(layout.Viewport, MapColors[12]);
            }
        }

        // PowerDownMapView: the map's rectangle closes while the mech shuts down.
        void PowerDownMapView()
        {
            if (cockpitLayoutIndex <= 2)
            {
                SwitchCockpitView();
                if (cockpitLayoutIndex > 2) return;
                if (cockpitLayoutIndex != 0)
                {
                    previousMapViewMode = mapViewMode;
                    mapViewMode = 3;
                }

                CockpitLayout layout = layouts[cockpitLayoutIndex];
                RectTransition transition = layout?.Transition;
                if (transition == null) return;
                if (previousMapViewMode != mapViewMode) transition.Start();
                DrawMapViewTransition(true, layout, transition, false);
            }
            else if (cockpitLayoutIndex == 4 && requestedCockpitView <= 2) SwitchCockpitView();
        }

        // ---- Gauge geometry (screenscale.c) -----------------------------------------------------------------------

        // DrawGaugeEllipse: the ellipse inscribed in the pane, its height by the pixel aspect.
        void DrawGaugeEllipse(Pane p_target, int p_color)
        {
            int centerX = (p_target.X1 - p_target.X0 + 1) >> 1, centerY = (p_target.Y1 - p_target.Y0 + 1) >> 1;
            int radius = (p_target.X1 - p_target.X0 + 1) / 2 - 1;
            hud.Ellipse(p_target, centerX, centerY, radius, Fixed.Mul16(radius, Art.g_pixelAspect), p_color);
        }

        // Not the original's: DrawGaugeEllipse's ellipse filled (the radar backdrop).
        void FillGaugeEllipse(Pane p_target, Color32 p_color)
        {
            int centerX = (p_target.X1 - p_target.X0 + 1) >> 1, centerY = (p_target.Y1 - p_target.Y0 + 1) >> 1;
            int radius = (p_target.X1 - p_target.X0 + 1) / 2 - 1;
            hud.FillEllipse(p_target, centerX, centerY, radius, Fixed.Mul16(radius, Art.g_pixelAspect), p_color);
        }

        // IsInsideGaugeEllipse.
        static bool IsInsideGaugeEllipse(Pane p_target, int p_x, int p_y)
        {
            int centerX = (p_target.X1 - p_target.X0 + 1) >> 1, centerY = (p_target.Y1 - p_target.Y0 + 1) >> 1;
            int radius = (p_target.X1 - p_target.X0 + 1) / 2 - 1;
            int dx = p_x - centerX, dy = Fixed.Div16(p_y - centerY, Art.g_pixelAspect);
            return (long)dy * dy + (long)dx * dx <= (long)radius * radius;
        }

        // GetEllipseEdgeAtAngle's radii: the ellipse's (its height by the pixel aspect), a pixel in.
        static (int radius, int radiusY) NeedleRadii(Pane p_target)
        {
            int radius = (p_target.X1 - p_target.X0 + 1) / 2 - 1;
            return (radius - 1, Fixed.Mul16(radius, Art.g_pixelAspect) - 1);
        }

        // GetEllipseNeedleToward: where the needle from the center toward a pane point meets the ellipse.
        static (int x, int y) GetEllipseNeedleToward(Pane p_target, int p_x, int p_y)
        {
            int halfX = (p_target.X1 - p_target.X0 + 1) >> 1, halfY = (p_target.Y1 - p_target.Y0 + 1) >> 1;
            float dx = halfX - p_x, dy = halfY - p_y;
            (int radius, int radiusY) = NeedleRadii(p_target);
            if (dx == 0) return (halfX, dy >= 0 ? halfY - radiusY : halfY + radiusY);
            float angle = Mathf.Atan2(dy * 65536f / Art.g_pixelAspect, dx);
            return (halfX - (int)(radius * Mathf.Cos(angle)), halfY - (int)(radiusY * Mathf.Sin(angle)));
        }

        // GetEllipseNeedleAt: where the needle at heading p_angle (degrees, 0 to the right, counterclockwise) meets the
        // ellipse.
        static (int x, int y) GetEllipseNeedleAt(Pane p_target, float p_angle)
        {
            p_angle = ((p_angle % 360f) + 360f) % 360f;
            int halfX = (p_target.X1 - p_target.X0 + 1) >> 1, halfY = (p_target.Y1 - p_target.Y0 + 1) >> 1;
            (int radius, int radiusY) = NeedleRadii(p_target);
            float radians = p_angle * Mathf.Deg2Rad;
            float dx = Mathf.Cos(radians), dy = Mathf.Sin(radians) * Art.g_pixelAspect / 65536f;
            float angle = Mathf.Atan2(dy, -dx);
            return (halfX - (int)(radius * Mathf.Cos(angle)), halfY - (int)(radiusY * Mathf.Sin(angle)));
        }

        // GetRectNeedleToward: where the needle from the center toward a pane point leaves the pane.
        protected static (int x, int y) GetRectNeedleToward(Pane p_target, int p_x, int p_y)
        {
            int halfX = (p_target.X1 - p_target.X0 + 1) >> 1, halfY = (p_target.Y1 - p_target.Y0 + 1) >> 1;
            int dx = halfX - p_x, dy = halfY - p_y;
            int quadrant = (dx >= 0 ? 1 : 0) | (dy >= 0 ? 2 : 0);
            bool steep = dx == 0 || dy / dx > 0x7fff || dy / dx < -0x8000;
            int slope = steep ? 0 : Fixed.Div16(dy, dx);
            return GetRectEdgeAtSlope(halfX, halfY, quadrant | (steep ? 4 : 0), slope);
        }

        // GetRectNeedleAt: where the needle at heading p_angle (degrees) leaves the pane.
        static (int x, int y) GetRectNeedleAt(Pane p_target, float p_angle)
        {
            p_angle = ((p_angle % 360f) + 360f) % 360f;
            int halfX = (p_target.X1 - p_target.X0 + 1) >> 1, halfY = (p_target.Y1 - p_target.Y0 + 1) >> 1;
            int quadrant = (p_angle >= 90 && p_angle <= 270 ? 1 : 0) | (p_angle <= 180 ? 2 : 0);
            float radians = p_angle * Mathf.Deg2Rad;
            float dx = Mathf.Cos(radians), dy = Mathf.Sin(radians) * Art.g_pixelAspect / 65536f;
            bool steep = Mathf.Abs(dx) < 1e-7f || Mathf.Abs(dy / dx) > 0x7fff;
            int slope = steep ? 0 : -(int)(dy / dx * 65536f);
            return GetRectEdgeAtSlope(halfX, halfY, quadrant | (steep ? 4 : 0), slope);
        }

        // GetRectEdgeAtSlope: where a line of slope p_slope (16.16) from the center leaves the rectangle, by direction.
        static (int x, int y) GetRectEdgeAtSlope(int p_halfX, int p_halfY, int p_quadrant, int p_slope)
        {
            int width = p_halfX * 2, height = p_halfY * 2;
            int ratio = p_halfX == 0 ? 0 : Fixed.Div16(p_halfY, p_halfX), negRatio = -ratio;
            switch (p_quadrant)
            {
                case 3:
                    return ratio < p_slope ? (p_halfX - Fixed.Div16(p_halfY, p_slope), 0) : (0, p_halfY - Fixed.Mul16(p_halfX, p_slope));
                case 2:
                    return p_slope < negRatio ? (p_halfX - Fixed.Div16(p_halfY, p_slope), 0) : (width, p_halfY + Fixed.Mul16(p_halfX, p_slope));
                case 0:
                    return ratio < p_slope ? (p_halfX + Fixed.Div16(p_halfY, p_slope), height) : (width, p_halfY + Fixed.Mul16(p_halfX, p_slope));
                case 1:
                    return p_slope < negRatio ? (p_halfX + Fixed.Div16(p_halfY, p_slope), height) : (0, p_halfY - Fixed.Mul16(p_halfX, p_slope));
                case 7:
                    return (p_halfX, 0);
                case 5:
                    return (p_halfX, height);
                default:
                    return (0, 0);
            }
        }
    }
}
