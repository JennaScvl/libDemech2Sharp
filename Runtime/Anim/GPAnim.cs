namespace Demech2
{
    // The animation fields of a Player (demech2 MW2/include/players.h, 0x80-0x94) that the reels and gpanim.c share, and
    // what they need of the rest of the player.
    public sealed class MotionPlayer
    {
        public const int c_playerTypeTank = 4;

        // m_animFlags: 1 the reels run, 2 the frame plants a foot (mechclass.c rolls MASC on it), 4 off the ground,
        // 8 the frame plays m_pendingSound, 0x10 reset the reels (ResetMotion).
        public uint m_animFlags;
        public int m_motionState = -1;     // the animation state it reached, whose sound plays
        public int m_nextMotionState = -1; // the one it wants: 0 moving, 1 at full throttle, 2 reversing, or -1
        public int m_speedLevel;           // 0 to 3, from the throttle
        public int m_animRate;             // clock ticks per frame
        public int m_pendingSound = -1;    // the animsound record's sound (footsteps)

        public int m_index;   // for g_lastMotionSound
        public int m_type;
        public bool m_isLocal; // m_index == g_localPlayerId
        public bool m_aiMode;

        // What UpdateMotion reads of the mech and its steering.
        public int m_throttle = 0x400; // m_mech->m_throttle.m_value: 0x400 stopped, 0x800 full
        public bool m_reverse;         // m_steering->m_reverse

        // PlaySoundAt(offset, sound, g_inCockpitView), for the owner to play.
        public IMotionSoundHost m_soundHost;

        // Not in the original: its mech's LateUpdateMech runs UpdateMotion, not the sim loop.
        public bool m_driven;
    }

    // Motion state and its sounds (demech2 MW2/src/gpanim.c).
    public static class GPAnim
    {
        // g_motionSounds, by [m_motionState + 1][m_nextMotionState + 1].
        static readonly int[,] g_motionSounds =
        {
            { -1, 0x12e, 0x12e, -1 },
            { 0x14f, -1, -1, 0x14f },
            { 0x11a, 0x11a, 0x148, 0x11a },
            { 0x119, 0x119, 0x119, 0x12d },
        };

        static readonly int[] g_lastMotionSound = new int[60];

        public static void FirstGPAnim()
        {
            for (int i = 0; i < 60; i++)
            {
                g_lastMotionSound[i] = -2;
            }
        }

        static GPAnim() => FirstGPAnim();

        public static void StartMotion(MotionPlayer p_player)
        {
            if (!p_player.m_isLocal)
            {
                p_player.m_pendingSound = Host.Anim.MapSoundId(0x103);
            }

            p_player.m_speedLevel = 0;
            p_player.m_nextMotionState = -1;
        }

        public static void StopMotion(MotionPlayer p_player)
        {
            p_player.m_speedLevel = 0;
            p_player.m_nextMotionState = -1;
        }

        // Picks the player's motion state from its mech's throttle (2 while its steering's m_reverse is set), plays its
        // pending sound once (bit 8 of m_animFlags), and for the local player updates the looping sounds.
        public static void UpdateMotion(MotionPlayer p_player)
        {
            if (p_player.m_throttle <= 0x420)
            {
                StopMotion(p_player);
            }
            else
            {
                int height = p_player.m_throttle - 0x400;
                if (height < 0)
                {
                    height = 0;
                }

                p_player.m_nextMotionState = 0;
                if (height < 0x100)
                {
                    p_player.m_speedLevel = 1;
                }
                else if (height < 0x300)
                {
                    p_player.m_speedLevel = 2;
                }
                else
                {
                    p_player.m_speedLevel = 3;
                    p_player.m_nextMotionState = 1;
                }

                if (p_player.m_motionState == 2 && p_player.m_speedLevel > 2)
                {
                    p_player.m_speedLevel = 2;
                }

                if (p_player.m_reverse)
                {
                    p_player.m_nextMotionState = 2;
                }

                if (p_player.m_type == MotionPlayer.c_playerTypeTank)
                {
                    p_player.m_motionState = p_player.m_nextMotionState;
                }
            }

            if (p_player.m_pendingSound != -1 && (p_player.m_animFlags & 8) != 0)
            {
                p_player.m_animFlags &= ~8u;
                p_player.m_soundHost?.PlaySoundAt(p_player.m_pendingSound);
            }

            if (p_player.m_isLocal)
            {
                UpdateMotionSounds(p_player);
            }
        }

        static void UpdateMotionSounds(MotionPlayer p_player)
        {
            int id = p_player.m_index;
            if ((p_player.m_motionState != p_player.m_nextMotionState && g_lastMotionSound[id] != p_player.m_motionState) ||
                (p_player.m_motionState == p_player.m_nextMotionState && g_lastMotionSound[id] != -2))
            {
                int sound;
                if (p_player.m_motionState == 0 && g_lastMotionSound[id] == 2)
                {
                    sound = g_motionSounds[0, 1];
                }
                else
                {
                    sound = g_motionSounds[p_player.m_motionState + 1, p_player.m_nextMotionState + 1];
                }

                if (sound != -1)
                {
                    p_player.m_soundHost?.PlaySoundAt(Host.Anim.MapSoundId(sound));
                }

                if (p_player.m_motionState != p_player.m_nextMotionState)
                {
                    g_lastMotionSound[id] = p_player.m_motionState;
                }
                else
                {
                    g_lastMotionSound[id] = -2;
                }
            }
        }

        public static void ResetMotion(MotionPlayer p_player)
        {
            p_player.m_speedLevel = 0;
            p_player.m_nextMotionState = -1;
            p_player.m_motionState = -1;
            p_player.m_animFlags |= 0x10;
            g_lastMotionSound[p_player.m_index] = -2;
        }
    }
}
