using System;
using UnityEngine;

namespace Demech2
{
    // The mech view panel (demech2 MW2/src/mechviewpanel.c, damagepanel.c): the damage outline (mode 1), the armor bars
    // (2, "HTAL"), the view behind (3), the view down (4) and the last shot's camera (5); and the target panel
    // (targetpanel.c): the target seen from behind it, or a nav's icon. The 3D views render into their panes
    // (ICockpitView.RenderViewToPane).
    public partial class Cockpit
    {
        // g_outlinePartSections: the section each part of the outline shows, plus one (0: none).
        static readonly int[] OutlinePartSections = { 1, 3, 3, 2, 2, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 0 };

        protected bool mechViewStatic, targetPanelStatic;
        Pane outlineRect;
        readonly (int x, int y)[] outlinePartOffsets = new (int, int)[16];
        protected (int x, int y) htalH = (0x1e1e, 0x199a), htalT = (0x5050, 0x199a), htalA = (0x9697, 0x199a), htalL = (0xd2d3, 0x199a);
        protected readonly (int x, int y)[] armorBarPositions = new (int, int)[8];
        protected readonly (int x, int y)[] fullSectionArmor = new (int, int)[8];
        protected (int x, int y) armorBarSize;
        protected int maxSectionArmor;

        // InitDamagePanel: the outline centered in the panel and its parts placed, the labels' and bars' places, and the
        // local mech's full armor.
        void InitDamagePanel()
        {
            Panel panel = panels[PanelMechView];
            HudShape shape = Art.ShapeOf(hudLayoutValues[0]);
            int width1 = 0, height1 = 0;
            if (shape != null)
            {
                int extent = shape.Resolution(0);
                width1 = extent >> 16;
                height1 = extent & 0xffff;
                shape = Art.ShapeOf(hudLayoutValues[0] + Art.g_artResolution);
            }

            if (shape != null && width1 > 0 && height1 > 0)
            {
                int extent = shape.Resolution(0);
                int width2 = extent >> 16, height2 = extent & 0xffff;
                outlineRect.X0 = (panel.Width - width2) / 2 + panel.X;
                outlineRect.Y0 = (panel.Height - height2) / 2 + panel.Y;
                outlineRect.X1 = outlineRect.X0 + width2 - 1;
                outlineRect.Y1 = outlineRect.Y0 + height2 - 1;
                for (int i = 15; i >= 0; i--)
                {
                    Pane part = outlinePartRects[i];
                    int minX = (int)((double)part.X0 / width1 * 65536.0 + 0.5), minY = (int)((double)part.Y0 / height1 * 65536.0 + 0.5);
                    int maxX = (int)((double)part.X1 / width1 * 65536.0 + 0.5), maxY = (int)((double)part.Y1 / height1 * 65536.0 + 0.5);
                    maxX += minX - 1;
                    maxY += minY - 1;
                    (minX, minY) = ScreenScale.ScalePointToFrame(outlineRect, minX, minY);
                    (maxX, maxY) = ScreenScale.ScalePointToFrame(outlineRect, maxX, maxY);
                    outlinePartRects[i] = new Pane(outlineRect.X0 + minX, minY + outlineRect.Y0, outlineRect.X0 + maxX, maxY + outlineRect.Y0);
                    outlinePartOffsets[i] = (-minX, -minY);
                }
            }

            int[] barX = { 0x2323, 0x7373, 0x5a5a, 0x4141, 0xaaab, 0x9192, 0xe1e2, 0xc8c9 };
            htalH = ScreenScale.ScalePointToFrame(panel.Target, htalH.x, htalH.y);
            htalT = ScreenScale.ScalePointToFrame(panel.Target, htalT.x, htalT.y);
            htalA = ScreenScale.ScalePointToFrame(panel.Target, htalA.x, htalA.y);
            htalL = ScreenScale.ScalePointToFrame(panel.Target, htalL.x, htalL.y);
            for (int i = 7; i >= 0; i--) armorBarPositions[i] = ScreenScale.ScalePointToFrame(panel.Target, barX[i], 0x5555);
            armorBarSize = ScreenScale.ScalePointToFrame(panel.Target, 0x1414, 0x8000);

            MechConfig config = mech.Config;
            if (config == null) return;
            for (int i = 0; i < 8; i++)
            {
                MechSection section = config.m_sections[i];
                fullSectionArmor[i] = (section.m_armor[0], section.m_armor[1]);
                if (fullSectionArmor[i].x > maxSectionArmor) maxSectionArmor = fullSectionArmor[i].x;
                if (fullSectionArmor[i].x > maxSectionArmor) maxSectionArmor = fullSectionArmor[i].x;
            }
        }

