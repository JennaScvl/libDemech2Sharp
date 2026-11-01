using UnityEngine;

namespace Demech2
{
    // The HUD (demech2 MW2/src/hud.c): the compass and its target markers at the top, the altimeter at the left, the
    // crosshair where the torso aims and the target's marker, drawn over the 3D view.
    //
    // Titanium 16-bit (mercsw.dll DrawHud 0x10032fe5) draws no altimeter, and compares the pitch with the fixed-point 3 as a
    // float of degrees, so the crosshair's lock ignores it and both compass arrows always show.
    public partial class Cockpit
    {
        static bool TitaniumHud => (Host.Variant.IsTitanium || Host.Variant.IsBattlePack);

        // The altimeter's and the compass's places, 16.16 fractions of their panels until InitHudGauges.
        (int x, int y) altimeterOrigin = (0xb333, 0x8000), compassOrigin = (0x8000, 0x6666);
        int altimeterMarkWidth = 0xf0f, altimeterMarkHeight = 0xd79;
        int compassArrowWidth, compassArrowHeight, compassSideArrowWidth, compassSideArrowHeight;
        int altimeterGroundX, altimeterTargetX, altimeterLevelX, altimeterScale, compassScale;
        int compassTapeAbove, compassTapeBelow;

        // The aim range (Player::m_aimRange).
        int AimRange => mech.AimRange;

        // InitHudGauges: the altimeter and the compass laid out from their shapes' extents.
        protected void InitHudGauges()
        {
            Pane target = panels[PanelAltimeter].Target;
            altimeterOrigin = ScreenScale.ScalePointToFrame(target, altimeterOrigin.x, altimeterOrigin.y);
            HudShape shape = Art.Shape11(Art.g_artResolution + 1);
            if (shape != null)
            {
                int extent = shape.Resolution(0);
                altimeterMarkWidth = extent >> 16;
                altimeterMarkHeight = extent & 0xffff;
            }

            altimeterLevelX = altimeterOrigin.x;
            altimeterGroundX = altimeterMarkWidth + altimeterLevelX;
            altimeterTargetX = altimeterMarkWidth + altimeterGroundX;
            shape = Art.Shape11(Art.g_artResolution + 7);
            if (shape != null)
            {
                int extent = shape.Resolution(0);
                altimeterOrigin.x -= extent >> 16;
                altimeterScale = ((extent & 0xffff) << 16) / 0xe8;
            }

            target = panels[PanelCompass].Target;
            compassOrigin = ScreenScale.ScalePointToFrame(target, compassOrigin.x, compassOrigin.y);
            shape = Art.Shape11(Art.g_artResolution + 0x19);
            if (shape != null) compassScale = ((shape.Resolution(0) >> 16) << 16) / 0x168;

            shape = Art.Shape11(Art.g_artResolution + 0x13);
            if (shape != null)
            {
                int extent = shape.Resolution(0) & 0xffff, origin = shape.Origin(0) & 0xffff;
                compassTapeBelow = extent - origin;
                compassTapeAbove = origin;
            }

            shape = Art.Shape11(Art.g_artResolution + 0x25);
            if (shape != null)
            {
                int extent = shape.Resolution(0);
                compassArrowWidth = extent >> 16;
                compassArrowHeight = extent & 0xffff;
            }

            shape = Art.Shape11(Art.g_artResolution + 0x1f);
            if (shape != null)
            {
                int extent = shape.Resolution(0);
                compassSideArrowWidth = extent >> 16;
                compassSideArrowHeight = extent & 0xffff;
            }
        }

