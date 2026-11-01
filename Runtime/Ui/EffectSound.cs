using UnityEngine;

namespace Demech2
{
    // A sound in the world (demech2 MW2/src/soundfx.c PlaySoundAt, PlayPositionalSound): none past 50000 units
    // (GetDistanceVolume), half as loud for p_half, its random rate drawn (RandomSampleRate) and then ignored, as in the
    // original. The game places and plays it (Host.Audio), its own distance, pan and travel time rather than Miles'
    // volume, CalculateSamplePan and g_soundDelayPerUnit.
    public static class EffectSound
    {
        // The rates a random one is drawn from (Tables/VoicePitches.json), read on first use.
        static int[] VoicePitches => SimTables.VoicePitches;

        // p_offset: from the sound to the eye, units (dx, dy, dz as SpawnEffectEx passes them). Returns the distance.
        // p_explosion: MW2's own falloff rather than the natural one.
        public static float PlaySoundAt(Vector3 p_offset, int p_sound, bool p_half, bool p_explosion = false)
        {
            float distance = p_offset.magnitude;
            if (GetDistanceVolume((int)distance) <= 0) return distance;

            // PlayDelayedSound -> PlaySoundRandomRate: the random rate is drawn, then PlaySample uses the sound's own.
            _ = RandomSampleRate();
            Host.Audio.PlayWorldSample(p_sound, p_half ? 0.5f : 1f, Host.Audio.EyeUnits - p_offset, false, p_explosion);
            return distance;
        }

        // Not in the original: a menu's sound at the eye, heard while the menu pauses the sim.
        public static void PlayMenuSound(int p_sound)
        {
            _ = RandomSampleRate();
            Host.Audio.PlayWorldSample(p_sound, 1f, Host.Audio.EyeUnits, true, false);
        }

        // GetDistanceVolume: 100 near, falling with the square of the distance in metres.
        public static int GetDistanceVolume(int p_distance)
        {
            if (p_distance > 50000) return 0;
            p_distance /= 100;
            p_distance *= p_distance;
            return 100 - p_distance * 100 / 250000;
        }

        // RandomSampleRate: one of g_sampleRates, Hz.
        public static int RandomSampleRate() => VoicePitches[SimRandom.Cosmetic.RandomIntBelow(10)];
    }
}