        // CycleMechViewMode: 1 to 5.
        void CycleMechViewMode()
        {
            MechViewMode++;
            if (MechViewMode == 6) MechViewMode = 1;
        }

        // DrawMechViewPanel: the mode's view; the 3D views flicker to static while the panel is damaged, and are static
        // past that.
        void DrawMechViewPanel(Panel p_panel)
        {
            if (!p_panel.Enabled || MechViewMode == 0) return;
            p_panel.LastPowerState = powerState;
            if (p_panel.ShownDamage == 1 && MechViewMode != 1 && MechViewMode != 2)
            {
                if (mechViewStatic)
                {
                    if (SimRandom.Cosmetic.RandomIntBelow(10) < 7) mechViewStatic = false;
                    DrawMechViewStatic(p_panel);
                    return;
                }

                if (SimRandom.Cosmetic.RandomIntBelow(10) < 3) mechViewStatic = true;
            }
            else if (p_panel.ShownDamage > 2 && MechViewMode != 1 && MechViewMode != 2)
            {
                DrawMechViewStatic(p_panel);
                return;
            }

            Pane target = p_panel.Target;
            switch (MechViewMode)
            {
                case 5:
                    // The tracked shot's view (the local player's last missile, tracked now if none is).
                    if (!Shots.GetTrackedShotView())
                    {
                        Shots.TrackLastShot();
                        Shots.GetTrackedShotView();
                    }

                    if (!Shots.g_hasTrackedView) hud.Wipe(target, 0);
                    else hud.Texture(target, view.RenderViewToPane(5, 2f, Shots.g_trackedPosition, Shots.g_trackedHeading, 0, 0, target, false));
                    DrawMechViewFrame(p_panel, 6, 0xfd);
                    break;
                case 4:
                {
                    // Down on the mech from its position, the mech hidden.
                    var at = new Vector3(mech.Position.x, mech.ObjectY, mech.Position.z);
                    hud.Texture(target, view.RenderViewToPane(5, 2f, at, view.EyeHeading, 90, 0, target, true));
                    DrawMechViewFrame(p_panel, 6, 0xf7);
                    break;
                }
                case 3:
                {
                    // Behind the mech, from the eye; with the "front" cheat (g_frontViewForRear) along the torso instead,
                    // outlined in place of the frame.
                    float rearHeading = Host.Cockpit.FrontViewForRear ? (mech.TorsoTwist + mech.HeadingFixed) / 65536f : mech.HeadingFixed / 65536f + 180f;
                    hud.Texture(target, view.RenderViewToPane(5, 2f, view.EyeUnits, rearHeading, 0, view.EyeRoll, target, true));
                    if (Host.Cockpit.FrontViewForRear) OutlinePane(target, 6);
                    else DrawMechViewFrame(p_panel, 6, 0xfa);
                    break;
                }
                case 2:
                    DrawArmorBars(target);
                    break;
                default:
                    DrawDamageOutline();
                    break;
            }
        }

        void DrawMechViewStatic(Panel p_panel)
        {
            if (!p_panel.Enabled || MechViewMode == 0) return;
            p_panel.LastPowerState = powerState;
            DrawAnim(p_panel.Target, 0, 0, 0);
        }

        // DrawMechViewFrame: the panel's outline and the view's label at its top.
        protected void DrawMechViewFrame(Panel p_panel, int p_color, int p_shape)
        {
            OutlinePane(p_panel.Target, p_color);
            DrawPaneShape(p_panel.Width >> 1, 2, p_shape, p_panel.Target);
        }