        // DrawHud: the compass and its markers, the crosshair (only with the eye steady in the cockpit), the target
        // marker and the altimeter, as the display options allow. Headings, the twist and the bearings are in whole
        // degrees, the pitch 16.16, the distance in units.
        void DrawHud(int p_heading, int p_twist, int p_bearing, int p_twistBearing, int p_pitch, int p_distance, bool p_drawCrosshair)
        {
            if (!ShowHud) return;
            if (showCompass)
            {
                DrawCompass(p_heading, p_twist);
                DrawCompassMarkers(p_bearing, p_twistBearing, p_pitch);
            }

            if (p_drawCrosshair && showCrosshair) DrawCrosshair(p_twistBearing, p_pitch, p_distance);
            if (showTargetMarker)
            {
                DrawTargetMarker();
                bool targetIsNav = targetId != 0 && (targetId & 0x1000) == 0 && (targetId & 0xf00) == 0x100;
                if (TitaniumHud && !targetIsNav) DrawRouteNavMarker();
            }
            if (showAltimeter && !TitaniumHud) DrawAltimeter();
        }

        // DrawAltimeter: the tape at the mech's height, the marks of its level and of the ground below, and the target's
        // height, clamped to the gauge.
        void DrawAltimeter()
        {
            Panel gauge = panels[PanelAltimeter];
            Pane target = gauge.Target;
            int height = mech.ObjectY - mech.Height;
            int level = (Fixed.MulDiv64(altimeterScale, height - 20100, 100) >> 16) + altimeterOrigin.y;
            if (altimeterOrigin.y < level) DrawPaneShape(altimeterOrigin.x, altimeterOrigin.y, 0x115, target);
            DrawPaneShape(altimeterOrigin.x, level, 7, target);
            DrawPaneShape(altimeterLevelX, altimeterOrigin.y, 1, target);
            int mark = (Fixed.MulDiv64(altimeterScale, height - mech.GroundHeight, 100) >> 16) + altimeterOrigin.y;
            DrawPaneShape(altimeterGroundX, mark, 4, target);
            if ((targetId & 0xf00) == 0 || (targetId & 0x1000) != 0) return;

            // The target's height: a player's feet, a thing's object, a nav's place.
            int kind = targetId & 0xf00, targetY;
            if (kind == 0x200)
            {
                targetY = Host.Cockpit.MechRenderPosition(targetId & 0xff, out Vector3Int rendered) ? rendered.y : (int)targetPosition.y;
            }
            else if (kind == 0x400)
            {
                targetY = SimMain.g_world != null && Host.World.ThingStanding(targetId & 0xff, out Vector3Int at) ? (int)at.y : 0;
            }
            else if (kind == 0x100) targetY = (int)targetPosition.y;
            else return;

            int y = (Fixed.MulDiv64(altimeterScale, height - targetY, 100) >> 16) + altimeterOrigin.y;

            int x = altimeterTargetX, shape;
            if (y < 0)
            {
                y = 0;
                shape = 0x25;
                x += altimeterMarkWidth;
            }
            else if (gauge.Height < y)
            {
                y = gauge.Height;
                shape = 0x1c;
                x += altimeterMarkWidth;
            }
            else shape = 0x1f;

            DrawShapeOverPane(x, y, shape, target);
        }

