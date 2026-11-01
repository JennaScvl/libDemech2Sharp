using UnityEngine;

namespace Demech2
{
    // A line playing on the cockpit's voice (the speech queue waits on it).
    public interface ISpeechVoice
    {
        bool IsPlaying { get; }
        void Stop();
    }

    // What the cockpit's sounds, the sound settings and the speech take from the game: its audio output and settings.
    public interface IAudioHost
    {
        // PlaySample's sound: SNDS p_id (the running install's) at p_volume (out of 100) of p_master, pan p_pan (out
        // of 127), its flags (0x400 doesn't start one already playing with it, 0x200 restarts one).
        void PlayCockpitSample(int p_id, int p_volume, float p_master, int p_pan, int p_flags);

        // A sound p_sound placed in the world at p_at (units), at p_volume (0 to 1), heard while the menus pause the sim
        // with p_ignorePause, MW2's own falloff with p_explosion.
        void PlayWorldSample(int p_sound, float p_volume, Vector3 p_at, bool p_ignorePause, bool p_explosion);

        // The eye (g_eyepoint) in world units: where the sounds are heard.
        Vector3 EyeUnits { get; }

        // The volumes (0 to 1) the sounds take, live or as saved: 0 the world's, 1 the cockpit's and the speech, 2 the
        // CD tracks, 3 the shell's songs; a live one set, and the volumes saved.
        float Volume(int p_channel);
        float SavedVolume(int p_channel);
        void SetVolume(int p_channel, float p_volume);
        void SaveVolumes();

        // The CD music.
        void MusicStart();
        void MusicPause();
        void MusicResume();
        void MusicStop();

        // MW2SND.CFG's bytes (null without the file), and written back.
        byte[] ReadSoundConfigFile(string p_name);
        void WriteSoundConfigFile(string p_name, byte[] p_data);

        // A sound's queue priority and how many of it may play at once (-1 any).
        int SoundPriority(int p_id);
        int SoundMaxCount(int p_id);

        // A speech line's sound: SNDS p_id, or a mission's line by name (its sound file, else the SNDS of that name);
        // null for none. Played on the cockpit's voice at p_volume.
        object SpeechClip(int p_id);
        object MissionLineClip(string p_name);
        ISpeechVoice PlayVoice(object p_clip, float p_volume);

        // A speech queue's lane made: the game advances it each frame (AdvanceSpeechQueue) until it's torn down
        // (MissionSpeech.Destroyed).
        void AttachSpeechLane(MissionSpeech p_lane, string p_name);
    }
}