        // DrawMechViewStartup: the 3D views open through the panel's transition.
        void DrawMechViewStartup(Panel p_panel)
        {
            if (!p_panel.Enabled || MechViewMode == 0 || MechViewMode == 2 || MechViewMode == 1) return;
            RectTransition transition = p_panel.Transition;
            if (transition != null)
            {
                if (p_panel.LastPowerState != 1) transition.Start();
                Pane? frame = transition.UpdateByAxis(false);
                if (frame != null) DrawPanelIn(p_panel, 5, frame.Value, DrawMechViewPanel);
                else DrawMechViewPanel(p_panel);
            }

            p_panel.LastPowerState = powerState;
        }

        // DrawMechViewShutdown: and close through it.
        void DrawMechViewShutdown(Panel p_panel)
        {
            if (!p_panel.Enabled || MechViewMode == 0 || MechViewMode == 2 || MechViewMode == 1) return;
            RectTransition transition = p_panel.Transition;
            if (transition != null)
            {
                if (p_panel.LastPowerState != 0 && p_panel.LastPowerState != 3 && p_panel.LastPowerState != 4) transition.Start();
                Pane? frame = transition.UpdateByAxis(true);
                if (frame != null) DrawPanelIn(p_panel, 5, frame.Value, DrawMechViewPanel);
            }

            p_panel.LastPowerState = powerState;
        }

        // The panel drawn with its rectangle (and its render target's) moved to p_frame for the call.
        protected void DrawPanelIn(Panel p_panel, int p_slot, Pane p_frame, Action<Panel> p_draw)
        {
            Pane savedSlot = panes[p_slot], savedTarget = p_panel.Target;
            panes[p_slot] = p_frame;
            p_panel.Target = p_frame;
            p_draw(p_panel);
            panes[p_slot] = savedSlot;
            p_panel.Target = savedTarget;
        }

        // DrawDamageOutline: the outline, each part recolored by its section's damage: yellow, red as its armor goes,
        // black once destroyed.
        void DrawDamageOutline()
        {
            HudShape shape = Art.ShapeOf(hudLayoutValues[0] + Art.g_artResolution);
            if (shape == null) return;
            hud.Shape(outlineRect, shape, 0, 0, 0);
            MechConfig config = mech.Config;
            if (config == null) return;
            for (int i = 0; i < 16; i++)
            {
                Pane part = outlinePartRects[i];
                if (part.X1 - part.X0 + 1 <= 0) continue;
                if (Host.Cockpit.FrameOutlineParts) OutlinePane(part, 0xe);
                int color = OutlinePartColor(config, i);
                if (color != 6) hud.Shape(part, shape, 0, outlinePartOffsets[i].x, outlinePartOffsets[i].y, color);
            }
        }

        // A part's color by its section's damage: 6 (unchanged) undamaged, 3 hit, 0xb badly, 0 destroyed.
        protected static int OutlinePartColor(MechConfig p_config, int p_part)
        {
            int index = OutlinePartSections[p_part] - 1;
            if (index < 0) return 6;
            MechSection section = p_config.m_sections[index];
            int front = 0, rear = 0, color = 6;
            int scale = (section.m_flags & 0xf0) >> 4;
            if (scale != 0)
            {
                front = 15 - (int)(((long)section.m_internal + section.m_armor[1] / MechConfig.ArmorPerLevel(true)) * 3 / ((long)scale << 16));
            }

            front = front < 1 ? 0 : Math.Min(front, 15);
            scale = section.m_flags & 0xf;
            if (scale != 0)
            {
                rear = 15 - (int)(((long)section.m_internal + section.m_armor[0] / MechConfig.ArmorPerLevel(true)) * 3 / ((long)scale << 16));
            }

            rear = rear < 1 ? 0 : Math.Min(rear, 15);
            int level = Math.Max(front, rear);
            if ((section.m_flags & 0x2000) != 0) color = 0;
            else if (level > 11) color = 0xb;
            else if (level > 0) color = 3;
            return color;
        }