        // DrawCrosshair: at the end of the aim ray, by the selected weapon: inoperative unless ready; an unguided
        // weapon's in-range shape while the target is in range and within 3 degrees of the aim, else its out-of-range
        // one; a guided weapon's locked, locking or seeking shape. Returns whether it's on the target.
        protected bool DrawCrosshair(int p_bearing, int p_pitch, int p_distance)
        {
            bool result = false;
            int shape;
            WeaponSlot slot = SelectedWeapon();
            if (slot != null && slot.m_state == WeaponSlot.Ready)
            {
                WeaponDef def = WeaponDef.Of(slot.m_type);
                int shapeIn, shapeOut;
                if (def.m_shotType == 3)
                {
                    shapeIn = 0x6a;
                    shapeOut = 0x6d;
                }
                else
                {
                    shapeOut = 0x76;
                    shapeIn = 0x73;
                }

                if (def.m_guided == 0)
                {
                    const int range = 3 << 16, pitch = 3;
                    if (targetId != 0 && (targetId & 0x1100) == 0 && def.m_shortRange < p_distance && def.m_longRange > p_distance &&
                        (TitaniumHud || (range > p_pitch && -range < p_pitch)) && pitch > p_bearing && -pitch < p_bearing)
                    {
                        result = true;
                        shape = shapeIn;
                    }
                    else shape = shapeOut;
                }
                else if ((mech.Flags & 0x80) != 0) shape = 0x61;
                else if ((mech.Flags & 0x8000) != 0) shape = 0x70;
                else shape = 0x6d;
            }
            else shape = 0x67;
            if (arcGlitch) shape = CrosshairShapes[new System.Random(ArcGlitchStep).Next(CrosshairShapes.Length)];

            if (!ProjectAimPoint(out int x, out int y))
            {
                crosshairSmoothed = false;
                return result;
            }

            // Not the original's: the aim point averaged over time, steadying its frame-to-frame jitter; once per
            // rendered frame, however many times it's drawn in one.
            Vector2 aim = new Vector2(x, y);
            if (!crosshairSmoothed) crosshairAt = aim;
            else if (crosshairFrame != Time.frameCount)
                crosshairAt = Vector2.Lerp(crosshairAt, aim, 1f - Mathf.Exp(-DeltaTime / CrosshairSmoothSeconds));
            crosshairFrame = Time.frameCount;
            crosshairSmoothed = true;
            DrawHudShape(Mathf.RoundToInt(crosshairAt.x), Mathf.RoundToInt(crosshairAt.y), shape);
            return result;
        }

        // Not the original's: every crosshair shape, one picked at random each arc-glitch step.
        static readonly int[] CrosshairShapes = { 0x61, 0x67, 0x6a, 0x6d, 0x70, 0x73, 0x76 };

        const float CrosshairSmoothSeconds = 0.1f;
        Vector2 crosshairAt;
        bool crosshairSmoothed;
        int crosshairFrame = -1;

        // DrawTargetMarker: brackets on a targeted mech or thing, a nav's marker, or the arrow at the edge toward one off
        // the screen.
        void DrawTargetMarker()
        {
            if (targetId == 0 || (targetId & 0x1000) != 0) return;
            int kind = targetId & 0xf00, index = targetId & 0xff;
            if (kind == 0x200)
            {
                // DrawPlayerBrackets: around the mech, by its radius.
                Mech target = SimMain.MechOf(index);
                if (target != null && Host.Cockpit.MechRenderObjectPosition(index, out Vector3 rendered))
                    DrawBrackets(rendered, target.m_radius, false, world.PlayerSide(index));
                return;
            }

            if (kind == 0x400)
            {
                // DrawObjectBrackets: around the thing's shape, by half its radius, at most half the screen apart.
                if (SimMain.g_world != null && Host.Cockpit.ThingShapeBounds(index, out Vector3 center, out float radius))
                    DrawBrackets(center, radius / 2, true, SimMain.ThingSide(index));
                return;
            }

            if (kind != 0x100) return;
            DrawNavMarker(targetPosition);
        }

        // A nav's marker on the view, or the arrow at the edge toward it.
        void DrawNavMarker(Vector3 p_position)
        {
            if (ProjectWorldPoint(p_position, out int x, out int y, out _)) DrawHudShape(x, y, 0xe5);
            else
            {
                (x, y) = GetRectNeedleToward(hud.Display, x, y);
                DrawHudShape(x, y, 0xeb);
            }
        }

        // DrawRouteNavMarker (0x10033674): Titanium marks the route's nav too, unless the target is a nav itself (a lost
        // or no target counts as not).
        void DrawRouteNavMarker()
        {
            if (routeId == 0 || (routeId & 0x1000) != 0 || (routeId & 0xf00) != 0x100) return;
            DrawNavMarker(routePosition);
        }

