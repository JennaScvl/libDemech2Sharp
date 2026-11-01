using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // The cockpit's text panels and bar gauges (demech2 MW2/src/weaponpanel.c, statuspanels.c, bargauges.c).
    public partial class Cockpit
    {
        // EasedValue: each step closes 1 / 2^shift of the way to the target. The original steps once a frame; here a
        // step every 30th of a second, whatever the frame rate.
        protected sealed class EasedValue
        {
            public int Value, Target;
            readonly int shift;
            float pending;

            public EasedValue(int p_shift) => shift = p_shift;

            // UpdateEasedValue.
            public int Update()
            {
                pending += DeltaTime * 30f;
                while (pending >= 1f)
                {
                    pending -= 1f;
                    Value += (Target - Value) >> shift;
                }

                return Value;
            }
        }

        protected readonly EasedValue heatBarLevel = new EasedValue(1), throttleBarLevel = new EasedValue(1);
        protected readonly EasedValue heatRateBarLevel = new EasedValue(4), jumpFuelBarLevel = new EasedValue(1);

        // The gauges' rectangles: 16.16 fractions of their panels until ScaleBarGauges.
        (int x, int y) throttleGaugeSize = (0x1e7a, 0x8dc9);
        int throttleFrameLeft, throttleFrameTop, throttleFrameRight, throttleFrameBottom, throttleBarLeft, throttleZeroY;
        (int x, int y) heatBarPosition = (0, 0x3333), heatBarSize = (0xea4e, 0x5555);
        (int x, int y) heatRateBarPosition = (0, 0x3333), heatRateBarSize = (0xdf2e, 0x5555);
        (int x, int y) jumpFuelBarPosition = (0, 0x3333), jumpFuelBarSize = (0xdf2e, 0x5555);

        // ---- Weapon panels (weaponpanel.c) ------------------------------------------------------------------------

        // DrawWeaponPanel: the weapon's name and ammunition, in its state's color (its group's while ready), framed
        // while it's selected. Titanium (0x1006bb50) gates on the weapon list's toggle, and has groups 4 and 5 (MekEngine's
        // in every edition, and AutoGroupWeapons' 6 and 7).
        void DrawWeaponPanel(Panel p_panel)
        {
            if (!(Titanium ? tiWeaponsShown : p_panel.Enabled) || p_panel.Weapon < 0 || mech.Config == null) return;
            WeaponSlot weapon = mech.Config.m_weapons[p_panel.Weapon];
            if (weapon.m_type < 0) return;

            int color;
            switch (weapon.m_state)
            {
                case WeaponSlot.Ready:
                    color = weapon.m_group switch { 1 => 0xfe, 2 => 3, 3 => 5, 4 => 7, 5 => 4, 6 => 6, _ => 0xe };
                    break;
                case WeaponSlot.Recycling:
                    color = 0xb;
                    break;
                case WeaponSlot.Empty:
                    color = 8;
                    break;
                case WeaponSlot.Firing:
                    color = 0xe;
                    break;
                default:
                    color = 0xb;
                    break;
            }

            VfxFont font = Art.HudFont;
            if (font != null)
            {
                Art.g_textColors[0xe] = color;
                // Titanium: a pooled weapon shows its pool's shots.
                string text;
                if (Titanium) text = weapon.m_pool == WeaponSlot.NoPool ? p_panel.Name : $"{p_panel.Name} {mech.Config.AmmoOf(weapon)}";
                else text = weapon.m_ammo < 0 ? p_panel.Name : $"{p_panel.Name} {weapon.m_ammo}";
                if (arcGlitch) text = ScrambleText(text, p_panel.Weapon);
                hud.String(p_panel.Target, p_panel.TextX, p_panel.TextY, font, text, Art.g_textColors);
                Art.g_textColors[0xe] = 0xe;
            }

            if (p_panel.Weapon == mech.SelectedWeapon) OutlinePane(p_panel.Target, color);
        }

        // DrawWeaponPanelStartup: the weapon's name, at the panel's corner, once the clock passes its light-up time.
        void DrawWeaponPanelStartup(Panel p_panel)
        {
            if (!p_panel.Enabled || p_panel.Weapon < 0 || mech.Config == null) return;
            WeaponSlot weapon = mech.Config.m_weapons[p_panel.Weapon];
            if (weapon.m_type < 0) return;
            VfxFont font = Art.HudFont;
            if (font == null) return;
            if (p_panel.Enabled && p_panel.LightUpTime < Host.Cockpit.RenderClock) hud.String(p_panel.Target, 0, 0, font, p_panel.Name, Art.g_textColors);
        }

        // ---- Status panels (statuspanels.c) -----------------------------------------------------------------------

        // FormatTicks: hh:mm:ss.ss of 181 ticks a second.
        static string FormatTicks(int p_ticks)
        {
            int hours = p_ticks / (181 * 3600);
            int minutes = (p_ticks - hours * (181 * 3600)) / (181 * 60);
            double seconds = (p_ticks - hours * (181 * 3600) - minutes * (181 * 60)) / 181.0;
            return $"{hours:00}:{minutes:00}:{seconds:00.00}";
        }

        // FormatSeconds: hh:mm:ss.
        static string FormatSeconds(int p_seconds)
        {
            int hours = p_seconds / 3600, minutes = (p_seconds - hours * 3600) / 60, seconds = p_seconds - hours * 3600 - minutes * 60;
            return $"{hours:00}:{minutes:00}:{seconds:00}";
        }

        // DrawObjectiveList: the listed objectives of priority p_priority, each with its state at the panel's right edge;
        // a name of 32 characters or more takes two lines.
        void DrawObjectiveList(Panel p_panel, ref int p_x, ref int p_y, VfxFont p_font, int p_priority, List<CockpitObjective> p_objectives)
        {
            Pane target = p_panel.Target;
            int height = p_font.Height;
            foreach (CockpitObjective objective in p_objectives)
            {
                if (!objective.Listed || objective.Priority != p_priority) continue;
                p_x = p_panel.TextX;
                bool twoLines = false;
                string label;
                switch (p_priority)
                {
                    case 1:
                        label = Titanium ? "Target: " : "Primary: ";
                        break;
                    case 2:
                        label = Titanium ? "Bonus: " : "Secondary: ";
                        break;
                    case 8:
                        label = "Return: ";
                        break;
                    default:
                        label = Titanium ? "Bounty: " : "Tertiary: ";
                        break;
                }

                Art.g_textColors[0xe] = 6;
                hud.String(target, p_x, p_y, p_font, label, Art.g_textColors);
                Art.g_textColors[0xe] = 0xe;
                p_x += TextWidth("Secondary: ", p_font);
                string name = objective.Title ?? "";
                if (name.Length < 0x20) hud.String(target, p_x, p_y, p_font, name, Art.g_textColors);
                else
                {
                    int x = p_x, y = p_y;
                    hud.String(target, p_x, p_y, p_font, name.Substring(0, 0x20), Art.g_textColors);
                    p_x = x;
                    p_y += height;
                    hud.String(target, p_x, p_y, p_font, name.Substring(0x20), Art.g_textColors);
                    p_x = x;
                    p_y = y;
                    twoLines = true;
                }

                string text;
                int color;
                switch (objective.State)
                {
                    case 5:
                        text = Titanium ? "Complete" : "Successful";
                        color = 7;
                        break;
                    case 6:
                        text = Titanium ? "InComplete" : "Failed";
                        color = 0xb;
                        break;
                    default:
                        text = "In progress";
                        color = 0xe;
                        break;
                }

                p_x = target.X1 - target.X0 - TextWidth(text, p_font);
                Art.g_textColors[0xe] = color;
                hud.String(target, p_x, p_y, p_font, text, Art.g_textColors);
                Art.g_textColors[0xe] = 0xe;
                p_y += height;
                if (twoLines) p_y += height;
            }
        }

        // DrawObjectivesPanel: the objectives by priority, then the mission's clock (its time limit counting down, or the
        // time elapsed). The mission's end isn't ported: it is always in progress, started as the mission did.
        protected void DrawObjectivesPanel(Panel p_panel)
        {
            if (!p_panel.Enabled || !ShowObjectives) return;
            VfxFont font = Art.HudFont;
            if (font == null) return;

            int height = font.Height, gap = height / 2;
            int x = p_panel.TextX, y = p_panel.TextY;
            Art.g_textColors[0xe] = 6;
            hud.String(p_panel.Target, x, y, font, "MISSION OBJECTIVES", Art.g_textColors);
            Art.g_textColors[0xe] = 0xe;
            UnderlineText(p_panel.Target, "MISSION OBJECTIVES", x, y, font, 6);
            y += gap + height;
            List<CockpitObjective> objectives = world.Objectives(world.LocalStar);
            DrawObjectiveList(p_panel, ref x, ref y, font, 1, objectives);
            y += gap;
            DrawObjectiveList(p_panel, ref x, ref y, font, 2, objectives);
            y += gap;
            DrawObjectiveList(p_panel, ref x, ref y, font, 4, objectives);
            DrawObjectiveList(p_panel, ref x, ref y, font, 0, objectives);
            y += gap;
            DrawObjectiveList(p_panel, ref x, ref y, font, 8, objectives);
            x = p_panel.TextX;
            y += gap + height;

            int timeLimit = world.MissionTimeLimit(world.LocalStar);
            string text = timeLimit > 0
                ? "Time Remaining: " + FormatTicks(timeLimit * 181 - Host.Cockpit.RenderClock)
                : "Elapsed Time: " + FormatTicks(Host.Cockpit.RenderClock);
            Art.g_textColors[0xe] = 6;
            hud.String(p_panel.Target, x, y, font, text, Art.g_textColors);
            Art.g_textColors[0xe] = 0xe;
        }

        // UnderlineText: a line under the text at p_x, p_y, its width.
        void UnderlineText(Pane p_target, string p_text, int p_x, int p_y, VfxFont p_font, int p_color)
        {
            int width = TextWidth(p_text, p_font), height = p_font.Height;
            hud.Line(p_target, p_x, p_y + height, p_x + width - 1, p_y + height, p_color);
        }

        // DrawNetworkPanel: the network's messages and chat. Single player has neither: it draws nothing.
        // Mech Soccer's score where the cockpit has no kills panel (Ghost Bear's Legacy).
        protected void DrawNetworkPanel(Panel p_panel)
        {
            if (PanelKills < 0) DrawKillsPanel(p_panel);
        }

        // DrawKillsPanel: the kills in a network game; nothing in single player.
        // Mech Soccer (not in the original; SoccerMatch): the teams' goals and the time left instead.
        protected void DrawKillsPanel(Panel p_panel)
        {
            if (!p_panel.Enabled || !Host.World.SoccerActive) return;
            VfxFont font = Art.HudFont;
            if (font == null) return;
            string text = $"A {Host.Cockpit.SoccerGoals(true)} - {Host.Cockpit.SoccerGoals(false)} B  " + FormatTicks(Mathf.Max(0, Host.Cockpit.SoccerTicksLeft));
            hud.String(p_panel.Target, p_panel.TextX, p_panel.TextY, font, text, Art.g_textColors);
        }

        // DrawAutopilotPanel: "AUTOPILOT" while it's on.
        protected void DrawAutopilotPanel(Panel p_panel)
        {
            if (!p_panel.Enabled) return;
            if (mech.Autopilot != 1 && mech.Autopilot != 2) return;
            VfxFont font = Art.HudFont;
            if (font == null) return;
            hud.String(p_panel.Target, p_panel.TextX, p_panel.TextY, font, "AUTOPILOT", Art.g_textColors);
        }

        // DrawSpeedPanel: the speed in kph (colored while backing up) and the throttle gauge.
        void DrawSpeedPanel(Panel p_panel)
        {
            if (!p_panel.Enabled) return;
            int speed = (int)(mech.VelocityLength / 10002 * 1.5);
            if (mech.SpeedValue < 0)
            {
                speed = -speed;
                Art.g_textColors[0xe] = 6;
            }
            else Art.g_textColors[0xe] = 0xe;

            VfxFont font = Art.HudFont;
            if (font == null) return;
            hud.String(p_panel.Target, p_panel.TextX, p_panel.TextY, font, $"{speed} kph", Art.g_textColors);
            Art.g_textColors[0xe] = 0xe;
            DrawThrottleGauge(p_panel.Target);
        }

        // DrawMascPanel: "MASC" while it's engaged.
        protected void DrawMascPanel(Panel p_panel)
        {
            if (!p_panel.Enabled || !Demech2.Mech.MascEngaged) return;
            p_panel.SetName("MASC");
            VfxFont font = Art.HudFont;
            if (font == null) return;
            hud.String(p_panel.Target, p_panel.TextX, p_panel.TextY, font, p_panel.Name, Art.g_textColors);
        }

        // DrawHeatPanel: "Heat", or the shutdown's state while overheating, and the heat bar.
        void DrawHeatPanel(Panel p_panel)
        {
            if (!p_panel.Enabled) return;
            VfxFont font = Art.HudFont;
            if (font == null) return;
            if ((mech.Flags & 4) != 0)
            {
                p_panel.SetName((mech.Flags & 8) == 0 ? "Shutdown..." : "Overridden");
                Art.g_textColors[0xe] = 0xb;
            }
            else
            {
                p_panel.SetName("Heat");
                Art.g_textColors[0xe] = 0xe;
            }

            hud.String(p_panel.Target, p_panel.TextX, p_panel.TextY, font, p_panel.Name, Art.g_textColors);
            Art.g_textColors[0xe] = 0xe;
            DrawHeatBar(p_panel.Target);
        }

        // DrawHeatRatePanel: "dH/dT" and its bar.
        void DrawHeatRatePanel(Panel p_panel)
        {
            if (!p_panel.Enabled) return;
            p_panel.SetName("dH/dT");
            VfxFont font = Art.HudFont;
            if (font == null) return;
            hud.String(p_panel.Target, p_panel.TextX, p_panel.TextY, font, p_panel.Name, Art.g_textColors);
            DrawHeatRateBar(p_panel.Target);
        }

        // DrawJetsPanel: "Jets" and the fuel bar, for a mech with jump jets.
        void DrawJetsPanel(Panel p_panel)
        {
            if (!p_panel.Enabled || mech.JumpFuel < 0) return;
            p_panel.SetName("Jets");
            VfxFont font = Art.HudFont;
            if (font == null) return;
            hud.String(p_panel.Target, p_panel.TextX, p_panel.TextY, font, p_panel.Name, Art.g_textColors);
            DrawJumpFuelBar(p_panel.Target);
        }

        // ---- Bar gauges (bargauges.c) -----------------------------------------------------------------------------

        // ScaleBarGauges: the gauges' rectangles in their panels.
        void ScaleBarGauges()
        {
            Panel panel = panels[PanelSpeed];
            throttleGaugeSize = ScreenScale.ScalePointToFrame(panel.Target, throttleGaugeSize.x, throttleGaugeSize.y);
            throttleFrameLeft = panel.Width - throttleGaugeSize.x - 1;
            throttleFrameTop = panel.Height - (throttleGaugeSize.y + throttleGaugeSize.y / 2) - 3;
            throttleFrameRight = panel.Width - 1;
            throttleFrameBottom = panel.Height - 1;
            throttleBarLeft = throttleFrameLeft + 1;
            throttleZeroY = panel.Height - throttleGaugeSize.y / 2 - 2;

            panel = panels[PanelHeat];
            heatBarSize = ScreenScale.ScalePointToFrame(panel.Target, heatBarSize.x, heatBarSize.y);
            heatBarPosition = ScreenScale.ScalePointToFrame(panel.Target, heatBarPosition.x, heatBarPosition.y);
            panel = panels[PanelHeatRate];
            heatRateBarSize = ScreenScale.ScalePointToFrame(panel.Target, heatRateBarSize.x, heatRateBarSize.y);
            heatRateBarPosition = ScreenScale.ScalePointToFrame(panel.Target, heatRateBarPosition.x, heatRateBarPosition.y);
            panel = panels[PanelJets];
            jumpFuelBarSize = ScreenScale.ScalePointToFrame(panel.Target, jumpFuelBarSize.x, jumpFuelBarSize.y);
            jumpFuelBarPosition = ScreenScale.ScalePointToFrame(panel.Target, jumpFuelBarPosition.x, jumpFuelBarPosition.y);
        }

        // DrawHeatBar: bands from each end that meet in the middle as the heat rises.
        void DrawHeatBar(Pane p_target)
        {
            int x = heatBarPosition.x, y = heatBarPosition.y, width = heatBarSize.x, height = heatBarSize.y - 1;
            heatBarLevel.Target = Fixed.MulDiv64(mech.Heat, width, 100);
            int level = heatBarLevel.Update() >> 16;
            int edge, color;
            if (level >= width)
            {
                DrawHorizontalBar(p_target, x, y, width, height, 0xb);
                return;
            }

            if (level <= 0)
            {
                DrawHorizontalBar(p_target, x, y, width, height, 7);
                return;
            }

            if (width >> 1 > level)
            {
                edge = level;
                color = 7;
            }
            else
            {
                edge = width - level;
                color = 0xb;
            }

            int middle = width - edge * 2, middleX = x + edge + 1, rightX = middleX + middle;
            DrawHorizontalBar(p_target, x, y, edge, height, 3);
            DrawHorizontalBar(p_target, middleX, y, middle, height, color);
            DrawHorizontalBar(p_target, rightX, y, edge, height, 3);
        }

        // DrawHeatRateBar: the heat's rise less the cooling over the frame.
        void DrawHeatRateBar(Pane p_target)
        {
            int cooling = mech.Config != null ? mech.Config.m_cooling : 0;
            heatRateBarLevel.Target = (mech.DeltaHeat - cooling * Clock.g_deltaTime) >> 6;
            int level = heatRateBarLevel.Update();
            int fill = 0, fillColor, backColor;
            if (level < 1)
            {
                fillColor = backColor = 7;
                level = 0;
            }
            else if (level < 0x300)
            {
                fillColor = 3;
                backColor = 7;
            }
            else
            {
                fillColor = 0xb;
                backColor = 3;
                level -= 0x300;
            }

            if (level > 0)
            {
                fill = Fixed.MulDiv64(heatRateBarSize.x, level, 0x300);
                if (fill < 0) fill = 0;
                else if (fill > heatRateBarSize.x) fill = heatRateBarSize.x;
            }

            if (fill > 0) DrawHorizontalBar(p_target, heatRateBarPosition.x, heatRateBarPosition.y, fill, heatRateBarSize.y - 1, fillColor);
            if (fill < heatRateBarSize.x)
            {
                DrawHorizontalBar(p_target, heatRateBarPosition.x + fill, heatRateBarPosition.y, heatRateBarSize.x - fill,
                    heatRateBarSize.y - 1, backColor);
            }
        }

        // DrawThrottleGauge: its frame and a bar up from zero, or down (half as far) in reverse.
        void DrawThrottleGauge(Pane p_target)
        {
            hud.Line(p_target, throttleFrameLeft, throttleFrameTop, throttleFrameLeft, throttleFrameBottom, 10);
            hud.Line(p_target, throttleFrameRight, throttleFrameTop, throttleFrameRight, throttleFrameBottom, 10);
            hud.Line(p_target, throttleFrameLeft, throttleFrameTop, throttleFrameRight, throttleFrameTop, 10);
            hud.Line(p_target, throttleFrameLeft, throttleFrameBottom, throttleFrameRight, throttleFrameBottom, 10);

            bool reverse = GameKeys.Reverse;
            int value = InputMap.Get("drive.throttle") << 16;
            value = Fixed.MulDiv64(value, throttleGaugeSize.y, 0x400);
            if (reverse) value /= -2;
            throttleBarLevel.Target = value;
            value = throttleBarLevel.Update() >> 16;

            int x = throttleBarLeft, y = throttleZeroY, width = throttleGaugeSize.x - 1, height = value, color = 0xf;
            if (reverse)
            {
                height = -height;
                if (height > throttleGaugeSize.y / 2) height = throttleGaugeSize.y / 2;
                color = 7;
                y += height;
            }
            else if (height > throttleGaugeSize.y) height = throttleGaugeSize.y;

            height++;
            DrawVerticalBar(p_target, x, y, width, height, color);
        }

        // DrawJumpFuelBar: the fuel left of 10 seconds' worth.
        void DrawJumpFuelBar(Pane p_target)
        {
            if (mech.JumpFuel < 0) return;
            jumpFuelBarLevel.Target = mech.JumpFuel;
            int fill = jumpFuelBarLevel.Update();
            fill = Fixed.MulDiv64(jumpFuelBarSize.x + 1, fill, 0x712);
            if (fill > jumpFuelBarSize.x) fill = jumpFuelBarSize.x;
            DrawHorizontalBar(p_target, jumpFuelBarPosition.x, jumpFuelBarPosition.y, fill, jumpFuelBarSize.y - 1, 0xf);
            DrawHorizontalBar(p_target, jumpFuelBarPosition.x + fill, jumpFuelBarPosition.y, jumpFuelBarSize.x - fill,
                jumpFuelBarSize.y - 1, 0xb);
        }

        // DrawVerticalBar: a bar p_height up from (p_x, p_y), its columns shaded darker toward the edges (the palette
        // color and the two below it).
        protected void DrawVerticalBar(Pane p_target, int p_x, int p_y, int p_width, int p_height, int p_color)
        {
            int half = p_width / 2, end = half / 2, top = p_y - p_height, dark = p_color - 1, darker = p_color - 2;
            if (p_height <= 0) return;
            for (int i = 0; i < end; i++) hud.Line(p_target, i + p_x, p_y, i + p_x, top, dark);
            for (int i = end; i < half; i++) hud.Line(p_target, i + p_x, p_y, i + p_x, top, p_color);
            end = half + (p_width - half) / 2;
            for (int i = half; i < end; i++) hud.Line(p_target, i + p_x, p_y, i + p_x, top, dark);
            for (int i = end; p_width > i; i++) hud.Line(p_target, i + p_x, p_y, i + p_x, top, darker);
        }

        // DrawHorizontalBar: a bar p_width across from (p_x, p_y), its rows shaded darker toward the edges.
        protected void DrawHorizontalBar(Pane p_target, int p_x, int p_y, int p_width, int p_height, int p_color)
        {
            int half = p_height / 2, end = half / 2, right = p_width + p_x, dark = p_color - 1, darker = p_color - 2;
            if (p_width <= 0) return;
            for (int i = 0; i < end; i++) hud.Line(p_target, p_x, i + p_y, right, i + p_y, dark);
            for (int i = end; i < half; i++) hud.Line(p_target, p_x, i + p_y, right, i + p_y, p_color);
            end = half + (p_height - half) / 2;
            for (int i = half; i < end; i++) hud.Line(p_target, p_x, i + p_y, right, i + p_y, dark);
            for (int i = end; p_height > i; i++) hud.Line(p_target, p_x, i + p_y, right, i + p_y, darker);
        }
    }
}
