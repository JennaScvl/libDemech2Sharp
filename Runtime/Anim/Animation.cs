namespace Demech2
{
    // Animated textures (demech2 MW2/src/animation.c): up to 0x200 animations, each playing one of 0x200 sets of up to
    // 0x20 CEL frames, advanced with the clock. Not the original's: an instance per PRJ, as a world may draw from two.
    public sealed class AnimationTable
    {
        public const int c_animationCount = 0x200, c_setCount = 0x200, c_framesPerSet = 0x20;
        public const int c_sequenceDelay = 0x2d; // a sequence's frame delay when started

        // AnimFrame (animframe.h): a CEL resource, loaded on first use.
        public struct AnimFrame
        {
            public short m_resourceId; // -1 for a free slot
            public short m_useCount;
            public bool m_loaded;
        }

        // Animation (animation.h), SIZE 0x0e
        public struct Animation
        {
            public short m_set;
            public short m_frame;
            public ushort m_delay;  // ticks per frame; 0 stops it
            public ushort m_mode;   // 0 stopped, 1 looping, 2 playing once
            public short m_flags;   // -2 for a free slot; 4: drawn without luma
            public int m_lastTime;
        }

        public readonly AnimFrame[,] g_animFrames = new AnimFrame[c_setCount, c_framesPerSet];
        public readonly Animation[] g_animations = new Animation[c_animationCount];

        int g_animInitialized = -2;
        int g_currentAnimSet;
        int g_animSetUsed;
        int g_animSetIsSequence;

        // g_lumaResourceId: the LUMA table the world names (the luma record).
        public int g_lumaResourceId;

        public AnimationTable() => Reset();

        // p_now: the real clock (every rendered frame).
        public void AdvanceAnimations(int p_now)
        {
            int now = p_now;
            for (int i = 0; i < c_animationCount; i++)
            {
                ref Animation anim = ref g_animations[i];
                if (anim.m_flags >= 0 && anim.m_mode != 0 && anim.m_delay > 0)
                {
                    if (anim.m_lastTime == 0)
                    {
                        anim.m_lastTime = now;
                    }

                    int elapsed = now - anim.m_lastTime;
                    int steps = elapsed / anim.m_delay;
                    int remainder = elapsed - anim.m_delay * steps;
                    uint frame = (uint)anim.m_frame;
                    if (steps > 0)
                    {
                        for (int j = 0; j < steps; j++)
                        {
                            frame++;
                            frame &= 0x1f;
                            if (g_animFrames[anim.m_set, frame].m_resourceId < 1)
                            {
                                frame = 0;
                            }
                        }

                        if (anim.m_mode == 2 && anim.m_frame > frame)
                        {
                            anim.m_mode = 0;
                            frame = 0;
                        }

                        anim.m_frame = (short)frame;
                        anim.m_lastTime = now - remainder;
                    }
                }
            }
        }

        public int AddAnimFrame(int p_resourceId, int p_set)
        {
            int slot = 0;
            bool found = false;
            if (g_animInitialized == -2)
            {
                InitAnimations();
            }

            if (p_set == -1)
            {
                if (g_animSetUsed != 0)
                {
                    g_currentAnimSet++;
                    g_animSetUsed = 0;
                }

                p_set = g_currentAnimSet;
            }

            if (p_resourceId <= 0 || p_set < 0 || p_set >= c_setCount)
            {
                return -1;
            }

            if (g_animFrames[p_set, 0].m_resourceId > 0)
            {
                g_animSetIsSequence = 1;
                while (!found)
                {
                    slot++;
                    if (slot >= c_framesPerSet)
                    {
                        return -1;
                    }

                    if (g_animFrames[p_set, slot].m_resourceId == -1)
                    {
                        found = true;
                    }
                }
            }
            else
            {
                g_animSetIsSequence = 0;
            }

            g_animFrames[p_set, slot].m_resourceId = (short)p_resourceId;
            return 0;
        }

        public int StartAnimation(int p_index, int p_set)
        {
            if (p_set == -1)
            {
                p_set = g_currentAnimSet;
                g_animSetUsed = 1;
            }

            if (p_set < 0 || p_set >= c_setCount || p_index < 0 || p_index >= c_animationCount)
            {
                return 0;
            }

            g_animations[p_index].m_set = (short)p_set;
            g_animations[p_index].m_flags = 1;
            g_animations[p_index].m_mode = 1;
            g_animations[p_index].m_frame = 0;
            if (g_animSetIsSequence != 0)
            {
                g_animations[p_index].m_delay = c_sequenceDelay;
            }

            return 1;
        }

        public void InitAnimations()
        {
            for (int i = 0; i < c_setCount; i++)
            {
                for (int j = 0; j < c_framesPerSet; j++)
                {
                    g_animFrames[i, j] = new AnimFrame { m_resourceId = -1 };
                }
            }

            for (int i = 0; i < c_animationCount; i++)
            {
                g_animations[i] = new Animation { m_set = -1, m_flags = -2, m_lastTime = -1 };
            }

            g_animInitialized = 0;
            g_currentAnimSet = 0;
            g_animSetUsed = 0;
            g_animSetIsSequence = 0;
        }

        public void SetAnimMode(int p_index, int p_mode)
        {
            if (g_animations[p_index].m_flags != -2)
            {
                g_animations[p_index].m_mode = (ushort)p_mode;
            }

            if (g_animations[p_index].m_delay == 0)
            {
                g_animations[p_index].m_delay = c_sequenceDelay;
            }
        }

        public void SetAnimFrame(int p_index, int p_frame)
        {
            if ((ushort)p_frame >= c_framesPerSet)
            {
                p_frame = 0;
            }

            ref Animation anim = ref g_animations[p_index];
            if (anim.m_flags < 0)
            {
                return;
            }

            if (g_animFrames[anim.m_set, p_frame].m_resourceId > 0)
            {
                anim.m_frame = (short)p_frame;
                anim.m_lastTime = 0;
            }
        }

        public void SetAnimDelay(int p_index, int p_delay)
        {
            g_animations[p_index].m_delay = (ushort)p_delay;
        }

        // The MW2.DLL load a mission starts with: every animation and frame forgotten, as the globals' initial values.
        public void Reset()
        {
            g_animInitialized = -2;
            g_currentAnimSet = 0;
            g_animSetUsed = 0;
            g_animSetIsSequence = 0;
            g_lumaResourceId = 0;
            InitAnimations();
            g_animInitialized = -2;
        }

        // DrawAnimatedPolygon's tests: a stopped or free animation, or one on an empty frame, draws nothing.
        public bool IsDrawn(int p_index)
        {
            if (p_index < 0 || p_index >= c_animationCount) return false;
            Animation anim = g_animations[p_index];
            if (anim.m_mode == 0 || anim.m_flags < 0) return false;
            return g_animFrames[anim.m_set, anim.m_frame].m_resourceId >= 1;
        }
    }
}
