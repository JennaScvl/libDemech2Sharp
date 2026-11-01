namespace Demech2
{
    // The cockpit's sound effects (demech2 MW2/src/soundfx.c: PlaySoundEffect, PlaySoundOnce, PlaySample, StartSample):
    // a SNDS resource played flat (not in the world), at a volume out of 100 and a pan out of 127. PlaySample takes the
    // sound's own rate, or 11025 Hz: the rate passed is never used. The flags' 0x400 doesn't start a sound already
    // playing with it, 0x200 restarts one; the sound's limit holds as for every sound. Not the original's: at the
    // cockpit volume, played by the game (Host.Audio).
    public static class CockpitSound
    {
        // PlaySoundEffect(p_id, p_volume, p_pan, p_rate, p_flags), at the cockpit volume (the original's effects volume,
        // g_soundConfig.m_effectsVolume).
        public static void PlaySoundEffect(int p_id, int p_volume, int p_pan, int p_rate, int p_flags) =>
            PlaySample(p_id, p_volume, Audio.VoiceScale, p_pan, p_flags);

        // PlaySample: p_id at p_volume (out of 100) of p_master (the effects' or the voice's volume, 0 to 1).
        public static void PlaySample(int p_id, int p_volume, float p_master, int p_pan, int p_flags)
        {
            if (p_volume <= 0) return;
            Host.Audio.PlayCockpitSample(p_id, p_volume, p_master, p_pan, p_flags);
        }

        // PlaySoundOnce: flags 0x450.
        public static void PlaySoundOnce(int p_id, int p_volume, int p_pan, int p_rate) =>
            PlaySoundEffect(p_id, p_volume, p_pan, p_rate, 0x450);
    }
}
