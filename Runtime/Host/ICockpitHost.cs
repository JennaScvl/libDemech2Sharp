using UnityEngine;

namespace Demech2
{
    // Not in the original: what a pilot asks of its mech outside its controls (the game keys' and the cockpit's), run
    // at a step by the game (in lockstep through the step bundle, else at once on the local mech).
    public enum PilotCommand : byte
    {
        None, PowerUp, PowerDown, Jettison, ToggleMasc, PunchInAutoHeading, OverrideShutdown, FeetToTorso, Eject,
        ResetTargeting, SetWeaponGroup, ToggleAms, AutoEject, StarOrder,
    }

    // What the cockpit and the in-mission UI take from the game: the loaded art, the clocks it draws by, and the rest
    // they don't draw or compute themselves.
    public interface ICockpitHost
    {
        // LoadCachedResource of the loaded PRJ's SHP p_id, of 1.1's SHP p_id11 (by name), of FONT p_id; null without one.
        HudShape ShapeOf(int p_id);
        HudShape Shape11(int p_id11);
        VfxFont FontOf(int p_id);

        // The palette's entry p_index.
        Color32 PaletteColor(int p_index);

        // The real clock the cockpit draws by (ticks), and the ticks since the last rendered frame.
        int RenderClock { get; }
        int FrameTicks { get; }

        // The world stream's cockpit records of chassis p_chassis' streams, run into p_cockpit (its LoadCptFile,
        // LoadHudFile once p_loadHudFile, LoadWindowRecord, LoadAnim2d).
        void RunCockpitStream(Cockpit p_cockpit, string p_chassis, ref bool p_loadHudFile);

        // The engine note (the original's MIDI): stopped, started, updated by the throttle, muted outside the cockpit.
        void EngineNoteStop();
        void EngineNoteStart();
        void EngineNoteUpdate(uint p_throttle, int p_powerState);
        void EngineNoteMute();

        // A command for the local mech; g_autoEject's toggle.
        void IssuePilotCommand(PilotCommand p_command, int p_arg);
        void SetAutoEject(bool p_on);

        // Mech p_index's rendered (interpolated) feet and object position, units; false without its mech.
        bool MechRenderPosition(int p_index, out Vector3Int p_at);
        bool MechRenderObjectPosition(int p_index, out Vector3 p_at);

        // Game thing p_index's shape's rendered center (units) and radius; false without one.
        bool ThingShapeBounds(int p_index, out Vector3 p_center, out float p_radius);

        // Not the original's: impact arcs crawl over player p_player's mech (ImpactArcs).
        bool ArcsOnMech(int p_player);

        // The cheats' cockpit flags: the rear view shows the front, the damage outline's parts framed.
        bool FrontViewForRear { get; }
        bool FrameOutlineParts { get; }

        // m_wireframe 1's target panel: the target (p_kind, p_index) drawn as wireframe from p_units at p_heading.
        void DrawEnhancedTarget(ICockpitSurface p_hud, Pane p_target, int p_kind, int p_index, Vector3 p_units, float p_heading,
            float p_scale, Camera p_camera);

        // Not the original's: Mech Soccer's score (team A, team B) and its ticks left.
        int SoccerGoals(bool p_teamA);
        int SoccerTicksLeft { get; }

        // Not the original's: the palette fades' plane over the screen, made the first time a fade asks for it.
        void ShowFlashPlane();

        // The real clock in ticks (GetTicks: the menus' repeat runs while the sim is paused).
        int RealTicks { get; }

        // The monitor brightness menu's slider (0 to 0x10000): read, previewed, set, cancelled.
        int GetBrightness(int p_arg);
        void PreviewBrightness(int p_arg, int p_value);
        void SetBrightness(int p_arg, int p_value);
        void RestoreBrightness(int p_arg);

        // The world's hostile atmosphere set to p_value; its value before (false without a world).
        bool SwapHostileAtmosphere(bool p_value);

        // g_fledToWindows: the mission ends and the game quits.
        void FleeToWindows();

        // The command menus' order p_command to the star's slot p_slot (OrderStarSlot, with its radio).
        void RunStarCommand(int p_command, int p_slot);

        // m_wireframe 1's view: drawn as wireframes (p_on) or put back; nothing hidden or outlined (a mission starts).
        void ApplyEnhancedVision(bool p_on);
        void ResetEnhancedVision();

        // The "dorcs" sequence's view and pictures: the eye camera's viewport p_rect of p_display, hidden, put back
        // whole; the palette's blend; a picture (VFX\p_name) loaded, drawn centered on p_display, released.
        void DorcsShowView(Pane p_rect, Pane p_display);
        void DorcsHideView();
        void DorcsRestoreView();
        void DorcsStartFade(float p_from, float p_to, int p_ticks);
        bool DorcsLoadGif(string p_name);
        void DorcsDrawGif(ICockpitSurface p_hud, Pane p_display);
        void DorcsReleaseGif();
    }
}
