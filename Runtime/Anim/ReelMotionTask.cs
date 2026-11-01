using System;
using System.Globalization;
using UnityEngine;

namespace Demech2
{
    // An animation of a loaded animation file (demech2 MW2/include/reel.h): per frame, an amount to move along or turn
    // about one axis, and the frame events every animation of its file shares. The original points into the loaded
    // resource; this keeps the resource and the offsets, so an event read before the first frame lands where the
    // original's would (in the amounts of the file's last animation).
    public sealed class Reel
    {
        public int ByName;     // the file was looked up by name (ResourceRef::m_id -1)
        public int FrameCount;
        public int Kind;       // 0-2: moves along x, y or z; 3-5: turns about them (16.16 degrees)

        public byte[] Data;
        public int AmountsAt;
        public int EventsAt;

        public int Amount(int frame) => BitConverter.ToInt32(Data, AmountsAt + frame * 4);

        // ReelEvent (reelevent.h), SIZE 0x08: m_flags, then m_values[4] as signed bytes.
        public uint EventFlags(int frame) => BitConverter.ToUInt32(Data, EventsAt + frame * 8);
        public sbyte EventValue(int frame, int i) => (sbyte)Data[EventsAt + frame * 8 + 4 + i];
    }

    // A timed callback moving a thing's object through an animation (demech2 MW2/src/objectanim.c ReelMotionTask). Its
    // data is "<thing or class id>;<rate>,<flags>,<animation>", made for the player being created (g_lastPlayer). Each
    // frame moves or turns the object by the frame's amount, spread over the rate; the frame events jump to other frames
    // by the player's animation state (m_motionState, the frame it reached, and m_nextMotionState, the one it wants).
    public sealed class ReelMotionTask : TimedCallback
    {
        // g_reelMotionError: why the task last failed: 1 no player, 2 disabled, 3 no object, 4-7 missing state.
        public static int g_reelMotionError;

        readonly MotionPlayer lastPlayer;
        readonly Func<int, Transform> findObject;

        // ReelMotion (SIZE 0x2c)
        Reel m_reel;
        Reel m_initial;
        Transform m_object;
        MotionPlayer m_player;
        int m_rate;       // clock ticks per frame
        int m_timer;      // until the next frame
        int m_amount;     // left to move in this frame
        int m_lastClock;
        int m_enabled;
        int m_frame;      // -1 before the first
        uint m_flags;     // 1: drives the player's animation state

        // p_lastPlayer is g_lastPlayer; p_findObject finds a thing's or class's object by id (FindThingIdxById and
        // GetClassObject, else FindClassById and GetShapeObject).
        public ReelMotionTask(MotionPlayer p_lastPlayer, Func<int, Transform> p_findObject)
        {
            lastPlayer = p_lastPlayer;
            findObject = p_findObject;
        }

        public Transform Object => m_object;

        public override bool Run(int p_event, string p_data, int p_clock, int p_period)
        {
            switch (p_event)
            {
                case 0:
                    return Create(p_data, p_clock);
                case 1:
                    return Step(p_clock);
                default:
                    return true;
            }
        }

        bool Create(string p_data, int p_clock)
        {
            if (lastPlayer == null)
            {
                g_reelMotionError = 1;
                return false;
            }

            m_object = null;
            m_reel = null;
            m_enabled = 0;
            m_rate = 0;
            m_flags = 0;
            m_player = lastPlayer;

            string idText = p_data;
            int semicolon = p_data.IndexOf(';');
            if (semicolon >= 0)
            {
                idText = p_data.Substring(0, semicolon);
                int rate = 0, enabled = 0, number = 0;
                ScanInts(p_data.Substring(semicolon + 1), ref rate, ref enabled, ref number);
                number += Host.Anim.AnimBase;
                if (number > Host.Anim.MaxAnimNumber)
                {
                    Host.Anim.MaxAnimNumber = number;
                }

                if (enabled != 0)
                {
                    m_enabled = 1;
                    if (enabled > 0x7f)
                    {
                        m_flags |= 1;
                    }
                }
                else
                {
                    m_enabled = 0;
                    g_reelMotionError = 2;
                }

                m_rate = rate;
                m_reel = Host.Anim.GetReel(number);
                m_initial = Host.Anim.GetReel(number);
                m_frame = -1;
                m_timer = 0;
                m_amount = 0;
                m_lastClock = p_clock;
                m_player.m_animRate = rate;
                m_player.m_motionState = -1;
            }

            m_object = findObject(Atoi(idText));
            if (m_object == null)
            {
                g_reelMotionError = 3;
            }

            return true;
        }

