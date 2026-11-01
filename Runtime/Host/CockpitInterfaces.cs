using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // What the cockpit (Cockpit) draws on: the VFX calls on the page (HudPage).
    public interface ICockpitSurface
    {
        int Width { get; }
        int Height { get; }

        // The display in page pixels (the 3D view's pane): the page and the margins around it.
        Pane Display { get; }

        // Not the original's: how far what's drawn in a pane moves into the display's margins (page pixels).
        Vector2 SpreadOf(Pane p_pane);

        // Not the original's: the arc glitch, each element of the page torn on its own while it's on.
        bool Glitching { get; set; }

        void Shape(Pane p_pane, HudShape p_shape, int p_frame, int p_x, int p_y, int p_remap6 = -1);
        void Line(Pane p_pane, int p_x0, int p_y0, int p_x1, int p_y1, int p_color);
        void Wipe(Pane p_pane, int p_color);
        // The 3D view's lines and convex polygons, at page points (EnhancedVision).
        void Line(Pane p_pane, Vector2 p_from, Vector2 p_to, int p_color);
        void Polygon(Pane p_pane, Vector2[] p_points, int p_count, int p_color);
        void Ellipse(Pane p_pane, int p_x, int p_y, int p_width, int p_height, int p_color);
        void FillEllipse(Pane p_pane, int p_x, int p_y, int p_width, int p_height, Color32 p_color);
        int Character(Pane p_pane, int p_x, int p_y, VfxFont p_font, char p_char, int[] p_colors);
        void String(Pane p_pane, int p_x, int p_y, VfxFont p_font, string p_text, int[] p_colors);
        void Texture(Pane p_pane, Texture p_texture);
    }

    // What the cockpit reads of the local mech (MechPilot: demech2's Mech and its Player).
    public interface ICockpitMech
    {
        int PowerState { get; }      // m_powerState
        int PlayerFlags { get; }     // Player::m_flags
        int Flags { get; set; }      // m_flags
        int Heat { get; }            // m_heat, 16.16 percent
        int DeltaHeat { get; }       // m_deltaHeat
        int Autopilot { get; }       // m_autopilot
        int SelectedWeapon { get; }  // m_selectedWeapon
        int JumpFuel { get; }        // m_jumpFuel
        MechConfig Config { get; }   // the weapons and sections
        int SpeedValue { get; }      // m_speed's value
        int VelocityLength { get; }  // ApproximateVectorLength of the velocity, 16.16
        Vector3Int Velocity { get; } // the velocity, 16.16 units a tick
        int Height { get; }          // m_height
        int GroundHeight { get; }    // Player::m_groundHeight
        Vector3Int Position { get; } // the feet, units
        int ObjectY { get; }         // Player::m_position's y
        int TorsoTwist { get; }      // 16.16 degrees
        int TorsoPitch { get; }
        int HeadingFixed { get; }    // Player::m_heading
        PlayerTargetInfo Target { get; }   // Player::m_targetInfo
        PlayerTargetInfo Route { get; }    // Titanium's route nav (Player +0xe8)
        int AimRange { get; }        // Player::m_aimRange's value, units
        int Throttle { get; }        // m_throttle's value: 0x400 stopped, 0x800 full
    }

    // What the cockpit reads of the world (SceneModel).
    public interface ICockpitWorld
    {
        IReadOnlyList<Player> CockpitPlayers { get; }
        IReadOnlyList<NavPoint> CockpitNavs { get; }
        int LocalStar { get; }
        int GroundColor { get; }
        int PlayerSide(int p_index);
        List<CockpitObjective> Objectives(int p_star);
        int MissionTimeLimit(int p_star);

        // The name of a game thing not yet inspected (HTXT: g_anonymousInstallationName).
        string AnonymousInstallationName { get; }
    }

    // The 3D view the cockpit is drawn over (the cockpit camera), and the views it renders into panes.
    public interface ICockpitView
    {
        // The eye (the camera) in world units, its heading and roll (degrees), and its vertical field of view.
        Vector3 EyeUnits { get; }
        float EyeHeading { get; }
        float EyeRoll { get; }
        float VerticalFov { get; }

        // The camera itself (its near and far planes for the views drawn on the page).
        Camera EyeCamera { get; }

        // The eye object's position (the camera's parent), units.
        Vector3 EyeObjectUnits { get; }

        // The aim ray (BuildAimRay): its start and direction, units.
        bool AimRay(out Vector3 p_origin, out Vector3 p_direction);

        // A world point (units) on the page: its page position and its depth in front of the eye (units).
        bool ProjectToPage(Vector3 p_units, out Vector2 p_page, out float p_depth);

        // A transform's position, units.
        Vector3 UnitsOf(Transform p_transform);

        // RenderViewToPane: the world from p_position (units) at heading, pitch and roll (degrees), the tangent of half
        // the pane's width 1 / p_fovX, for pane p_target; the local mech hidden with p_hideMech. The texture to draw.
        Texture RenderViewToPane(int p_slot, float p_fovX, Vector3 p_position, float p_heading, float p_pitch, float p_roll,
            Pane p_target, bool p_hideMech);

        // BeginMapView's 3D view: the world straight down from p_center (units), north up, p_width units across pane
        // p_target, as far as p_far below; rendered at p_texture's size (smaller for the satellite view's static, then
        // stretched). The texture to draw.
        Texture RenderMapViewToPane(int p_slot, Vector3 p_center, float p_width, float p_far, Pane p_target, Pane p_texture);
        void RenderMapViewToScreen(int p_slot, Vector3 p_center, float p_width, float p_far, Pane p_target, Color p_background);
    }

    // An objective as the objectives panel lists it (MissionObjective).
    public readonly struct CockpitObjective
    {
        public readonly string Title;
        public readonly int Priority, State;
        public readonly bool Listed;

        public CockpitObjective(string p_title, int p_priority, bool p_listed, int p_state)
        {
            Title = p_title;
            Priority = p_priority;
            Listed = p_listed;
            State = p_state;
        }
    }
}
