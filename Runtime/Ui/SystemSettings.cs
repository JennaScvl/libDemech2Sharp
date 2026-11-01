namespace Demech2
{
    // The systems status menu's settings (demech2 MW2/src/settings.c: GetSystemSetting, SetSystemSetting, by the game
    // key that toggles each) and the views two of them switch (environment.c: SetInfrared, IsInfraredOn; polydraw.c's
    // g_renderSettings.m_wireframe 1): light amplification (0xa6) fades the screen to the infrared palette (slot 0xc),
    // as the 3D-card edition tints it green, unless the radar is damaged; image enhancement (0xa7) draws every polygon
    // black outlined in its wireframe color over the background color, without sky or ground (the game draws it:
    // Host.Cockpit.ApplyEnhancedVision). The HUD (0x13), the shutdown override (0x40) and auto eject (0x3c) are the
    // cockpit's and the combat's own flags.
    public static class SystemSettings
    {
        // g_infraredOn, g_renderSettings.m_wireframe == 1.
        public static bool InfraredOn { get; private set; }
        public static bool EnhancedVisionOn { get; private set; }

        // g_renderSettings.m_wireframe: 0 off, 1 image enhancement, 2 the "xray" cheat's outlines without the fill.
        public static int Wireframe { get; private set; }

        public static void SetWireframe(int p_level)
        {
            Wireframe = p_level;
            SetEnhancedVision(p_level != 0);
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            InfraredOn = false;
            EnhancedVisionOn = false;
            Wireframe = 0;
        }

        // A mission starts or ends: both views off (with p_restore, the world put back as it was drawn).
        public static void Reset(bool p_restore)
        {
            if (EnhancedVisionOn && p_restore) SetEnhancedVision(false);
            InfraredOn = false;
            EnhancedVisionOn = false;
            Wireframe = 0;
            Host.Cockpit.ResetEnhancedVision();
        }

        // GetSystemSetting.
        public static int GetSystemSetting(int p_id)
        {
            switch (p_id)
            {
                case 0xa6:
                    return InfraredOn ? 1 : 0;
                case 0xa7:
                    return EnhancedVisionOn ? 1 : 0;
                case 0x13:
                    return Cockpit.Current != null && Cockpit.Current.ShowHud ? 1 : 0;
                case 0x40:
                    return Cockpit.OverrideShutdown ? 1 : 0;
                case 0x3c:
                    return SimMain.g_autoEject ? 1 : 0;
            }

            return 0;
        }

        // SetSystemSetting.
        public static void SetSystemSetting(int p_id, int p_value)
        {
            switch (p_id)
            {
                case 0xa6:
                    SetInfrared(p_value != 0);
                    break;
                case 0xa7:
                    if (p_value != 0)
                    {
                        SetEnhancedVision(true);
                        MissionSpeech.PlayCockpitSound(0x1b, 1);
                    }
                    else SetEnhancedVision(false);

                    break;
                case 0x13:
                    if (Cockpit.Current != null) Cockpit.Current.ShowHud = p_value != 0;
                    break;
                case 0x40:
                    Cockpit.OverrideShutdown = p_value != 0;
                    break;
                case 0x3c:
                    Host.Cockpit.SetAutoEject(p_value != 0);
                    break;
            }
        }

        // SetInfrared: on (with the radar undamaged) the fade to the infrared palette, its sound and "light
        // amplification engaged"; off, back to the time of day's (the 3D-card edition's FadeToBasePalette(0)).
        public static void SetInfrared(bool p_on)
        {
            if (InfraredOn == p_on) return;
            if (p_on)
            {
                if (Cockpit.Current != null && Cockpit.Current.RadarDamage >= 1) return;
                InfraredOn = true;
                ScreenFlash.FadeToBasePalette(0xc, 181);
                CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xb2), 100, 0x40, 5, 0x50);
                MissionSpeech.PlayCockpitSound(0x1c, 1);
            }
            else
            {
                InfraredOn = false;
                ScreenFlash.FadeToBasePalette(0, 181);
            }
        }

        // m_wireframe 1 on or off (DrawPolygonOrLine: each polygon filled with color 0, then outlined in
        // GetFaceColor's wireframe color); the game draws the view so.
        public static void SetEnhancedVision(bool p_on)
        {
            if (EnhancedVisionOn == p_on) return;
            EnhancedVisionOn = p_on;
            if (!p_on) Wireframe = 0;
            else if (Wireframe == 0) Wireframe = 1;
            Host.Cockpit.ApplyEnhancedVision(p_on);
        }
    }
}