        bool Step(int p_clock)
        {
            if (m_object == null)
            {
                return false;
            }

            if (m_player == null)
            {
                g_reelMotionError = 5;
                return false;
            }

            if (m_enabled == 0)
            {
                g_reelMotionError = 4;
                return false;
            }

            int elapsed = p_clock - m_lastClock;
            m_lastClock = p_clock;
            if ((m_player.m_animFlags & 0x10) != 0)
            {
                m_reel = m_initial;
                m_frame = -1;
                m_timer = 0;
                m_amount = 0;
                m_lastClock = p_clock;
                if ((m_flags & 1) != 0)
                {
                    m_player.m_animRate = m_rate;
                    m_player.m_animFlags &= ~0x11u;
                    m_player.m_motionState = -1;
                }
            }
            else if ((m_player.m_animFlags & 1) != 0)
            {
                if (m_rate == 0)
                {
                    g_reelMotionError = 6;
                    return false;
                }

                if (m_player.m_animRate == 0)
                {
                    g_reelMotionError = 7;
                    return false;
                }

                // The original faults on an animation its file didn't have; this drops the task instead.
                if (m_reel == null)
                {
                    Debug.LogError($"ReelMotionTask: {m_object.name} has no animation");
                    return false;
                }

                Advance(elapsed);
            }
            else if ((m_flags & 1) != 0)
            {
                if (m_player.m_nextMotionState != -1 || m_player.m_motionState != -1)
                {
                    m_player.m_animFlags |= 1;
                    m_player.m_animRate = ObjectAnim.ScaleBySpeedLevel(m_player.m_speedLevel, m_rate);
                }
            }

            return true;
        }

