using System;
using UnityEngine;

namespace Demech2
{
    // The sound and display settings (demech2 MW2/include/soundconfig.h, MW2/src/audio.c: g_soundConfig, g_mw2SndCfgData,
    // GetSoundSetting, PreviewSoundSetting, SetSoundSetting, RestoreSoundSetting; config.c: LoadSndCfg, SaveSndCfg;
    // simmain.c's reading; perf.c's getters and setters): MW2SND.CFG's block of 0x3c bytes, read as the mission starts
    // (the defaults without the file) and written back as it ends. Live holds what plays now (g_soundConfig), Saved what
    // the file keeps (g_mw2SndCfgData): the menus preview into Live and accept into both. Volumes are 16.16 (0x10000
    // full). Not the original's: the volumes are the game's (Host.Audio), the file's are kept but unused. The effects slider is
    // its world sounds, the voice slider its cockpit sounds and the speech (none at 0: the speech is off, flag 2), the
    // music slider its CD tracks. The perf settings (textmaps, detail, density, chunks) are kept and shown but
    // change nothing: MekEngine draws one detail level.
    public static class Audio
    {
        public sealed class Block
        {
            public int Unk0x00, EffectsVolume, VoiceVolume, MidiVolume, SimFlags;
            public int ObjectTextmaps, TerrainTextmaps, DisplayDetail, ObjectDensity, ExplosionChunks, DisplayBrightness;
            public byte[] VideoDriver = new byte[0x10];

            public Block Copy()
            {
                var copy = (Block)MemberwiseClone();
                copy.VideoDriver = (byte[])VideoDriver.Clone();
                return copy;
            }

            public static Block Defaults() => new Block
            {
                Unk0x00 = 0x10000, EffectsVolume = 0x10000, VoiceVolume = 0x10000, MidiVolume = 0x10000, SimFlags = 11,
                ObjectTextmaps = 1, TerrainTextmaps = 1, DisplayDetail = 1, ObjectDensity = 1, ExplosionChunks = 1,
                DisplayBrightness = 9, VideoDriver = Driver("mcga.dll"),
            };

            static byte[] Driver(string p_name)
            {
                var bytes = new byte[0x10];
                System.Text.Encoding.ASCII.GetBytes(p_name).CopyTo(bytes, 0);
                return bytes;
            }
        }

        // g_soundConfig, g_mw2SndCfgData.
        public static Block Live = Block.Defaults();
        public static Block Saved = Block.Defaults();

        const string FileName = "mw2snd.cfg";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Live = Block.Defaults();
            Saved = Block.Defaults();
        }

        // The scales the sounds take (0 to 1): the world's, the cockpit's and the speech, the CD tracks, the shell's songs.
        public const int Effects = 0, Voice = 1, Music = 2, Midi = 3;
        public static float EffectsScale => Host.Audio.Volume(Effects);
        public static float VoiceScale => Host.Audio.Volume(Voice);
        public static float MusicScale => Host.Audio.Volume(Music);
        public static float MidiScale => Host.Audio.Volume(Midi);

        static int Fixed(float p_scale) => Mathf.RoundToInt(p_scale * 0x10000);
        static float Scale(int p_value) => Mathf.Clamp01(p_value / 65536f);
        public static bool SpeechOn => (Live.SimFlags & 2) != 0;

        // LoadSndCfg: the file (beside mw2prm.cfg, else the install's), or the defaults (g_soundConfig) without it.
        public static void Load()
        {
            byte[] data = Host.Audio.ReadSoundConfigFile(FileName);

            Saved = data != null && data.Length >= 0x2c ? Parse(data) : Block.Defaults();
            // Not the original's: the speech and the CD music are on as the game's volumes say.
            Saved.SimFlags = Host.Audio.SavedVolume(Voice) > 0 ? Saved.SimFlags | 2 : Saved.SimFlags & ~2;
            Saved.SimFlags = Host.Audio.SavedVolume(Music) > 0 ? Saved.SimFlags | 8 | 4 : Saved.SimFlags & ~(8 | 4);
            Live = Saved.Copy();
        }

        // SaveSndCfg (ShutdownAudio).
        public static void Save()
        {
            Block b = Saved;
            var data = new byte[0x3c];
            int[] words = { b.Unk0x00, b.EffectsVolume, b.VoiceVolume, b.MidiVolume, b.SimFlags, b.ObjectTextmaps, b.TerrainTextmaps,
                b.DisplayDetail, b.ObjectDensity, b.ExplosionChunks, b.DisplayBrightness };
            for (int i = 0; i < words.Length; i++) BitConverter.GetBytes(words[i]).CopyTo(data, i * 4);
            Array.Copy(b.VideoDriver, 0, data, 0x2c, Math.Min(0x10, b.VideoDriver.Length));
            Host.Audio.WriteSoundConfigFile(FileName, data);
        }

