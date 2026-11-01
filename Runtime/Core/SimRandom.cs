using Unity.Collections;

namespace Demech2
{
    // The sim's random numbers (demech2 MW2/src/random.c): two 127-entry tables filled once by InitRandom from the C
    // runtime's rand() (MSVC's: holdrand * 214013 + 2531011, bits 16-30), read round-robin. The tables and their indexes
    // live in persistent native memory so Burst jobs (DebrisJob) can draw from them.
    //
    // Two streams: the gameplay one (the original's tables, seed 0), which only the lockstep sim step draws from, and
    // SimRandom.Cosmetic (debris, effects, sounds, cockpit static: not the original's split), seeded apart so whatever
    // the render side draws never moves the gameplay sequence.
    public static class SimRandom
    {
        const int Count = 127;
        const int RandMax = 0x7fff;

        // The cosmetic stream's seed: any but the gameplay one's.
        const uint CosmeticSeed = 0x5eed;

        // One stream's g_randomInts, g_normalRandomInts; [0] g_randomIndex, [1] g_normalRandomIndex, [2] g_randomIndex2,
        // [3] g_normalRandomIndex2.
        struct Stream
        {
            public NativeArray<int> RandomInts, NormalRandomInts, Indexes;

            public bool IsCreated => RandomInts.IsCreated;

            public void Dispose()
            {
                if (RandomInts.IsCreated) RandomInts.Dispose();
                if (NormalRandomInts.IsCreated) NormalRandomInts.Dispose();
                if (Indexes.IsCreated) Indexes.Dispose();
            }
        }

        static Stream s_gameplay, s_cosmetic;

        public static NativeArray<int> RandomInts
        {
            get
            {
                Ensure();
                return s_gameplay.RandomInts;
            }
        }

        public static NativeArray<int> Indexes
        {
            get
            {
                Ensure();
                return s_gameplay.Indexes;
            }
        }

        // InitRandom(p_seed): srand, then 127 rand()s, then 127 sums of 30 rand()s scaled to a normal deviate * 1024.
        public static void InitRandom(uint p_seed)
        {
            s_gameplay.Dispose();
            s_gameplay = Fill(p_seed);
            UnityEngine.Application.quitting -= Dispose;
            UnityEngine.Application.quitting += Dispose;
        }

        // NormalScale: sqrt(90) * 2^28, floored (isqrt(90 << 56)).
        const long NormalScale = 2546602337L;

        static Stream Fill(uint p_seed)
        {
            var stream = new Stream
            {
                RandomInts = new NativeArray<int>(Count, Allocator.Persistent),
                NormalRandomInts = new NativeArray<int>(Count, Allocator.Persistent),
                Indexes = new NativeArray<int>(4, Allocator.Persistent),
            };

            uint holdrand = p_seed;
            int Rand()
            {
                holdrand = holdrand * 214013 + 2531011;
                return (int)((holdrand >> 16) & 0x7fff);
            }

            for (int i = 0; i < Count; i++)
            {
                stream.RandomInts[i] = Rand();
            }

            // The original's (int)((sum * range * 2 / (30 * RAND_MAX) - range) * 1024) with range sqrt(90), in integers:
            // the same value for every possible sum (checked against the double form over all 0..30 * RAND_MAX).
            const long span = 30L * RandMax;
            for (int i = 0; i < Count; i++)
            {
                int sum = 0;
                for (int j = 0; j < 30; j++)
                {
                    sum += Rand();
                }

                stream.NormalRandomInts[i] = (int)((2L * sum - span) * NormalScale * 1024 / (span << 28));
            }

            return stream;
        }

        // The tables as the sim starts them: InitRandom(seed) with WinMain's seed of 0.
        static void Ensure()
        {
            if (!s_gameplay.IsCreated) InitRandom(0);
        }

        public static int RandomIntBelow(int p_max)
        {
            Ensure();
            return RandomIntBelow(s_gameplay.RandomInts, s_gameplay.Indexes, p_max);
        }

        // RandomIntBelow on the given tables (for Burst jobs).
        public static int RandomIntBelow(NativeArray<int> p_randomInts, NativeArray<int> p_indexes, int p_max)
        {
            int value = p_randomInts[p_indexes[0]] % p_max;
            p_indexes[0] = (p_indexes[0] + 1) % Count;
            return value;
        }

        public static int RandomNormal()
        {
            Ensure();
            return RandomNormal(ref s_gameplay);
        }

        static int RandomNormal(ref Stream p_stream)
        {
            int value = p_stream.NormalRandomInts[p_stream.Indexes[1]];
            p_stream.Indexes[1] = (p_stream.Indexes[1] + 1) % Count;
            return value;
        }

        public static int RandomIntBelow2(int p_max)
        {
            Ensure();
            int value = s_gameplay.RandomInts[s_gameplay.Indexes[2]] % p_max;
            s_gameplay.Indexes[2] = (s_gameplay.Indexes[2] + 1) % Count;
            return value;
        }

        public static int RandomNormal2()
        {
            Ensure();
            int value = s_gameplay.NormalRandomInts[s_gameplay.Indexes[3]];
            s_gameplay.Indexes[3] = (s_gameplay.Indexes[3] + 1) % Count;
            return value;
        }

        public static void Dispose()
        {
            s_gameplay.Dispose();
            s_cosmetic.Dispose();
        }

        // The cosmetic stream: the same tables' shape and draws, its own seed and indexes. Nothing the sim step's
        // gameplay state depends on may draw from it.
        public static class Cosmetic
        {
            static void Ensure()
            {
                if (s_cosmetic.IsCreated) return;
                s_cosmetic = Fill(CosmeticSeed);
                UnityEngine.Application.quitting -= SimRandom.Dispose;
                UnityEngine.Application.quitting += SimRandom.Dispose;
            }

            public static NativeArray<int> RandomInts
            {
                get
                {
                    Ensure();
                    return s_cosmetic.RandomInts;
                }
            }

            public static NativeArray<int> Indexes
            {
                get
                {
                    Ensure();
                    return s_cosmetic.Indexes;
                }
            }

            public static int RandomIntBelow(int p_max)
            {
                Ensure();
                return SimRandom.RandomIntBelow(s_cosmetic.RandomInts, s_cosmetic.Indexes, p_max);
            }

            public static int RandomNormal()
            {
                Ensure();
                return SimRandom.RandomNormal(ref s_cosmetic);
            }
        }
    }
}