        void Advance(int elapsed)
        {
            bool jump = false;
            int target = 0;

            int amount;
            if (m_timer <= 0)
            {
                amount = m_amount;
            }
            else
            {
                amount = unchecked(m_amount * elapsed) / m_timer;
            }

            if (m_amount < 0)
            {
                if (m_amount > amount)
                {
                    amount = m_amount;
                }
            }
            else if (m_amount < amount)
            {
                amount = m_amount;
            }

            if (m_reel.Kind >= 3)
            {
                int turnX = 0, turnY = 0, turnZ = 0;
                switch (m_reel.Kind)
                {
                    case 3:
                        turnX = amount;
                        break;
                    case 4:
                        turnY = amount;
                        break;
                    case 5:
                        turnZ = amount;
                        break;
                }

                Host.Anim.RotateObj(m_object, turnX, turnY, turnZ);
            }
            else
            {
                int x = 0, y = 0, z = 0;
                switch (m_reel.Kind)
                {
                    case 0:
                        x = amount;
                        break;
                    case 1:
                        y = amount;
                        break;
                    case 2:
                        z = amount;
                        break;
                }

                Host.Anim.MoveObj(m_object, x, y, z);
            }

            m_amount -= amount;
            m_timer -= elapsed;
            if (m_frame == -1)
            {
                m_frame = 0;
                m_timer = 0;
                m_player.m_motionState = 0;
            }

            if (m_timer > 0)
            {
                return;
            }

            Reel reel = m_reel;
            int frame = m_frame;
            m_frame++;
            m_timer += m_player.m_animRate;
            if (-m_player.m_animRate > m_timer)
            {
                m_timer = -m_player.m_animRate;
            }

            uint flags = reel.EventFlags(frame);
            if ((flags & 0x40) != 0)
            {
                target = reel.EventValue(frame, 1);
                jump = m_player.m_nextMotionState == target;
            }

            if ((flags & 0x80) != 0)
            {
                target = reel.EventValue(frame, 1);
                if (m_player.m_nextMotionState != -1 && m_player.m_motionState != m_player.m_nextMotionState)
                {
                    jump = true;
                }
            }

            if ((flags & 0x100) != 0)
            {
                target = reel.EventValue(frame, 2);
                if (m_player.m_nextMotionState == -1)
                {
                    jump = true;
                }
            }

            if (jump)
            {
                jump = false;
                int step = m_player.m_motionState < target ? 1 : -1;

                bool found = false;
                int next = frame;
                while (!found)
                {
                    next += step;
                    if (next >= reel.FrameCount)
                    {
                        next = -1;
                    }

                    if (next == -1)
                    {
                        found = true;
                    }
                    else if ((reel.EventFlags(next) & 1) != 0)
                    {
                        if (reel.EventValue(next, 0) == target)
                        {
                            found = true;
                        }
                    }
                }

                if (next != -1)
                {
                    found = false;
                    while (!found)
                    {
                        if (next >= reel.FrameCount)
                        {
                            next = -1;
                            found = true;
                        }
                        else
                        {
                            if ((reel.EventFlags(next) & 0x200) != 0)
                            {
                                if (m_player.m_motionState == reel.EventValue(next, 3))
                                {
                                    found = true;
                                }
                            }

                            next++;
                        }
                    }
                }

                if (next != -1)
                {
                    m_frame = next;
                    jump = true;
                }
            }

            if (!jump && (flags & 0xc) != 0)
            {
                bool match = true;
                if ((flags & 4) != 0)
                {
                    match = false;
                    for (int i = 0; i < 4; i++)
                    {
                        if (reel.EventValue(frame, i) == -1)
                        {
                            break;
                        }

                        match = reel.EventValue(frame, i) == (sbyte)m_player.m_nextMotionState;
                        if (match)
                        {
                            break;
                        }
                    }
                }

                if (match)
                {
                    // The original tests the flags before the index, so it can read the event before frame 0.
                    int back = frame - 1;
                    while ((reel.EventFlags(back) & 2) == 0 && back >= 0)
                    {
                        back--;
                    }

                    m_frame = back;
                }
            }

            if ((!jump && (flags & 0x400) != 0) || m_frame >= reel.FrameCount)
            {
                m_timer = 0;
                m_frame = -1;
            }

            if (m_frame != -1)
            {
                m_amount += reel.Amount(m_frame);
            }

            if ((m_flags & 1) != 0)
            {
                if (jump)
                {
                    m_player.m_motionState = target;
                }

                m_player.m_animFlags &= ~2u;
                m_player.m_animFlags &= ~8u;
                if (m_frame != -1)
                {
                    m_player.m_animRate = ObjectAnim.ScaleBySpeedLevel(m_player.m_speedLevel, m_rate);
                    if ((reel.EventFlags(m_frame) & 0x10) != 0)
                    {
                        m_player.m_animFlags |= 2;
                    }

                    if ((reel.EventFlags(m_frame) & 0x800) != 0)
                    {
                        m_player.m_animFlags |= 8;
                    }
                }
                else if (m_player.m_nextMotionState == -1)
                {
                    m_player.m_animFlags &= ~1u;
                    m_player.m_motionState = -1;
                }
            }
        }

        // sscanf(token, "%ld,%d,%d"): stops at the first field that doesn't parse, leaving the rest.
        static void ScanInts(string p_text, ref int a, ref int b, ref int c)
        {
            string[] fields = p_text.Split(',');
            if (fields.Length < 1 || !TryLeadingInt(fields[0], out a)) return;
            if (fields.Length < 2 || !TryLeadingInt(fields[1], out b)) return;
            if (fields.Length < 3) return;
            TryLeadingInt(fields[2], out c);
        }

        // atoi: leading whitespace, a sign and digits; 0 if none.
        static int Atoi(string p_text)
        {
            TryLeadingInt(p_text, out int value);
            return value;
        }

        static bool TryLeadingInt(string p_text, out int value)
        {
            value = 0;
            int i = 0;
            while (i < p_text.Length && char.IsWhiteSpace(p_text[i])) i++;
            int start = i;
            if (i < p_text.Length && (p_text[i] == '-' || p_text[i] == '+')) i++;
            int digits = i;
            while (i < p_text.Length && p_text[i] >= '0' && p_text[i] <= '9') i++;
            if (i == digits) return false;
            value = int.Parse(p_text.Substring(start, i - start), CultureInfo.InvariantCulture);
            return true;
        }
    }
}