        static Block Parse(byte[] p_data)
        {
            int At(int p_at) => p_at + 4 <= p_data.Length ? BitConverter.ToInt32(p_data, p_at) : 0;
            var b = new Block
            {
                Unk0x00 = At(0), EffectsVolume = At(4), VoiceVolume = At(8), MidiVolume = At(0xc), SimFlags = At(0x10),
                ObjectTextmaps = At(0x14), TerrainTextmaps = At(0x18), DisplayDetail = At(0x1c), ObjectDensity = At(0x20),
                ExplosionChunks = At(0x24), DisplayBrightness = At(0x28),
            };
            if (p_data.Length > 0x2c) Array.Copy(p_data, 0x2c, b.VideoDriver, 0, Math.Min(0x10, p_data.Length - 0x2c));
            return b;
        }

        // ---- The audio menu's settings (0 effects, 1 voice, 2 music, 3 the first word, 4 speech on) ------------------

        // GetSoundSetting: asking for the music's turns the CD music on (and starts it, paused).
        public static int GetSoundSetting(int p_setting)
        {
            switch (p_setting)
            {
                case 3:
                    return Live.Unk0x00;
                case 0:
                    return Fixed(EffectsScale);
                case 1:
                    return Fixed(VoiceScale);
                case 2:
                    Live.SimFlags |= 8;
                    Host.Audio.MusicStart();
                    Host.Audio.MusicPause();
                    return Fixed(MusicScale);
                case 4:
                    return (Live.SimFlags & 2) != 0 ? 1 : 0;
            }

            return 0;
        }

        // PreviewSoundSetting: the setting now, heard (a test sound for the effects and the voice, the music's volume).
        public static void PreviewSoundSetting(int p_setting, int p_value)
        {
            if (p_setting < 0 || p_setting >= 5) return;
            if (p_setting != 2) Host.Audio.MusicPause();
            switch (p_setting)
            {
                case 3:
                    Live.Unk0x00 = p_value;
                    break;
                case 0:
                {
                    int old = Fixed(EffectsScale);
                    Host.Audio.SetVolume(Effects, Scale(p_value));
                    if (p_value != old) CockpitSound.PlaySample(Host.Anim.MapSoundId(0xd2), 100, EffectsScale, 0x40, 0x250); // PlayEffectsVolumeTest
                    break;
                }
                case 1:
                {
                    int old = Fixed(VoiceScale);
                    Host.Audio.SetVolume(Voice, Scale(p_value));
                    if (p_value != 0) Live.SimFlags |= 2;
                    else Live.SimFlags &= ~2;
                    if (p_value != old) MissionSpeech.PlayVoiceVolumeTest();
                    break;
                }
                case 2:
                {
                    int old = Fixed(MusicScale);
                    Host.Audio.SetVolume(Music, Scale(p_value));
                    if (p_value != old && old == 0) Host.Audio.MusicStart();
                    Host.Audio.MusicResume();
                    break;
                }
            }
        }

        // SetSoundSetting: the setting, kept by MW2SND.CFG too.
        public static void SetSoundSetting(int p_setting, int p_value)
        {
            switch (p_setting)
            {
                case 3:
                    Saved.Unk0x00 = Live.Unk0x00 = p_value;
                    break;
                case 0:
                    Host.Audio.SetVolume(Effects, Scale(p_value));
                    Host.Audio.SaveVolumes();
                    break;
                case 1:
                    Host.Audio.SetVolume(Voice, Scale(p_value));
                    Host.Audio.SaveVolumes();
                    if (p_value != 0) Live.SimFlags |= 2;
                    else Live.SimFlags &= ~2;
                    break;
                case 2:
                    Host.Audio.SetVolume(Music, Scale(p_value));
                    Host.Audio.SaveVolumes();
                    if (p_value != 0)
                    {
                        Saved.SimFlags |= 8 | 4;
                        Host.Audio.MusicPause();
                    }
                    else
                    {
                        Saved.SimFlags &= ~(8 | 4);
                        Host.Audio.MusicStop();
                    }

                    Live.SimFlags = Saved.SimFlags;
                    break;
                case 4:
                    if (p_value != 0) Saved.SimFlags |= 2;
                    else Saved.SimFlags &= ~2;
                    Live.SimFlags = Saved.SimFlags;
                    break;
            }
        }

        // RestoreSoundSetting: back to what MW2SND.CFG keeps.
        public static void RestoreSoundSetting(int p_setting)
        {
            switch (p_setting)
            {
                case 3:
                    Live.Unk0x00 = Saved.Unk0x00;
                    break;
                case 0:
                    Host.Audio.SetVolume(Effects, Host.Audio.SavedVolume(Effects));
                    break;
                case 1:
                    Host.Audio.SetVolume(Voice, Host.Audio.SavedVolume(Voice));
                    if (Host.Audio.Volume(Voice) != 0) Live.SimFlags |= 2;
                    else Live.SimFlags &= ~2;
                    break;
                case 2:
                    Host.Audio.SetVolume(Music, Host.Audio.SavedVolume(Music));
                    if (Host.Audio.Volume(Music) != 0)
                    {
                        Live.SimFlags |= 8 | 4;
                        Host.Audio.MusicPause();
                    }
                    else
                    {
                        Live.SimFlags &= ~(8 | 4);
                        Host.Audio.MusicStop();
                    }

                    break;
                case 4:
                    Live.SimFlags = Saved.SimFlags;
                    break;
            }
        }
    }
}
