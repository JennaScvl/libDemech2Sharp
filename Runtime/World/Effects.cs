using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // Explosions, fires, flashes and debris (shots.c: SpawnEffectEx, UpdateEffects, ScatterDebris, StartNuke, UpdateNuke;
    // world.c's xplode records; fadepal.c EmitWreckSmoke), the Matrox edition's floats as the lockstep's fixed point
    // (Fixed) where they are the sim's (the effects' places, timing, crowding and damage; their shapes, lights and sounds the
    // game's, Host.Fx). MW2 has no particles: each of the 0x100 effect slots is a shape (an xplode record's) shown where the
    // effect goes off for its type's duration, its animated faces playing once (SetAnimMode 2). Its type (g_effectInfo)
    // also gives it a sound, a palette flash and the effect camera, and whether it damages what is near it. Debris (type
    // 0xb) slots are thrown as Debris pieces.
    //
    // An effect that damages hurts the mechs (with splash damage), the game things and the chunks near it while it lasts,
    // and fires heat the mechs near them (HeatMechsNearFires).
    public static class Effects
    {
        public const int c_impactMech = 0x100, c_impactThing = 0x200, c_impactGround = 0x400;
        public const int c_slotCount = 0x100;

        // EffectInfo (SIZE 0x1c)
        public readonly struct EffectInfo
        {
            public readonly int m_duration;    // ticks
            public readonly int m_flash;       // a palette flash when the camera follows, or -1
            public readonly int m_sound;       // a sound effect, or <= 0
            public readonly int m_soundChance; // the percent chance of the sound, or -1 for always
            public readonly bool m_camera;     // the effect camera may follow it
            public readonly bool m_needsObject; // only a slot with a scene object can play it
            public readonly bool m_damages;    // it deals splash damage while it lasts
            // Titanium's (SIZE 0x2c): the damage (the classic's 1/256) and heat per tick, and the flags (1 sticks to the
            // mech it hit, 2 never crowded out); its sounds are the loaded PRJ's.
            public readonly float m_damageRate, m_heatRate;
            public readonly int m_damageRateFx, m_heatRateFx; // the sim's, 16.16 (truncated as the sim took them)
            public readonly int m_flags;
            readonly bool m_prjSound;

            public EffectInfo(int p_duration, int p_flash, int p_sound, int p_soundChance, int p_camera, int p_needsObject, int p_damages)
            {
                m_duration = p_duration;
                m_flash = p_flash;
                m_sound = p_sound;
                m_soundChance = p_soundChance;
                m_camera = p_camera != 0;
                m_needsObject = p_needsObject != 0;
                m_damages = p_damages != 0;
                m_damageRate = 1f / 256;
                m_heatRate = 0;
                m_damageRateFx = 256;
                m_heatRateFx = 0;
                m_flags = 0;
                m_prjSound = false;
            }

            public EffectInfo(int p_duration, int p_flash, int p_sound, int p_soundChance, int p_camera, int p_needsObject,
                int p_damages, float p_damageRate, float p_heatRate, int p_flags)
                : this(p_duration, p_flash, p_sound, p_soundChance, p_camera, p_needsObject, p_damages)
            {
                m_damageRate = p_damageRate;
                m_heatRate = p_heatRate;
                m_damageRateFx = (int)(p_damageRate * 65536f);
                m_heatRateFx = (int)(p_heatRate * 65536f);
                m_flags = p_flags;
                m_prjSound = true;
            }

            // The sound as the loaded PRJ's id.
            public int SoundId => m_prjSound ? m_sound : Host.Anim.MapSoundId(m_sound);
        }

        // The base game's effect types (Tables/BlastKinds.json), read on first use. Held in a nested class so the state
        // walk (which walks Effects' own statics) never sees it.
        static class BlastKinds
        {
            internal static readonly EffectInfo[] All = Load();

            static EffectInfo[] Load()
            {
                BlastKindRow[] rows = SimTables.BlastKinds;
                var info = new EffectInfo[rows.Length];
                for (int i = 0; i < rows.Length; i++)
                {
                    BlastKindRow r = rows[i];
                    info[i] = new EffectInfo(r.lifeTicks, r.paletteFlash, r.soundId, r.soundOdds, r.followCam, r.wantsTarget, r.hurts);
                }
                return info;
            }
        }

        public static EffectInfo[] BaseInfo => BlastKinds.All;

        // Titanium's g_effectInfo (mercsw.dll 0x1009bb08, 0x24 entries): 0xa the inferno's fire, 0x22 the torpedo's
        // blast, 0x23 the Arrow IV's.
        public static readonly EffectInfo[] TitaniumEffectInfo =
        {
            new EffectInfo(271, -1, 272, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x00
            new EffectInfo(271, -1, 272, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x01
            new EffectInfo(271, -1, 272, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x02
            new EffectInfo(253, 1, 227, -1, 1, 1, 1, 1f / 256, 0, 0), // 0x03
            new EffectInfo(253, 1, 227, -1, 1, 1, 1, 1f / 256, 0, 0), // 0x04
            new EffectInfo(90, -1, 271, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x05
            new EffectInfo(579, 2, 256, -1, 1, 1, 1, 1f / 256, 0, 0), // 0x06
            new EffectInfo(398, 1, 205, -1, 0, 1, 1, 1f / 256, 0, 0), // 0x07
            new EffectInfo(90, -1, 269, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x08
            new EffectInfo(162, -1, 260, 10, 0, 1, 0, 1f / 256, 0, 0), // 0x09
            new EffectInfo(905, -1, 95, -1, 1, 1, 1, 0, 1f / 64, 3), // 0x0a
            new EffectInfo(1810, -1, -1, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x0b
            new EffectInfo(398, 1, 227, -1, 1, 1, 1, 1f / 256, 0, 0), // 0x0c
            new EffectInfo(579, 1, 267, -1, 1, 1, 1, 1f / 256, 0, 0), // 0x0d
            new EffectInfo(72, -1, 270, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x0e
            new EffectInfo(72, -1, 270, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x0f
            new EffectInfo(72, -1, 270, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x10
            new EffectInfo(108, 1, 277, -1, 1, 1, 1, 1f / 256, 0, 0), // 0x11
            new EffectInfo(144, -1, 273, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x12
            new EffectInfo(362, 1, 227, -1, 1, 1, 1, 1f / 256, 0, 0), // 0x13
            new EffectInfo(362, 1, 227, -1, 1, 1, 1, 1f / 256, 0, 0), // 0x14
            new EffectInfo(579, 2, 256, -1, 1, 1, 1, 1f / 256, 0, 0), // 0x15
            new EffectInfo(1810, -1, 226, -1, 1, 1, 1, 1f / 8, 1f / 64, 2), // 0x16
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x17
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x18
            new EffectInfo(72, -1, -1, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x19
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x1a
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x1b
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x1c
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x1d
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x1e
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x1f
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x20
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 1f / 256, 0, 0), // 0x21
            new EffectInfo(181, 1, 362, -1, 1, 1, 1, 1f / 64, 0, 0), // 0x22
            new EffectInfo(181, 1, 285, -1, 1, 1, 1, 1f / 8, 0, 2), // 0x23
        };

        // Ghost Bear's Legacy's g_effectInfo (GBL MW2.flat 0xb0d04, 0x24 entries of 0x2c: duration, flash, sound, chance,
        // camera, needs object, damages, an unread dword (2; 1 for the inferno), the damage and heat rates (16.16) and the
        // flags): Titanium's layout, its own values; its sounds its PRJ's ids (467 the inferno, 457 the torpedo, 465 Arrow
        // IV). Its SpawnEffect (0x52040) and UpdateEffects (0x52670) are Titanium's but for the details Gbl marks.
        public static readonly EffectInfo[] GblEffectInfo =
        {
            new EffectInfo(273, -1, 251, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x00
            new EffectInfo(273, -1, 251, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x01
            new EffectInfo(273, -1, 251, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x02
            new EffectInfo(254, 1, 210, -1, 1, 1, 1, 256f / 65536, 0, 0), // 0x03
            new EffectInfo(254, 1, 210, -1, 1, 1, 1, 256f / 65536, 0, 0), // 0x04
            new EffectInfo(91, -1, 250, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x05
            new EffectInfo(582, 2, 235, -1, 1, 1, 1, 256f / 65536, 0, 0), // 0x06
            new EffectInfo(400, 1, 188, -1, 0, 1, 1, 256f / 65536, 0, 0), // 0x07
            new EffectInfo(91, -1, 248, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x08
            new EffectInfo(163, -1, 239, 10, 0, 1, 0, 256f / 65536, 0, 0), // 0x09
            new EffectInfo(910, -1, 467, -1, 1, 1, 1, 128f / 65536, 1024f / 65536, 3), // 0x0a
            new EffectInfo(1820, -1, -1, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x0b
            new EffectInfo(400, 1, 210, -1, 1, 1, 1, 256f / 65536, 0, 0), // 0x0c
            new EffectInfo(582, 1, 246, -1, 1, 1, 1, 256f / 65536, 0, 0), // 0x0d
            new EffectInfo(72, -1, 249, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x0e
            new EffectInfo(72, -1, 249, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x0f
            new EffectInfo(72, -1, 249, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x10
            new EffectInfo(109, 1, 255, -1, 1, 1, 1, 256f / 65536, 0, 0), // 0x11
            new EffectInfo(145, -1, 252, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x12
            new EffectInfo(364, 1, 210, -1, 1, 1, 1, 256f / 65536, 0, 0), // 0x13
            new EffectInfo(364, 1, 210, -1, 1, 1, 1, 256f / 65536, 0, 0), // 0x14
            new EffectInfo(582, 2, 235, -1, 1, 1, 1, 256f / 65536, 0, 0), // 0x15
            new EffectInfo(1820, -1, 209, -1, 1, 1, 1, 8192f / 65536, 1024f / 65536, 2), // 0x16
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x17
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x18
            new EffectInfo(72, -1, -1, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x19
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x1a
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x1b
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x1c
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x1d
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x1e
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x1f
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x20
            new EffectInfo(36, -1, -1, -1, 0, 1, 0, 256f / 65536, 0, 0), // 0x21
            new EffectInfo(254, 1, 457, -1, 1, 1, 1, 1024f / 65536, 0, 0), // 0x22
            new EffectInfo(254, 1, 465, -1, 1, 1, 1, 8192f / 65536, 0, 2), // 0x23
        };

#if UNITY_EDITOR || MEKENGINE_MERCS
        // Mercenaries' g_effectInfo (DOS-Win95 MERCSW.DLL 0x100a9670, 0x24 entries of 0x2c; both editions): Titanium's but
        // the inferno's fire (0xa), which damages 128 a tick (1/512).
        public static readonly EffectInfo[] MercsEffectInfo = BuildMercsEffectInfo();

        static EffectInfo[] BuildMercsEffectInfo()
        {
            var info = (EffectInfo[])TitaniumEffectInfo.Clone();
            info[0x0a] = new EffectInfo(905, -1, 95, -1, 1, 1, 1, 1f / 512, 1f / 64, 3);
            return info;
        }
#endif

        static bool Titanium => WeaponDef.Titanium;
        // GBL's effect rules; a MekNet match runs Titanium's (WeaponDef.Titanium).
        static bool Gbl => Host.Variant.IsGhostBearsLegacy && !Host.Net.NetRules;
#if UNITY_EDITOR || MEKENGINE_MERCS
        // Mercenaries (SpawnEffect 0x1004ab46, UpdateEffects 0x1004b4ef): Titanium's effects with GBL's approximate distance
        // and splash damage (a quarter in the head; its heat goes to mech +0x3c8, which nothing reads), only Arrow IV's
        // blast growing (24 a tick).
        static bool Mercs => WeaponDef.Mercs;
#endif
        // The 0x24-type effects (Titanium's, Mercenaries', and GBL's).
        public static bool TiFx => Titanium || Gbl;
#if UNITY_EDITOR || MEKENGINE_MERCS
        public static EffectInfo[] Info => Mercs ? MercsEffectInfo : Titanium ? TitaniumEffectInfo : Gbl ? GblEffectInfo : BaseInfo;
#else
        public static EffectInfo[] Info => Titanium ? TitaniumEffectInfo : Gbl ? GblEffectInfo : BaseInfo;
#endif

        // The running edition's own entry for an effect type, for what it plays: its sound (its PRJ's ids) and palette
        // flash. In a MekNet match the rules' table is Titanium's whatever the edition, so the edition's own table at the
        // type it shows (the game's EffectVisualType); outside one, Info's.
        static EffectInfo LocalInfo(int p_type)
        {
            if (!Host.Net.NetRules) return Info[p_type];
#if UNITY_EDITOR || MEKENGINE_MERCS
            EffectInfo[] own = WeaponDef.NativeMercs ? MercsEffectInfo : WeaponDef.NativeTitanium ? TitaniumEffectInfo
                : Host.Variant.IsGhostBearsLegacy ? GblEffectInfo : BaseInfo;
#else
            EffectInfo[] own = WeaponDef.NativeTitanium ? TitaniumEffectInfo : Host.Variant.IsGhostBearsLegacy ? GblEffectInfo : BaseInfo;
#endif
            int type = Host.Fx.EffectVisualType(p_type);
            return own[type >= 0 && type < own.Length ? type : 3];
        }

        // Effect (SIZE 0x28)
        public sealed class Effect
        {
            public ISceneObject m_object;
            public FixVec m_position;   // 16.16 units
            public int m_timeLeft;      // ticks
            public bool m_active;
            public int m_animation = -1;
            public bool m_hasCamera;
            public int m_type = -1;     // an index into g_effectInfo
            public int m_owner = -1;    // the player it damages on behalf of, or -2
            public Shape m_follow;      // Titanium: the mech part a sticky effect (flags 1) rides on
            public int m_billboard;     // not the original's: 0 none, 1 turned to the eye about the vertical, 2 turned to it fully
        }

        public static readonly Effect[] g_effects = new Effect[c_slotCount];
        public static int g_nextEffectRecord;

        // g_launchEffectPlayer's firing object (SpawnLaunchEffect places the effect there).
        static Transform g_launchEffectFiringObj;

        static int g_nukeTimeLeft;
        static long g_nukeMaxRadius, g_nukeRadius; // 16.16 units
        static FixVec g_nukePosition;

        // FirstShots' effect half: every slot empty.
        public static void FirstEffects()
        {
            for (int i = 0; i < c_slotCount; i++)
            {
                g_effects[i] = new Effect();
                ResetEffectSlot(i);
            }

            g_nextEffectRecord = 0;
            Host.Fx.ResetEffectLooks();
            g_nukeTimeLeft = 0;
            Debris.ZeroChunx();
        }

        public static void ResetEffectSlot(int p_index)
        {
            Effect effect = g_effects[p_index];
            effect.m_position = FixVec.Zero;
            effect.m_timeLeft = 0;
            effect.m_active = false;
            effect.m_hasCamera = false;
            effect.m_owner = -1;
            effect.m_follow = null;
        }

        // Not the original's: a MekNet match runs Titanium's effects on every edition's world (WeaponDef.Titanium). An
        // effect with no free slot of its type takes a slot no xplode record filled, made its type for good, shown as the
        // world's own shape of it or of its nearest (the game's ClaimEffectLook; none: unseen, still burning and
        // damaging). The damage reaches Titanium's shape radius (TitaniumEffectRadius), so every machine damages alike.
        static int Claim(int p_type)
        {
            for (int i = g_nextEffectRecord; i < c_slotCount; i++)
            {
                Effect effect = g_effects[i];
                if (effect.m_type != -1 || effect.m_object != null) continue;
                effect.m_type = p_type;
                effect.m_active = false;
                Host.Fx.ClaimEffectLook(i, p_type);
                return i;
            }

            return -1;
        }

        // Titanium's radii by effect type (units), from its worlds' xplode shapes; a type not listed takes its object's.
        static readonly Dictionary<int, float> TitaniumEffectRadius = new Dictionary<int, float>
        {
            { 0x0, 282.1365f }, { 0x1, 282.1365f }, { 0x2, 282.1365f }, { 0x3, 564.273f }, { 0x4, 564.273f },
            { 0x5, 141.068253f }, { 0x6, 1410.6825f }, { 0x7, 140.714249f }, { 0x8, 141.068253f }, { 0x9, 141.068253f },
            { 0xa, 707.1068f }, { 0xb, 47.92703f }, { 0xc, 1410.6825f }, { 0xd, 1906.1377f }, { 0xe, 282.1365f },
            { 0xf, 282.1365f }, { 0x10, 282.1365f }, { 0x11, 2116.02368f }, { 0x12, 141.068253f }, { 0x13, 1410.6825f },
            { 0x14, 1410.6825f }, { 0x15, 1414.21362f }, { 0x16, 7071.068f }, { 0x17, 140.714249f }, { 0x19, 258.0097f },
            { 0x22, 564.273f }, { 0x23, 1410.6825f },
        };

        // An effect's damage radius (16.16 units): Titanium's in a MekNet match, else its object's; false without either.
        static bool DamageRadius(Effect p_effect, out long p_radius)
        {
            if (Host.Net.NetRules && TitaniumEffectRadius.TryGetValue(p_effect.m_type, out float radius))
            {
                p_radius = Fixed.FromFloatL(radius);
                return true;
            }

            p_radius = p_effect.m_object != null ? Fixed.FromFloatL(p_effect.m_object.Radius) : 0;
            return p_effect.m_object != null;
        }

        // ---- Spawning -----------------------------------------------------------------------------------------------------

        // p_at, p_camera: 16.16 units.
        public static void SpawnEffect(int p_owner, int p_type, FixVec p_at, FixVec p_camera, Shape p_hit = null) =>
            SpawnEffectEx(p_owner, p_type, p_at, p_camera, Vector3.zero, p_hit);

        // Float units.
        public static void SpawnEffect(int p_owner, int p_type, Vector3 p_at, Vector3 p_camera, Shape p_hit = null) =>
            SpawnEffect(p_owner, p_type, FixVec.FromFloat(p_at), FixVec.FromFloat(p_camera), p_hit);

        public static void SpawnRotatedEffect(int p_type, Vector3 p_angles, FixVec p_at) => SpawnEffectEx(-2, p_type, p_at, p_at, p_angles);

        // An effect at a player's firing object (the jump jets' flames, muzzle flashes): types 0x17 and up.
        public static void SpawnLaunchEffect(int p_type, Transform p_firingObj)
        {
            g_launchEffectFiringObj = p_firingObj;
            SpawnEffectEx(-2, p_type, FixVec.Zero, FixVec.Zero, Vector3.zero);
        }

        // Spawns an effect. The low byte of p_type is the effect; the high byte says what a shot hit (c_impactMech etc.),
        // which picks a variant of it, and 0x4000 keeps its sound quiet. The effect takes a free slot of its type (none if
        // three of them already burn within 500 units), and the effect camera may follow it from p_camera. p_angles:
        // degrees. Titanium (SpawnEffect 0x10011f58): 0x24 types, the torpedo's and Arrow IV's (0x22, 0x23) varying as the
        // missiles' 3 does; launch effects and flags-2 types never crowded out; a sticky type (flags 1) rides on the mech
        // part it hit (p_hit), else lasts 0xb5 ticks.
        // Not the original's: the game's look of the shot DetonateShot is setting off (Host.Fx.ImpactFxFor; null
        // otherwise).
        public static object g_impactFx;

        // Not the original's: an explosion (a missile's impact, something destroyed) is heard over MW2's falloff rather
        // than the natural one. Its chained effects too.
        public static bool g_explosionSound;

        public static void SpawnExplosion(int p_owner, int p_type, FixVec p_at, FixVec p_camera, Shape p_hit = null)
        {
            bool was = g_explosionSound;
            g_explosionSound = true;
            try { SpawnEffect(p_owner, p_type, p_at, p_camera, p_hit); }
            finally { g_explosionSound = was; }
        }

        public static void SpawnExplosion(int p_owner, int p_type, Vector3 p_at, Vector3 p_camera, Shape p_hit = null) =>
            SpawnExplosion(p_owner, p_type, FixVec.FromFloat(p_at), FixVec.FromFloat(p_camera), p_hit);

        public static void SpawnEffectEx(int p_owner, int p_type, FixVec p_at, FixVec p_camera, Vector3 p_angles,
            Shape p_hit = null)
        {
            if (g_effects[0] == null) FirstEffects();
            object impactFx = g_impactFx; // the first effect's only, not a chained one's
            g_impactFx = null;
            bool quiet = false, chain = false;
            int chainType = -1;
            if (p_type < 0) return;

            int flags = p_type & 0xff00;
            p_type &= 0xff;
            if (p_type >= (TiFx ? 0x24 : 0x20)) return;
            if ((flags & 0x4000) != 0) quiet = true;

            switch (p_type)
            {
                case 5:
                    if ((flags & c_impactMech) != 0)
                    {
                        p_at.X = p_camera.X;
                        p_at.Z = p_camera.Z;
                        p_type = 8;
                    }
                    else if ((flags & c_impactGround) != 0) p_type = 9;
                    else if ((flags & 0x2000) != 0) p_type = 0x12;
                    break;
                case 3:
                case 0x22:
                case 0x23:
                    if ((flags & c_impactGround) != 0) p_type = 0xc;
                    else if ((flags & 0x2000) != 0) p_type = 0x11;
                    else if ((flags & c_impactThing) != 0) p_type = 0x13;
                    chain = true;
                    chainType = 0x10b;
                    break;
                case 4:
                    if ((flags & c_impactGround) != 0) p_type = 0xc;
                    else if ((flags & 0x2000) != 0) p_type = 0x11;
                    else if ((flags & c_impactThing) != 0) p_type = 0x14;
                    chain = true;
                    chainType = 0x10b;
                    break;
                case 0:
                    if ((flags & 0x2000) != 0) p_type = 0xe;
                    else if ((flags & c_impactGround) != 0) p_type = 9;
                    break;
                case 1:
                    if ((flags & 0x2000) != 0) p_type = 0xf;
                    else if ((flags & c_impactGround) != 0) p_type = 9;
                    break;
                case 2:
                    if ((flags & 0x2000) != 0) p_type = 0x10;
                    else if ((flags & c_impactGround) != 0) p_type = 9;
                    break;
                case 6:
                    if ((flags & c_impactGround) != 0) p_type = 0x15;
                    break;
                case 0xb:
                {
                    int pieces;
                    if ((flags & 0x1000) != 0) pieces = 0x10;
                    else if ((flags & c_impactMech) != 0) pieces = 4;
                    else if ((flags & c_impactThing) != 0) pieces = 4;
                    else pieces = 8;
                    ScatterDebris(p_at, pieces);
                    return;
                }
            }

            EffectInfo info = Info[p_type];
            Vector3 d = Host.Fx.Eyepoint - p_at.ToFloat(); // the sound's and the effect camera's (render side)
            if (info.m_needsObject || info.m_camera)
            {
                int count = 0, slot = -1;
                for (int i = 0; i < c_slotCount; i++)
                {
                    Effect other = g_effects[i];
                    if (other.m_type != p_type) continue;
                    if (other.m_active)
                    {
                        // GBL (0x52040): Titanium's test with the approximate distance.
#if UNITY_EDITOR || MEKENGINE_MERCS
                        bool crowds = Gbl || Mercs
#else
                        bool crowds = Gbl
#endif
                            ? (other.m_type < 0x17 || other.m_type > 0x21) && (info.m_flags & 2) == 0 && (other.m_position - p_at).ApproxLength < 500L << 16
                            : Titanium
                            ? (other.m_type < 0x17 || other.m_type > 0x21) && (info.m_flags & 2) == 0 && (other.m_position - p_at).Length < 500L << 16
                            : other.m_type < 0x17 && (other.m_position - p_at).ApproxLength < 500L << 16;
                        if (crowds)
                        {
                            count++;
                            // The detail toggle is each machine's own: not in lockstep (an effect damages). Pending the
                            // user's decision on lockstep's option overrides.
                            if (Host.Fx.LowDetail || count > 2) return;
                        }
                    }
                    else if (slot < 0 && (!info.m_needsObject || other.m_object != null || Host.Net.NetRules))
                    {
                        slot = i;
                    }
                }

                if (slot < 0 && Host.Net.NetRules) slot = Claim(p_type);
                if (slot < 0) return;

                Effect effect = g_effects[slot];
                effect.m_active = true;
                effect.m_position = p_at;
                effect.m_timeLeft = info.m_duration + Clock.g_deltaTime;
                effect.m_owner = p_owner;
                effect.m_follow = null;
                if (effect.m_object != null)
                {
                    if ((info.m_flags & 1) != 0)
                    {
                        if (p_hit != null && (p_hit.Kind & 0x400) == 0 && (p_hit.Kind & 0x100) != 0)
                        {
                            effect.m_follow = p_hit;
                            p_at = p_hit.FxCenter;
                            effect.m_position = p_at;
                        }
                        else effect.m_timeLeft = Clock.g_deltaTime + (Gbl ? 0xb6 : 0xb5);
                    }

                    if (p_type >= 0x17 && g_launchEffectFiringObj != null)
                    {
                        Host.Fx.PlaceAtFiringObj(g_launchEffectFiringObj, effect.m_object);
                        g_launchEffectFiringObj = null;
                    }
                    else
                    {
                        effect.m_object.SetRotation(Quaternion.Euler(p_angles));
                        effect.m_object.SetPosition(p_at.ToFloat());
                    }

                    effect.m_object.Show(true);
                    Host.Fx.BoostEffect(effect.m_object, effect.m_type);
                    if (effect.m_animation != -1)
                    {
                        Host.Fx.SetAnimFrame(effect.m_animation, 0);
                        Host.Fx.SetAnimMode(effect.m_animation, 2);
                    }
                }

                // Not the original's: the effect's light (its type's, or a shot's own impact look, g_impactFx).
                Host.Fx.LightEffect(impactFx, p_type, effect.m_object, p_at, p_camera, info.m_camera, info.m_duration, s_lightKelvin, p_hit);

                if (info.m_camera && Host.Fx.EffectCameraEnabled)
                {
                    // The effect camera: the closest such effect takes it (and with it the palette's flash).
                    if (Host.Fx.TakeEffectCamera(slot, p_camera, d))
                    {
                        effect.m_hasCamera = true;
                        int flash = LocalInfo(p_type).m_flash;
                        if (flash > -1) ScreenFlash.StartPaletteFlash(flash, info.m_duration, 1);
                    }
                }
            }

            if (chain) SpawnEffect(p_owner, chainType, p_at, p_camera);

            EffectInfo sounding = LocalInfo(p_type);
            if (sounding.m_sound > 0 && (sounding.m_soundChance == -1 || SimRandom.Cosmetic.RandomIntBelow(100) < sounding.m_soundChance))
            {
                // Half as loud in the cockpit view (GetViewMode 0), unless quiet.
                bool half = Eyepoint.g_inCockpitView && !quiet;
                EffectSound.PlaySoundAt(d, sounding.SoundId, half, g_explosionSound);
            }
        }

        // Shows up to p_count free debris slots (type 0xb; all when 0) at p_at (16.16 units), thrown.
        public static void ScatterDebris(FixVec p_at, int p_count)
        {
            int shown = 0;
            if (p_count == 0) p_count = c_slotCount;

            for (int i = 0; i < c_slotCount && shown < p_count; i++)
            {
                Effect effect = g_effects[i];
                if (!effect.m_active && effect.m_type == 0xb && effect.m_object != null)
                {
                    ISceneObject obj = effect.m_object;
                    int piece = Debris.AddDebrisPiece(obj, 1, p_at);
                    if (piece != -1)
                    {
                        effect.m_active = true;
                        effect.m_timeLeft = Info[0xb].m_duration;
                        Debris.ThrowDebrisPiece(piece);
                        obj.Show(true);
                        shown++;
                    }
                }
            }
        }

        // ---- Updating -----------------------------------------------------------------------------------------------------

        // Counts down the effects and frees them when their time runs out, giving the camera back if one had it; hides
        // the animated faces of those whose animation has played out.
        public static void UpdateEffects()
        {
            if (g_effects[0] == null) return;
            UpdateNuke();
            if (SimMain.g_difficulty.m_heatTracking) HeatMechsNearFires();

            for (int i = 0; i < c_slotCount; i++)
            {
                Effect effect = g_effects[i];
                if (!effect.m_active) continue;

                EffectInfo info = Info[effect.m_type];
                if (TiFx)
                {
                    // A sticky effect follows its mech part.
                    if ((info.m_flags & 1) != 0 && effect.m_follow != null)
                    {
                        effect.m_position = effect.m_follow.FxCenter;
                        effect.m_object?.SetPosition(effect.m_position.ToFloat());
                    }

                    // UpdateEffects (0x100128d0): the nuke's and the Arrow IV's blasts grow (28 and 24 units a tick) and
                    // damage mechs without splash damage too; the rates are the type's. The heat rate goes to mech+0xdc8,
                    // which nothing reads (as written): not ported.
                    // Mercenaries (0x1004b4ef): only Arrow IV's grows (the nuke's effect its object's radius), and both splash.
#if UNITY_EDITOR || MEKENGINE_MERCS
                    bool grows = Mercs ? effect.m_type == 0x23 : effect.m_type == 0x16 || effect.m_type == 0x23;
                    bool splashes = grows || (Mercs && effect.m_type == 0x16);
#else
                    bool grows = effect.m_type == 0x16 || effect.m_type == 0x23;
                    bool splashes = grows;
#endif
                    bool sized = DamageRadius(effect, out long shapeRadius);
                    if (info.m_damages && (grows || sized))
                    {
#if UNITY_EDITOR || MEKENGINE_MERCS
                        long radius = effect.m_type == 0x16 && !Mercs ? (long)(info.m_duration - effect.m_timeLeft) * 28 << 16
#else
                        long radius = effect.m_type == 0x16 ? (long)(info.m_duration - effect.m_timeLeft) * 28 << 16
#endif
                            : effect.m_type == 0x23 ? (long)(info.m_duration - effect.m_timeLeft) * 24 << 16
                            : shapeRadius;
                        if (SimMain.g_difficulty.m_splashDamage || splashes)
                        {
                            // GBL (0x52900): the approximate distance, a quarter in the head, and the heat rate heats the mech.
                            if (Gbl) Mech.DamageMechsInRadiusGbl(effect.m_owner, effect.m_position, radius, info.m_damageRateFx, info.m_heatRateFx);
#if UNITY_EDITOR || MEKENGINE_MERCS
                            // Mercenaries (0x1004b839): GBL's, its heat into an unread field.
                            else if (Mercs) Mech.DamageMechsInRadiusGbl(effect.m_owner, effect.m_position, radius, info.m_damageRateFx, 0);
#endif
                            else Mech.DamageMechsInRadiusTitanium(effect.m_owner, effect.m_position, radius, info.m_damageRateFx);
                        }

                        if (SimMain.g_world != null) Shots.DamageThingsInRadius(SimMain.g_world, effect.m_owner, effect.m_position, radius, info.m_damageRateFx);
                        Debris.DamageChunksInRadius(effect.m_position, radius, info.m_damageRateFx);
                    }
                }
                else if (info.m_damages && DamageRadius(effect, out long radius))
                {
                    if (SimMain.g_difficulty.m_splashDamage) Mech.DamageMechsInRadius(effect.m_owner, effect.m_position, radius, 256);
                    if (SimMain.g_world != null) Shots.DamageThingsInRadius(SimMain.g_world, effect.m_owner, effect.m_position, radius, 256);
                    Debris.DamageChunksInRadius(effect.m_position, radius, 256);
                }

                effect.m_timeLeft -= Clock.g_deltaTime;
                if (effect.m_timeLeft > 0) continue;

                if (effect.m_hasCamera)
                {
                    // The camera's effect lasts while the palette still fades.
                    if (ScreenFlash.GetPaletteFadeSteps() != 0) continue;
                    effect.m_hasCamera = false;
                    Host.Fx.ReleaseEffectCamera();
                }

                effect.m_object?.Show(false);
                if (effect.m_animation > 0)
                {
                    Host.Fx.SetAnimMode(effect.m_animation, 0);
                    Host.Fx.SetAnimFrame(effect.m_animation, 0);
                }

                ResetEffectSlot(i);
            }

            Host.Fx.ShowAnimatedFaces();
        }

        // ---- Wrecks and nukes ---------------------------------------------------------------------------------------------

        // Not the original's: a fireball's peak temperature for the effect SpawnEffectEx is lighting (0: its type's light's).
        static float s_lightKelvin;

        // A wrecked mech's smoke (fadepal.c EmitWreckSmoke): once as it falls (p_stateTime 0), then now and then a spark or
        // a puff near it. Not the original's: its reactor's fireball peaks at p_kelvin.
        // Its draws are the gameplay stream's: the sparks (type 3) damage what is near them.
        public static void EmitWreckSmoke(int p_killer, Vector3Int p_units, int p_stateTime, float p_kelvin = 0)
        {
            FixVec at = FixVec.FromUnits(p_units);
            if (p_stateTime == 0)
            {
                s_lightKelvin = p_kelvin;
                SpawnExplosion(p_killer, 0xd, at, at);
                s_lightKelvin = 0;
            }
            else if (SimRandom.RandomIntBelow(100) <= 20)
            {
                at.X += (long)SimRandom.RandomNormal() << 15; // RandomNormal() / 2
                at.Z += (long)SimRandom.RandomNormal() << 15;
                if (SimRandom.RandomIntBelow(100) < 0x3c) SpawnEffect(p_killer, 3, at, at);
                else SpawnEffect(p_killer, 0x10b, at, at);
            }
        }

        // Sets off a nuke at p_at: the palette's fade, the effect, and the blast that grows over the next ticks
        // (UpdateNuke).
        public static void StartNuke(Vector3Int p_at)
        {
            int duration = Info[0x16].m_duration * 2;
            g_nukeMaxRadius = 400000L << 16;
            // A MekNet match: Titanium's nuke shape's (DamageRadius).
            if (Host.Net.NetRules && TitaniumEffectRadius.TryGetValue(0x16, out float nukeRadius))
                g_nukeMaxRadius = Fixed.FromFloatL(nukeRadius * 100);
            else for (int i = c_slotCount - 1; i >= 0; i--)
            {
                Effect effect = g_effects[i];
                if (effect.m_type == 0x16 && effect.m_object != null)
                {
                    g_nukeMaxRadius = Fixed.FromFloatL(effect.m_object.Radius * 100);
                    break;
                }
            }

            g_nukePosition = FixVec.FromUnits(p_at);
            g_nukeTimeLeft = duration;
            g_nukeRadius = 0;
            ScreenFlash.StartPaletteFade(0x12, duration, 2);
            SpawnExplosion(-2, 0x16, g_nukePosition, g_nukePosition);
        }

        // Grows the nuke's blast, which damages the mechs, the game things and the chunks within it.
        static void UpdateNuke()
        {
            if (g_nukeTimeLeft <= 0) return;
            g_nukeRadius += (long)Clock.g_deltaTime * 200 << 16;
            if (g_nukeRadius > g_nukeMaxRadius) g_nukeRadius = g_nukeMaxRadius;
#if UNITY_EDITOR || MEKENGINE_MERCS
            if (Mercs)
            {
                // Mercenaries' UpdateNuke (0x1004c273): the classic's 1/16 (0x1000) through its splash, owner -2, no heat.
                Mech.DamageMechsInRadiusGbl(-2, g_nukePosition, g_nukeRadius, 0x1000, 0);
                if (SimMain.g_world != null) Shots.DamageThingsInRadius(SimMain.g_world, -2, g_nukePosition, g_nukeRadius, 0x1000);
                Debris.DamageChunksInRadius(g_nukePosition, g_nukeRadius, 0x1000);
            }
            else if (Titanium)
#else
            if (Titanium)
#endif
            {
                // Titanium's UpdateNuke (0x1001358f): 4096 a tick (0x45800000, in points where the classic's 1/16 was).
                Mech.DamageMechsInRadiusTitanium(-2, g_nukePosition, g_nukeRadius, 4096 << 16);
                if (SimMain.g_world != null) Shots.DamageThingsInRadius(SimMain.g_world, -2, g_nukePosition, g_nukeRadius, 4096 << 16);
                Debris.DamageChunksInRadius(g_nukePosition, g_nukeRadius, 4096L << 16);
            }
            else if (Gbl)
            {
                // GBL's UpdateNuke (MW2.flat 0x530d0): the classic's 1/16 through GBL's splash (0x52900), no heat.
                Mech.DamageMechsInRadiusGbl(-1, g_nukePosition, g_nukeRadius, 0x1000, 0);
                if (SimMain.g_world != null) Shots.DamageThingsInRadius(SimMain.g_world, -1, g_nukePosition, g_nukeRadius, 0x1000);
                Debris.DamageChunksInRadius(g_nukePosition, g_nukeRadius, 0x1000);
            }
            else
            {
                Mech.DamageMechsInRadius(-1, g_nukePosition, g_nukeRadius, 0x1000);
                if (SimMain.g_world != null) Shots.DamageThingsInRadius(SimMain.g_world, -1, g_nukePosition, g_nukeRadius, 0x1000);
                Debris.DamageChunksInRadius(g_nukePosition, g_nukeRadius, 0x1000);
            }
            g_nukeTimeLeft -= Clock.g_deltaTime;
        }

        // HeatMechsNearFires: each fire (a shape of kind 0x10) heats the mechs within twice its radius (and theirs),
        // more the nearer they stand. (The shapes' order doesn't matter: the heats only add up.)
        static void HeatMechsNearFires()
        {
            foreach (Shape shape in ShapeCollision.AllShapes)
            {
                if (shape.KindType != 0x10 || !shape.InScene) continue;
                long radius = shape.FxRadius * 2; // 16.16 units
                FixVec center = shape.FxCenter;
                foreach (Mech mech in SimMain.g_mechs)
                {
                    if (mech == null || (mech.PlayerFlags & 2) != 0 || (mech.PlayerFlags & 4) != 0) continue;
                    long reach = ((long)mech.m_radius << 16) + radius;
                    long distance = (FixVec.FromUnits(mech.ObjectPosition) - center).ApproxLength;
                    if (distance < reach)
                    {
                        int heat = (int)((radius - radius * distance / reach) / 65536) >> 3;
                        mech.m_deltaHeat += heat * Clock.g_deltaTime;
                    }
                }
            }
        }
    }
}
