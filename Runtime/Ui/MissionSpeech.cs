using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // Betty and the other voice lines (demech2 MW2/src/speech.c: QueueSpeech, QueueSpeechLine, AdvanceSpeechQueue,
    // StartSpeech, PlayCockpitSound; missionaudio.c: ReadSoundFile): lines queued and spoken one at a time, not placed in
    // the world. A mission's line is a sound name: the mission's sound file (keating\<name>.sfl) when there is one, else
    // the SNDS resource of that name. A cockpit line (g_cockpitSpeech) is a SNDS resource, maybe followed by a suffix
    // ("engaged"). Lines queue by priority (the sound's own, SoundLimits); a line already queued as often as the sound
    // may play isn't queued again. Not ported but for the host's lines (SayLine with text): the text shown without
    // digital sound. Not the original's: two queues, each speaking beside the other: Betty's (the cockpit lines, and a
    // mission's lines named BET...) and the mission dialog's (the other mission lines, the lancemates); the game loads and
    // plays the lines (Host.Audio) and advances each queue a frame.
    public sealed class MissionSpeech
    {
        // g_cockpitSpeech's SNDS ids.
        public static readonly int[] CockpitLines =
        {
            0x40, 0x4b, 0x4e, 0xf, 0x10, 0x11, 0x12, 0x13, 0x30, 0x3a, 0x3b, 0x36, 0xc, 0xe, 0x7a, 0x7b, 0x80, 0x42, 0x43,
            0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x49, 0x4a, 0x4c, 0x4d, 0x41, 0x3c, 0x3d, 0x3f, 0x3e,
        };

        // g_engageSpeech: disengaged, engaged, disengaged.
        static readonly int[] EngageLines = { 0x54, 0x50, 0x54 };

        // A queued line (SpeechEntry): its sound, or a mission's sound data, and its suffix.
        sealed class Entry
        {
            public int Id, SuffixId, Priority;
            public object Clip, Suffix;
            // A line's text (SayLine with text) and how long it's shown without speech (g_currentClock).
            public string Text;
            public int HoldUntil;
        }

        readonly List<Entry> queue = new List<Entry>();
        ISpeechVoice voice;
        Entry speaking;
        bool suffixDue;

        static MissionSpeech s_betty, s_dialog;

        // QueueSpeechLine with the line p_name (a sound name such as "BET14"), priority 0x50; nothing for one that
        // doesn't load.
        public static void Say(string p_name)
        {
            object clip = Load(p_name);
            if (clip == null)
            {
                Debug.LogWarning($"MissionSpeech: no sound {p_name}");
                return;
            }

            LaneOf(p_name).Queue(new Entry { Clip = clip, Priority = 0x50 });
        }

        // Betty's queue for a line named BET..., else the mission dialog's.
        static MissionSpeech LaneOf(string p_name) =>
            p_name != null && p_name.StartsWith("BET", StringComparison.OrdinalIgnoreCase) ? Betty : Dialog;

        // QueueSpeechLine of a mission's line p_name (an objective's or the mission's result sound): Say, nothing for an
        // empty name.
        public static void SayLine(string p_name)
        {
            if (string.IsNullOrEmpty(p_name)) return;
            Say(p_name);
        }

        // The line with its text, when the host's lines carry text (Host.Objectives.LinesCarryText; at most 0x33
        // characters, then "."). With the speech off the text shows on the message bar for 0x712 ticks in place of the
        // sound, and a line without text isn't queued; with it on a line without a sound isn't. Otherwise SayLine.
        public static void SayLine(string p_name, string p_text)
        {
            if (!Host.Objectives.LinesCarryText)
            {
                SayLine(p_name);
                return;
            }

            string text = string.IsNullOrEmpty(p_text) ? "" : p_text.Length > 0x33 ? p_text.Substring(0, 0x33) : p_text;
            if (text.Length != 0) text += ".";
            if (Audio.SpeechOn)
            {
                object clip = string.IsNullOrEmpty(p_name) ? null : Load(p_name);
                if (clip == null) return;
                LaneOf(p_name).Queue(new Entry { Clip = clip, Priority = 0x50, Text = text });
                return;
            }

            if (text.Length == 0) return;
            LaneOf(p_name).Queue(new Entry { Priority = 0x50, Text = text });
        }

        // PlayVoiceVolumeTest: "Critical hit" at the voice volume, or with the speech off a beep at full volume.
        public static void PlayVoiceVolumeTest()
        {
            if (Audio.SpeechOn) CockpitSound.PlaySample(Host.Anim.MapSoundId(0x36), 100, Audio.VoiceScale, 0x40, 0x250);
            else CockpitSound.PlaySample(Host.Anim.MapSoundId(0x54), 100, 1f, 0x40, 0x250);
        }

        // !g_speechQueue: nothing is queued or speaking.
        public static bool QueueEmpty => (s_betty == null || s_betty.queue.Count == 0) && (s_dialog == null || s_dialog.queue.Count == 0);

        // g_speechLocked: FlushSpeechQueue stops the queue taking lines (until the mission is restarted).
        public static bool Locked;

        // FlushSpeechQueue: the queue stops taking lines; a speaking cockpit line below priority 0x50 stops, and the
        // queued lines are dropped (with p_keep, a mission's line or one of priority 0x50 and up stays).
        public static void FlushSpeechQueue(bool p_keep)
        {
            if (Locked) return;
            Locked = true;
            s_betty?.Flush(p_keep);
            s_dialog?.Flush(p_keep);
        }

        void Flush(bool p_keep)
        {
            if (queue.Count == 0) return;
            Entry first = queue[0];
            if (first.Id > 0 && first.Priority < 0x50)
            {
                voice?.Stop();
                queue.RemoveAt(0);
                speaking = null;
                suffixDue = false;
                if (queue.Count == 0) return;
            }

            for (int i = queue.Count - 1; i >= 1; i--)
            {
                Entry entry = queue[i];
                bool keep = p_keep && (entry.Id <= 0 || entry.Priority >= 0x50);
                if (!keep) queue.RemoveAt(i);
            }
        }

        // PlayCockpitSound(p_message, p_engage): line p_message of g_cockpitSpeech; p_engage 1 or 2 adds "engaged" or
        // "disengaged", -1 nothing, and 0 says nothing at all.
        public static void PlayCockpitSound(int p_message, int p_engage)
        {
            if (p_engage == 0 || p_message < 0 || p_message >= CockpitLines.Length) return;
            int suffix = p_engage == -1 ? 0 : EngageLines[p_engage];
            QueueSpeech(CockpitLines[p_message], suffix, -1, true);
        }

        // QueueSpeech of SNDS p_id, then SNDS p_suffix (0: none), at p_priority (-1: the sound's own); Betty's line with
        // p_betty, else the mission dialog's.
        public static void QueueSpeech(int p_id, int p_suffix, int p_priority, bool p_betty = false)
        {
            // demech2's ids, every caller's (mapped to the running install's).
            QueueSpeechPrj(Host.Anim.MapSoundId(p_id), Host.Anim.MapSoundId(p_suffix), p_priority, p_betty);
        }

        // QueueSpeech of the loaded PRJ's SNDS ids as they are (the host's own lines).
        public static void QueueSpeechPrj(int p_id, int p_suffix, int p_priority, bool p_betty = false)
        {
            if (p_id <= 0)
            {
                if (p_suffix <= 0) return;
                p_id = p_suffix;
                p_suffix = 0;
            }

            object clip = Host.Audio.SpeechClip(p_id);
            if (clip == null) return;
            var entry = new Entry
            {
                Id = p_id,
                SuffixId = p_suffix > 0 ? p_suffix : 0,
                Suffix = p_suffix > 0 ? Host.Audio.SpeechClip(p_suffix) : null,
                Clip = clip,
                Priority = p_priority == -1 ? Host.Audio.SoundPriority(p_id) : p_priority,
            };
            if (entry.Suffix == null) entry.SuffixId = 0;
            (p_betty ? Betty : Dialog).Queue(entry);
        }

        static MissionSpeech Betty => s_betty != null ? s_betty : s_betty = Create("Betty");

        static MissionSpeech Dialog => s_dialog != null ? s_dialog : s_dialog = Create("Mission dialog");

        static MissionSpeech Create(string p_name)
        {
            var lane = new MissionSpeech();
            Host.Audio.AttachSpeechLane(lane, p_name);
            return lane;
        }

        // QueueSpeech's insertion: the speaking line stays first; behind it, before the first of lower priority. A line
        // of a sound queued (with the same suffix) as often as it may play is dropped.
        void Queue(Entry p_entry)
        {
            if (Locked || queue.Count >= 8) return;
            if (queue.Count == 0)
            {
                queue.Add(p_entry);
                Speak(p_entry);
                return;
            }

            int max = p_entry.Id > 0 ? Host.Audio.SoundMaxCount(p_entry.Id) : -1;
            int same = 0;
            for (int i = 1; i < queue.Count; i++)
            {
                Entry queued = queue[i];
                if (queued.Id > 0 && queued.Id == p_entry.Id && (queued.SuffixId == 0 || queued.SuffixId == p_entry.SuffixId) &&
                    (max == -1 || ++same >= max))
                {
                    return;
                }

                if (queued.Priority < p_entry.Priority)
                {
                    queue.Insert(i, p_entry);
                    return;
                }
            }

            queue.Add(p_entry);
        }

        // StartSpeech: the line, then its suffix.
        void Speak(Entry p_entry)
        {
            speaking = p_entry;
            // A line with text and the speech off: the text, held 0x712 ticks.
            if (p_entry.Text != null && !Audio.SpeechOn)
            {
                suffixDue = false;
                if (p_entry.Text.Length != 0) TimedOverlays.ShowInGameMessage(p_entry.Text, 0, 0x712, 0x32);
                p_entry.HoldUntil = Clock.g_currentClock + 0x712;
                return;
            }

            suffixDue = p_entry.Suffix != null;
            Play(p_entry.Clip);
        }

        void Play(object p_clip)
        {
            voice?.Stop();
            voice = Host.Audio.PlayVoice(p_clip, Audio.SpeechOn ? Audio.VoiceScale : 0f);
        }

        // ReadSoundFile's data, else the resource the line's id names (FindResourceIdByName(0xb, name)).
        static object Load(string p_name) => Host.Audio.MissionLineClip(p_name);

        // AdvanceSpeechQueue: the next line once the last has ended (and its suffix with it). Once a frame.
        public void AdvanceSpeechQueue()
        {
            if ((voice != null && voice.IsPlaying) || queue.Count == 0) return;
            if (speaking != null && speaking.HoldUntil != 0 && Clock.g_currentClock < speaking.HoldUntil) return;
            if (speaking != null && suffixDue)
            {
                suffixDue = false;
                Play(speaking.Suffix);
                return;
            }

            if (queue.Count > 0 && queue[0] == speaking) queue.RemoveAt(0);
            speaking = null;
            if (queue.Count > 0) Speak(queue[0]);
        }

        // The lane torn down (its game object destroyed).
        public void Destroyed()
        {
            voice?.Stop();
            if (s_betty == this) s_betty = null;
            if (s_dialog == this) s_dialog = null;
            if (s_betty == null && s_dialog == null) Locked = false;
        }
    }
}