        // DrawPlayerBrackets, DrawObjectBrackets: the side's four corners around p_at, sized by p_radius and depth (an
        // object's at most half the screen apart, p_clamp), or an arrow at the edge toward it.
        void DrawBrackets(Vector3 p_at, float p_radius, bool p_clamp, int p_side)
        {
            if (!ProjectWorldPoint(p_at, out int x, out int y, out float z))
            {
                (x, y) = GetRectNeedleToward(hud.Display, x, y);
                DrawHudShape(x, y, p_side == 0 ? 0xeb : p_side == 2 ? 0xee : 0xe8);
                return;
            }

            int size = ProjectRadius(p_radius, z);
            int max = hud.Display.Width / 4;
            if (p_clamp && size > max) size = max;
            int topLeft, topRight, bottomLeft, bottomRight;
            switch (p_side)
            {
                case 0:
                    (topLeft, topRight, bottomLeft, bottomRight) = (0xb8, 0xc1, 0xca, 0xd3);
                    break;
                case 2:
                    (topLeft, topRight, bottomLeft, bottomRight) = (0xbe, 0xc7, 0xd0, 0xd9);
                    break;
                default:
                    (topLeft, topRight, bottomLeft, bottomRight) = (0xbb, 0xc4, 0xcd, 0xd6);
                    break;
            }

            DrawHudShape(x - size, y - size, topLeft);
            DrawHudShape(x + size, y - size, topRight);
            DrawHudShape(x - size, y + size, bottomLeft);
            DrawHudShape(x + size, y + size, bottomRight);
        }

        // DrawCompassMarkers: the target's bearing on the tape (from the torso unless it's a nav), the arrows above or below
        // while its pitch is beyond 3 degrees, and the side arrows (doubled past 90 degrees).
        void DrawCompassMarkers(int p_bearing, int p_twistBearing, int p_pitch)
        {
            Panel gauge = panels[PanelCompass];
            Pane target = gauge.Target;
            if ((targetId & 0x100) == 0) p_bearing = p_twistBearing;
            if (mech.Autopilot != 2)
            {
                if ((targetId & 0xf00) == 0 || (targetId & 0x1000) != 0) return;
                if (TitaniumHud || p_pitch > -(3 << 16))
                {
                    DrawShapeOverPane(compassOrigin.x, compassOrigin.y - compassArrowHeight - compassTapeAbove, 0x25, target);
                }

                if (TitaniumHud || p_pitch < 3 << 16)
                {
                    DrawShapeOverPane(compassOrigin.x, compassArrowHeight + compassOrigin.y + compassTapeBelow, 0x1c, target);
                }
            }

            if (p_bearing == 0) DrawPaneShape(compassOrigin.x, compassOrigin.y, 0x10, target);
            else DrawPaneShape(compassOrigin.x + Fixed.Mul16(compassScale, p_bearing), compassOrigin.y, 0xd, target);
            if (p_bearing == 0) DrawPaneShape(compassOrigin.x, compassOrigin.y, 0x16, target);

            if (p_bearing > -3)
            {
                int x = gauge.Width + compassSideArrowWidth - 1, y = compassOrigin.y + compassSideArrowHeight / 2;
                DrawShapeOverPane(x, y, 0x22, target);
                if (p_bearing > 0x5a) DrawShapeOverPane(x + 1, y, 0x22, target);
            }

            if (p_bearing < 3)
            {
                int x = -compassSideArrowWidth, y = compassOrigin.y + compassSideArrowHeight / 2;
                DrawShapeOverPane(x, y, 0x1f, target);
                if (p_bearing < -0x5a) DrawShapeOverPane(x - 1, y, 0x1f, target);
            }
        }