        // DrawArmorBars: the labels, and each section's front armor (and the torso sections' rear beside it), full height
        // for the section's full armor.
        protected void DrawArmorBars(Pane p_target)
        {
            VfxFont font = Art.HudFont;
            if (font != null)
            {
                Art.g_textColors[0xe] = 6;
                hud.String(p_target, htalH.x, htalH.y, font, "H", Art.g_textColors);
                hud.String(p_target, htalT.x, htalT.y, font, "T", Art.g_textColors);
                hud.String(p_target, htalA.x, htalA.y, font, "A", Art.g_textColors);
                hud.String(p_target, htalL.x, htalT.y, font, "L", Art.g_textColors);
                Art.g_textColors[0xe] = 0xe;
            }

            MechConfig config = mech.Config;
            if (config == null || maxSectionArmor == 0) return;
            for (int i = 0; i < 8; i++)
            {
                MechSection section = config.m_sections[i];
                int width = armorBarSize.x;
                if (i > 0 && i < 4) width /= 2;
                DrawArmorBar(p_target, section, i, 0, width, armorBarPositions[i].x);
                if (i > 0 && i < 4) DrawArmorBar(p_target, section, i, 1, width, armorBarPositions[i].x + width);
            }
        }

        void DrawArmorBar(Pane p_target, MechSection p_section, int p_index, int p_side, int p_width, int p_x)
        {
            int armor, color;
            int fullArmor = p_side == 0 ? fullSectionArmor[p_index].x : fullSectionArmor[p_index].y;
            if ((p_section.m_flags & 0x2000) != 0)
            {
                armor = 0;
                color = 0xf3;
            }
            else
            {
                armor = p_section.m_armor[p_side];
                color = fullArmor >> 2 >= armor ? 0xb : 3;
                armor = Fixed.MulDiv64(armor, armorBarSize.y, maxSectionArmor);
            }

            int full = Fixed.MulDiv64(fullArmor, armorBarSize.y, maxSectionArmor);
            int y = armorBarPositions[p_index].y;
            if (armor != 0) DrawVerticalBar(p_target, p_x, y + armor, p_width, armor, 0xf);
            if (full > armor) DrawVerticalBar(p_target, p_x, y + full, p_width, full - armor, color);
        }

        // ---- The target panel (targetpanel.c) ---------------------------------------------------------------------

        // DrawTargetPanelText's state: the target it last showed (g_lastPanelTarget), and until when an inspection's
        // result shows (g_targetFullNameTime).
        int lastPanelTarget, targetFullNameTime;

        // DrawTargetPanelText: the target's name, colored by its side (a nav by whether it's reached), and its range; an
        // inspection's result (the short name, or "Out of range") for two seconds; "Unknown" for a target that hides its
        // name until inspected (flag 0x100). Nothing without a target.
        protected void DrawTargetPanelText(Panel p_panel)
        {
            // Taken once per rendered frame (Targeting.s_inspectShown), not read every frame until the next sim step.
            int inspect = Targeting.s_inspectShown;
            Targeting.s_inspectShown = 0;
            if (!p_panel.Enabled) return;
            int kind = targetId & 0xf00;
            if (kind == 0 || (targetId & 0x1000) != 0) return;
            int index = targetId & 0xff, color = 0xe;
            World scene = SimMain.g_world;
            NavPoint nav = kind == 0x100 && index < world.CockpitNavs.Count ? world.CockpitNavs[index] : null;
            Player player = kind == 0x200 && index < world.CockpitPlayers.Count ? world.CockpitPlayers[index] : null;
            GameThing thing = kind == 0x400 && scene != null && index < scene.m_gameThings.Count ? scene.m_gameThings[index] : null;

            if (targetId != lastPanelTarget) targetFullNameTime = 0;
            if (inspect == 2)
            {
                targetFullNameTime = Host.Cockpit.RenderClock + 362;
                p_panel.SetName("Out of range");
                MissionSpeech.PlayCockpitSound(0x14, -1);
            }
            else if (inspect != 0)
            {
                targetFullNameTime = Host.Cockpit.RenderClock + 362;
                string shortName = "";
                if (nav != null) shortName = "";
                else if (thing != null) shortName = thing.m_shortName;
                else if (player != null) shortName = player.m_shortName;
                p_panel.SetName(shortName ?? "");
                if (nav != null || thing != null || player != null) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xdc), 100, 0x40, 5, 0x32);
                if (string.IsNullOrEmpty(p_panel.Name)) p_panel.SetName("Contents unknown");
            }

            if (targetFullNameTime == 0 || Host.Cockpit.RenderClock > targetFullNameTime)
            {
                targetFullNameTime = 0;
                if (nav != null)
                {
                    if ((nav.m_flags & 0x20) == 0 && (nav.m_flags & 0x100) != 0) p_panel.SetName("Unknown");
                    else p_panel.SetName(string.IsNullOrEmpty(nav.m_name) ? "Nav Point" : nav.m_name);
                }
                else if (thing != null)
                {
                    if ((thing.m_flags & 0x20) == 0 && (thing.m_flags & 0x100) != 0)
                    {
                        string anonymous = world.AnonymousInstallationName;
                        p_panel.SetName(string.IsNullOrEmpty(anonymous) ? "Unknown" : anonymous);
                    }
                    else p_panel.SetName(string.IsNullOrEmpty(thing.m_name) ? "Installation" : thing.m_name);
                }
                else if (player != null)
                {
                    if ((player.m_flags & 0x20) == 0 && (player.m_flags & 0x100) != 0) p_panel.SetName("Unknown");
                    else p_panel.SetName(string.IsNullOrEmpty(player.m_name) ? "Mech" : player.m_name);
                }
                else p_panel.SetName("");
            }

            int side = kind == 0x400 ? SimMain.ThingSide(index) : kind == 0x200 ? world.PlayerSide(index) : -1;
            if (side == 2) color = 6;
            else if (side == 1) color = 0xa;
            else if (kind == 0x100) color = nav != null && (nav.m_flags & 0x20) != 0 ? 1 : 2;