        // DrawCompass: the tape at the heading, twice to wrap around, the torso's twist as a bar from the center (a pixel
        // a degree), and the center mark.
        void DrawCompass(int p_heading, int p_twist)
        {
            Panel gauge = panels[PanelCompass];
            Pane target = gauge.Target;
            int heading = (p_heading + 360) % 360;
            int x, x2;
            if (TitaniumHud)
            {
                // Titanium (0x10033d97) runs the tape the other way and wraps on its left edge.
                x = compassOrigin.x - Fixed.Mul16(compassScale, heading);
                x2 = x < 0 ? x + Fixed.Mul16(compassScale, 360) : x - Fixed.Mul16(compassScale, 360);
            }
            else
            {
                x = compassOrigin.x + Fixed.Mul16(compassScale, heading);
                x2 = gauge.Width > x ? x + Fixed.Mul16(compassScale, 360) : x - Fixed.Mul16(compassScale, 360);
            }
            DrawPaneShape(x, compassOrigin.y, 0x19, target);
            DrawPaneShape(x2, compassOrigin.y, 0x19, target);
            if (p_twist != 0)
            {
                if (p_twist < 0)
                {
                    DrawHorizontalBar(target, compassOrigin.x + p_twist, compassOrigin.y - compassTapeAbove, -p_twist, compassTapeAbove, 0xf);
                }
                else DrawHorizontalBar(target, compassOrigin.x, compassOrigin.y - compassTapeAbove, p_twist, compassTapeAbove, 0xf);
            }

            DrawPaneShape(compassOrigin.x, compassOrigin.y, 0x13, target);
        }

        // ProjectAimPoint: the end of the aim ray on the 3D view.
        public bool ProjectAimPoint(out int p_x, out int p_y)
        {
            p_x = p_y = 0;
            if (!view.AimRay(out Vector3 origin, out Vector3 direction)) return false;
            return ProjectWorldPoint(origin + direction * AimRange, out p_x, out p_y, out _);
        }

        // ProjectWorldPoint: a world point (units) on the 3D view, relative to it. Returns whether it is in front of the
        // near plane and on the view.
        public bool ProjectWorldPoint(Vector3 p_units, out int p_x, out int p_y, out float p_depth)
        {
            bool front = view.ProjectToPage(p_units, out Vector2 page, out p_depth);
            Pane display = hud.Display;
            p_x = Mathf.FloorToInt(page.x) - display.X0;
            p_y = Mathf.FloorToInt(page.y) - display.Y0;
            return front && p_x >= 0 && p_x <= display.X1 - display.X0 && p_y >= 0 && p_y <= display.Y1 - display.Y0;
        }

        // ProjectRadius: a radius at a depth (units) in page pixels across (m_projectScaleX: the page's half width over
        // the tangent of half the view's width, which spans the 4:3 page).
        int ProjectRadius(float p_radius, float p_depth)
        {
            if (p_depth <= 0) return 0;
            float tanHalfWidth = Mathf.Tan(view.VerticalFov * 0.5f * Mathf.Deg2Rad) * 4f / 3f;
            return Mathf.RoundToInt(p_radius / p_depth * (hud.Width / 2f) / tanHalfWidth);
        }

        // DrawHudShape: frame 0 of 1.1's SHP p_id (plus the art resolution) at p_x, p_y of the 3D view.
        void DrawHudShape(int p_x, int p_y, int p_id) =>
            hud.Shape(hud.Display, Art.Shape11(p_id + Art.g_artResolution), 0, p_x, p_y);

        // DrawPaneShape: the same, in pane p_target.
        void DrawPaneShape(int p_x, int p_y, int p_id, Pane p_target) =>
            hud.Shape(p_target, Art.Shape11(p_id + Art.g_artResolution), 0, p_x, p_y);

        // DrawShapeOverPane: DrawHudShape's shape at p_x, p_y of p_target, unclipped by it.
        void DrawShapeOverPane(int p_x, int p_y, int p_id, Pane p_target)
        {
            Pane view = hud.Display;
            // Not the original's: it moves with p_target into the display's margins (HudPage.SpreadOf).
            Vector2 spread = hud.SpreadOf(p_target);
            DrawHudShape(p_target.X0 + p_x - view.X0 + Mathf.RoundToInt(spread.x), p_target.Y0 + p_y - view.Y0 + Mathf.RoundToInt(spread.y), p_id);
        }
    }
}