            VfxFont font = Art.HudFont;
            if (font == null) return;
            Art.g_textColors[0xe] = color;
            hud.String(p_panel.Target, 0, 0, font, p_panel.Name, Art.g_textColors);
            Art.g_textColors[0xe] = 0xe;
            int meters = targetRange / 100;
            string text = meters > 1000 ? $"\n{meters / 1000.0:0.00}k" : $"\n{meters,3}m";
            DrawWrappedText(hud, p_panel.Target, text, font);
            lastPanelTarget = targetId;
        }

        // DrawWrappedText: text word-wrapped into a pane inside the margins (1%), until it runs out of lines.
        public static void DrawWrappedText(ICockpitSurface hud, Pane p_target, string p_text, VfxFont p_font)
        {
            (int x, int y) origin = ScreenScale.ScalePointToFrame(p_target, 0x28f, 0x28f);
            int width = p_target.X1 - p_target.X0, height = p_target.Y1 - p_target.Y0;
            int lineHeight = p_font.Height, p = 0, end = p_text.Length;
            char[] buffer = (p_text + "\0").ToCharArray();
            while (p <= end && origin.y < height)
            {
                int line = p, x = origin.x;
                while (p <= end)
                {
                    if (buffer[p] == '\n') break;
                    x += p_font.CharacterWidth(buffer[p]);
                    if (x > width) break;
                    p++;
                }

                if (p <= end)
                {
                    if (buffer[p] == '\n') buffer[p] = '\0';
                    else
                    {
                        int breakAt = p;
                        while (p >= line && buffer[p] != ' ' && buffer[p] != '\t') p--;
                        if (p < line) p = breakAt;
                        buffer[p] = '\0';
                    }
                }

                int length = Array.IndexOf(buffer, '\0', line) - line;
                if (length > 0) hud.String(p_target, origin.x, origin.y, p_font, new string(buffer, line, length), Art.g_textColors);
                origin.y += lineHeight;
                p++;
            }
        }

        // DrawTargetPanel: black and framed without a target (no targeting is ported); a nav's icon; the target seen from
        // behind it (wireframe in mode 1) while running. Static while the panel is damaged.
        void DrawTargetPanel(Panel p_panel)
        {
            if (!p_panel.Enabled || TargetPanelMode == 0) return;
            p_panel.LastPowerState = powerState;
            if (p_panel.ShownDamage == 1)
            {
                if (targetPanelStatic)
                {
                    if (SimRandom.Cosmetic.RandomIntBelow(10) < 7) targetPanelStatic = false;
                    DrawTargetStatic(p_panel);
                    return;
                }

                if (SimRandom.Cosmetic.RandomIntBelow(10) < 3) targetPanelStatic = true;
            }
            else if (p_panel.ShownDamage > 2)
            {
                DrawTargetStatic(p_panel);
                return;
            }

            DrawTargetPanelContent(p_panel, 7);
        }

        // The target panel's content, rendered into view slot p_slot.
        protected void DrawTargetPanelContent(Panel p_panel, int p_slot)
        {
            int kind = targetId & 0xf00, index = targetId & 0xff;
            if (kind == 0 || (targetId & 0x1000) != 0)
            {
                hud.Wipe(p_panel.Target, 0);
                OutlinePane(p_panel.Target, 8);
                return;
            }

            if (kind == 0x100)
            {
                int centerX = (p_panel.Target.X1 - p_panel.Target.X0) / 2, centerY = (p_panel.Target.Y1 - p_panel.Target.Y0) / 2;
                bool reached = index < world.CockpitNavs.Count && (world.CockpitNavs[index].m_flags & 0x20) != 0;
                hud.Wipe(p_panel.Target, 0);
                DrawPaneShape(centerX, centerY, reached ? 0x103 : 0x100, p_panel.Target);
                OutlinePane(p_panel.Target, 8);
                return;
            }

            // A target object, seen along the bearing to it from three times its radius back: a player's from its
            // position (m_radius, the MGD's), a game thing's from its shape's center (GetShapeBounds).
            hud.Wipe(p_panel.Target, 0);
            if (powerState == 2)
            {
                float heading = targetHeading / 65536f;
                Vector3 center = targetPosition;
                float distance = 0;
                if (kind == 0x400)
                {
                    if (SimMain.g_world != null && Host.Cockpit.ThingShapeBounds(index, out Vector3 shapeCenter, out float radius))
                    {
                        center = shapeCenter;
                        distance = radius * 3;
                    }
                }
                else
                {
                    Mech target = SimMain.MechOf(index);
                    if (target != null) distance = target.m_radius * 3;
                }

                Vector3 units = center - new Vector3(Mathf.Sin(heading * Mathf.Deg2Rad), 0, Mathf.Cos(heading * Mathf.Deg2Rad)) * distance;
                // Mode 1 the target's wireframe (m_wireframe 1), else the view rendered.
                if (TargetPanelMode == 1) Host.Cockpit.DrawEnhancedTarget(hud, p_panel.Target, kind, index, units, heading, 2f, view.EyeCamera);
                else hud.Texture(p_panel.Target, view.RenderViewToPane(p_slot, 2f, units, heading, 0, 0, p_panel.Target, false));
            }

            OutlinePane(p_panel.Target, 8);
        }

        protected void DrawTargetStatic(Panel p_panel)
        {
            if (!p_panel.Enabled) return;
            p_panel.LastPowerState = powerState;
            DrawAnim(p_panel.Target, 0, 0, 0);
        }

        // DrawTargetPanelStartup: the panel opens through its transition.
        void DrawTargetPanelStartup(Panel p_panel)
        {
            if (!p_panel.Enabled || TargetPanelMode == 0) return;
            RectTransition transition = p_panel.Transition;
            if (transition != null)
            {
                if (p_panel.LastPowerState != 1) transition.Start();
                Pane? rect = transition.UpdateByAxis(false);
                if (rect != null) DrawPanelIn(p_panel, 7, rect.Value, DrawTargetPanel);
                else DrawTargetPanel(p_panel);
            }

            p_panel.LastPowerState = powerState;
        }

        // DrawTargetPanelShutdown: and closes through it.
        void DrawTargetPanelShutdown(Panel p_panel)
        {
            if (!p_panel.Enabled || TargetPanelMode == 0) return;
            RectTransition transition = p_panel.Transition;
            if (transition != null)
            {
                if (p_panel.LastPowerState != 0 && p_panel.LastPowerState != 3 && p_panel.LastPowerState != 4) transition.Start();
                Pane? rect = transition.UpdateByAxis(true);
                if (rect != null) DrawPanelIn(p_panel, 7, rect.Value, DrawTargetPanel);
            }

            p_panel.LastPowerState = powerState;
        }
    }
}
