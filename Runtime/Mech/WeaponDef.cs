using UnityEngine;

namespace Demech2
{
    // A weapon type (demech2 MW2/include/weapondef.h; weapondata.c g_weaponDefs), the Matrox edition's values: its shot,
    // the effects it sets off, its volley and ammunition, the shot's flight, damage and heat, the lock's ranges and its
    // timing (ticks). Titanium (mercsw.dll g_weaponDefs 0x1009a038, 66 entries of 0x68) adds the flags and the flamer's
    // animation slot before the same fields, its sounds already the loaded PRJ's.
    public readonly struct WeaponDef
    {
        // Titanium's flags (+8): 1 the sound with a volley's first shot, 2 with every shot, 4 selectable, 0x10 the
        // torpedo's smoothed steering, 0x20 the flamer's animations restarted, 0x40 launched at the muzzle, 0x80 NARC.
        public const int FlagFirstSound = 1, FlagEverySound = 2, FlagSelectable = 4, FlagTorpedo = 0x10, FlagFlameAnim = 0x20,
            FlagAtMuzzle = 0x40, FlagNarc = 0x80;

        public readonly int m_shotType;      // the Shot::m_type a launch takes from the free shots
        public readonly int m_impact;        // the effect the shot sets off
        public readonly int m_launchEffect;  // m_launchEffect2: effects started at each launch
        public readonly int m_launchEffect2;
        public readonly int m_autoRepeat;    // the next volley follows at once while the trigger is held
        public readonly int m_launchesShot;
        public readonly int m_guided;        // the shots follow the locked target
        public readonly int m_volley;        // shots per volley
        public readonly int m_volleysPerBin; // -1 for unlimited ammunition
        public readonly int m_sound;         // the launch sound, or <= 0
        public readonly int m_gravity;       // scales the gravity on the shot
        public readonly int m_speed;         // units a tick
        public readonly float m_damage;
        public readonly float m_heat;        // added to the firing mech's heat per shot (percent)
        public readonly float m_shotHeat;    // added to the heat of the mech the shot hits
        // The sim's 16.16 copies of Damage, Heat and ShotHeat (truncated, as the sim always took them).
        public readonly int m_damageFx, m_heatFx, m_shotHeatFx;
        public readonly int m_shortRange;    // m_longRange: a lock needs the target between them
        public readonly int m_longRange;
        public readonly int m_recycle;       // ticks between volleys
        public readonly int m_interval;      // ticks between the shots of a volley
        public readonly int m_lifetime;      // the shot's, ticks
        public readonly string m_name;
        public readonly int m_flags;         // Titanium's (+8); 0 elsewhere
        public readonly int m_animSlot;      // Titanium's +0xc: the first of the flamer's eight animated-texture slots
        readonly bool prjSound;            // Sound is the loaded PRJ's id (Titanium's table), not a DOS one

        // The launch sound as the loaded PRJ's id.
        public int SoundId => prjSound ? m_sound : Host.Anim.MapSoundId(m_sound);

        WeaponDef(int p_shotType, int p_impact, int p_launchEffect, int p_launchEffect2, int p_autoRepeat,
            int p_launchesShot, int p_guided, int p_volley, int p_volleysPerBin, int p_sound, int p_gravity, int p_speed,
            float p_damage, float p_heat, float p_shotHeat, int p_shortRange, int p_longRange, int p_recycle,
            int p_interval, int p_lifetime, string p_name)
        {
            m_shotType = p_shotType;
            m_impact = p_impact;
            m_launchEffect = p_launchEffect;
            m_launchEffect2 = p_launchEffect2;
            m_autoRepeat = p_autoRepeat;
            m_launchesShot = p_launchesShot;
            m_guided = p_guided;
            m_volley = p_volley;
            m_volleysPerBin = p_volleysPerBin;
            m_sound = p_sound;
            m_gravity = p_gravity;
            m_speed = p_speed;
            m_damage = p_damage;
            m_heat = p_heat;
            m_shotHeat = p_shotHeat;
            m_damageFx = (int)(p_damage * 65536f);
            m_heatFx = (int)(p_heat * 65536f);
            m_shotHeatFx = (int)(p_shotHeat * 65536f);
            m_shortRange = p_shortRange;
            m_longRange = p_longRange;
            m_recycle = p_recycle;
            m_interval = p_interval;
            m_lifetime = p_lifetime;
            m_name = p_name;
            m_flags = 0;
            m_animSlot = 0;
            prjSound = false;
        }

        WeaponDef(int p_flags, int p_animSlot, WeaponDef p_def)
        {
            this = p_def;
            m_flags = p_flags;
            m_animSlot = p_animSlot;
            prjSound = true;
        }

        static WeaponDef W(int p_shotType, int p_impact, int p_launchEffect, int p_launchEffect2, int p_autoRepeat,
            int p_launchesShot, int p_guided, int p_volley, int p_volleysPerBin, int p_sound, int p_gravity, int p_speed,
            float p_damage, float p_heat, float p_shotHeat, int p_shortRange, int p_longRange, int p_recycle,
            int p_interval, int p_lifetime, string p_name) =>
            new WeaponDef(p_shotType, p_impact, p_launchEffect, p_launchEffect2, p_autoRepeat, p_launchesShot, p_guided,
                p_volley, p_volleysPerBin, p_sound, p_gravity, p_speed, p_damage, p_heat, p_shotHeat, p_shortRange,
                p_longRange, p_recycle, p_interval, p_lifetime, p_name);

        // The base game's weapons (Tables/Armament.json), read on first use.
        static WeaponDef[] s_classic;
        public static WeaponDef[] Table => s_classic ??= LoadClassic();

        static WeaponDef[] LoadClassic()
        {
            ArmamentRow[] rows = SimTables.Armament;
            var defs = new WeaponDef[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                ArmamentRow r = rows[i];
                defs[i] = W(r.projectile, r.hitEffect, r.muzzleFx, r.muzzleFxAlt, r.repeats, r.spawnsShot, r.homing, r.burst,
                    r.burstsPerMag, r.soundId, r.gravityScale, r.velocity, r.dmg, r.firingHeat, r.targetHeat, r.minLock,
                    r.maxLock, r.cooldown, r.burstGap, r.ttl, r.label);
            }
            return defs;
        }

        static WeaponDef T(int p_flags, int p_animSlot, int p_shotType, int p_impact, int p_launchEffect, int p_launchEffect2,
            int p_autoRepeat, int p_launchesShot, int p_guided, int p_volley, int p_volleysPerBin, int p_sound, int p_gravity,
            int p_speed, float p_damage, float p_heat, float p_shotHeat, int p_shortRange, int p_longRange, int p_recycle,
            int p_interval, int p_lifetime, string p_name) =>
            new WeaponDef(p_flags, p_animSlot, W(p_shotType, p_impact, p_launchEffect, p_launchEffect2, p_autoRepeat,
                p_launchesShot, p_guided, p_volley, p_volleysPerBin, p_sound, p_gravity, p_speed, p_damage, p_heat, p_shotHeat,
                p_shortRange, p_longRange, p_recycle, p_interval, p_lifetime, p_name));

        // Titanium's g_weaponDefs (mercsw.dll 0x1009a038): the Clan types (0-34), then the Inner Sphere's (35-65). Types
        // 29 and 62 (AMS) run AmsUpdate and AmsSpawn instead of SpawnShot.
        public static readonly WeaponDef[] TitaniumTable =
        {
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 20, 6, 634, 0, 100, 1, 0, 0, 7500, 100100, 2534, 11, 724, "LRM20"), // 0
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 15, 8, 634, 0, 100, 1, 0, 0, 7500, 100100, 2172, 22, 724, "LRM15"), // 1
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 10, 12, 634, 0, 100, 1, 0, 0, 7500, 100100, 1810, 45, 724, "LRM10"), // 2
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 5, 24, 634, 0, 100, 1, 0, 0, 7500, 100100, 1448, 45, 724, "LRM5"), // 3
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 6, 15, 628, 0, 70, 2, 1, 0, 0, 49686, 1448, 60, 543, "SRM6"), // 4
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 4, 25, 645, 0, 70, 2, 1, 0, 0, 49686, 1267, 45, 543, "SRM4"), // 5
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 2, 50, 644, 0, 100, 2, 2, 0, 0, 49686, 1086, 45, 543, "SRM2"), // 6
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 6, 15, 634, 1, 70, 2, 1, 0, 25, 49686, 1086, 45, 543, "SSRM6"), // 7
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 4, 25, 634, 1, 70, 2, 1, 0, 25, 49686, 1448, 45, 543, "SSRM4"), // 8
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 2, 50, 634, 1, 70, 2, 2, 0, 25, 49686, 1448, 45, 543, "SSRM2"), // 9
            T(0x5, 0, 5, 5, 23, 24, 1, 1, 0, 1, 200, 636, 1, 300, 1, 0, 0, 0, 17500, 11, 11, 57, "MGun"), // 10
            T(0x5, 0, 7, 11, 23, 24, 0, 1, 0, 1, 8, 629, 0, 150, 15, 0.5f, 0, 0, 182000, 724, 22, 646, "GAUSS"), // 11
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 45, 646, 0, 240, 2, 0.5f, 0, 0, 80000, 45, 45, 331, "LBAC2"), // 12
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 20, 646, 0, 240, 5, 0.5f, 0, 0, 70000, 45, 45, 289, "LBAC5"), // 13
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 10, 646, 0, 240, 10, 1, 0, 0, 60000, 45, 11, 247, "LBAC10"), // 14
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 5, 646, 0, 240, 20, 3, 0, 0, 45000, 45, 11, 186, "LBAC20"), // 15
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 45, 646, 0, 220, 2, 0.5f, 0, 0, 70000, 22, 11, 316, "uAC2"), // 16
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 20, 646, 0, 220, 5, 0.5f, 0, 0, 60000, 22, 11, 271, "uAC5"), // 17
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 10, 646, 0, 220, 10, 1.5f, 0, 0, 50000, 22, 11, 226, "uAC10"), // 18
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 5, 646, 0, 220, 20, 3.5f, 0, 0, 40000, 22, 11, 181, "uAC20"), // 19
            T(0x65, 118, 8, 10, 26, 27, 1, 1, 0, 10, -1, 235, 0, 20, 0, 1.5f, 3, 1000, 80000, 181, 4, 543, "FLAMER"), // 20
            T(0x5, 0, 6, 6, 30, 31, 0, 1, 0, 1, -1, 640, 0, 88, 15, 7.5f, 3.5f, 0, 120000, 452, 22, 1705, "erPPC"), // 21
            T(0x5, 0, 0, 0, 28, 29, 1, 1, 0, 1, -1, 632, 0, 280, 10, 6, 0, 0, 101920, 108, 60, 362, "erLLAS"), // 22
            T(0x5, 0, 1, 1, 28, 29, 1, 1, 0, 1, -1, 637, 0, 280, 7, 2.5f, 0, 0, 50960, 90, 60, 181, "erMLAS"), // 23
            T(0x5, 0, 2, 2, 28, 29, 1, 1, 0, 1, -1, 642, 0, 280, 5, 1, 0, 0, 25480, 72, 60, 90, "erSLAS"), // 24
            T(0x5, 0, 0, 0, 28, 29, 1, 1, 0, 2, -1, 633, 0, 280, 10, 5, 0, 0, 81536, 45, 60, 289, "LPLAS"), // 25
            T(0x5, 0, 1, 1, 28, 29, 1, 1, 0, 2, -1, 638, 0, 280, 7, 2, 0, 0, 40768, 36, 11, 144, "MPLAS"), // 26
            T(0x5, 0, 2, 2, 28, 29, 1, 1, 0, 2, -1, 643, 0, 280, 3, 1, 0, 0, 20384, 18, 11, 72, "SPLAS"), // 27
            T(0x85, 0, 12, -1, 23, 24, 0, 1, 1, 1, 6, 644, 1, 80, 0, 4, 0, 0, 200000, 2534, 45, 1448, "NARC"), // 28
            T(0xa, 0, 5, 5, 23, 24, 1, 0, 0, 24, 20, 204, 1, 600, 0, 0, 0, 0, 10000, 22, 11, 181, "AMS"), // 29
            T(0x5, 0, 22, 22, -1, -1, 1, 1, 0, 1, 1, 284, 0, 0, 1000, 0, 50, 0, 200000, 36200, 181, 5430, "NUKE"), // 30
            T(0x5, 0, 3, 10, 26, 27, 0, 1, 0, 2, 50, 644, 0, 70, 0, 1, 0, 0, 27000, 905, 60, 543, "inSRM"), // 31
            T(0x15, 0, 9, 34, 32, 33, 0, 1, 1, 2, 50, 340, 0, 10, 50, 1, 0, 0, 49686, 905, 60, 6371, "Torp"), // 32
            T(0x5, 0, 3, 10, 26, 27, 0, 1, 1, 2, 50, 644, 0, 70, 0, 1, 0, 0, 27000, 905, 60, 543, "inSSRM"), // 33
            T(0x5, 0, 10, 35, -1, -1, 0, 1, 1, 1, 5, 281, 0, 100, 7, 10, 0, 0, 200200, 3620, 60, 1784, "ArrowIV"), // 34
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 20, 6, 618, 0, 100, 1, 0, 0, 3000, 63000, 2534, 11, 724, "LRM20"), // 35
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 15, 8, 618, 0, 100, 1, 0, 0, 3000, 63000, 2172, 22, 724, "LRM15"), // 36
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 10, 12, 618, 0, 100, 1, 0, 0, 3000, 63000, 1810, 45, 724, "LRM10"), // 37
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 5, 24, 618, 0, 100, 1, 0, 0, 3000, 63000, 1448, 45, 724, "LRM5"), // 38
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 6, 15, 609, 0, 70, 2, 1, 0, 0, 27000, 1448, 60, 543, "SRM6"), // 39
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 4, 25, 608, 0, 70, 2, 1, 0, 0, 27000, 1267, 45, 543, "SRM4"), // 40
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 2, 50, 595, 0, 100, 2, 2, 0, 0, 27000, 1086, 45, 543, "SRM2"), // 41
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 2, 50, 618, 1, 70, 2, 2, 0, 25, 27000, 1448, 45, 543, "SSRM2"), // 42
            T(0x5, 0, 5, 5, 23, 24, 1, 1, 0, 1, 200, 621, 1, 300, 1, 0, 0, 0, 9000, 11, 11, 57, "MGun"), // 43
            T(0x5, 0, 7, 11, 23, 24, 0, 1, 0, 1, 8, 276, 0, 150, 15, 0.5f, 0, 0, 182000, 724, 22, 646, "GAUSS"), // 44
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 45, 622, 0, 240, 2, 0.5f, 0, 0, 72000, 45, 45, 331, "AC2"), // 45
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 20, 622, 0, 240, 5, 0.5f, 0, 0, 54000, 45, 45, 289, "AC5"), // 46
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 10, 622, 0, 240, 10, 1.5f, 0, 0, 45000, 45, 11, 247, "AC10"), // 47
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 5, 622, 0, 240, 20, 3.5f, 0, 0, 27000, 45, 11, 186, "AC20"), // 48
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 10, 622, 0, 220, 5, 0.5f, 0, 0, 60000, 22, 11, 226, "uAC5"), // 49
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 10, 622, 0, 240, 10, 1, 0, 0, 54000, 45, 11, 247, "LBAC10"), // 50
            T(0x65, 118, 8, 10, 26, 27, 1, 1, 0, 10, -1, 235, 0, 20, 0, 1.5f, 3, 0, 9000, 181, 4, 543, "FLAMER"), // 51
            T(0x5, 0, 6, 6, 30, 31, 0, 1, 0, 1, -1, 584, 0, 88, 10, 7.5f, 3.5f, 0, 69000, 452, 22, 1705, "erPPC"), // 52
            T(0x5, 0, 6, 6, 30, 31, 0, 1, 0, 1, -1, 584, 0, 88, 10, 5, 3.5f, 0, 54000, 452, 22, 1705, "PPC"), // 53
            T(0x5, 0, 0, 0, 28, 29, 1, 1, 0, 1, -1, 615, 0, 280, 8, 6, 0, 0, 57000, 108, 60, 362, "erLLAS"), // 54
            T(0x5, 0, 0, 0, 28, 29, 1, 1, 0, 1, -1, 616, 0, 280, 8, 4, 0, 0, 45000, 108, 60, 362, "LLAS"), // 55
            T(0x5, 0, 1, 1, 28, 29, 1, 1, 0, 1, -1, 181, 0, 280, 5, 1.5f, 0, 0, 27000, 90, 60, 181, "MLAS"), // 56
            T(0x5, 0, 2, 2, 28, 29, 1, 1, 0, 1, -1, 625, 0, 280, 3, 0.5f, 0, 0, 9000, 72, 60, 90, "SLAS"), // 57
            T(0x5, 0, 0, 0, 28, 29, 1, 1, 0, 2, -1, 617, 0, 280, 9, 5, 0, 0, 30000, 45, 60, 289, "LPLAS"), // 58
            T(0x5, 0, 1, 1, 28, 29, 1, 1, 0, 2, -1, 623, 0, 280, 6, 2, 0, 0, 18000, 36, 11, 144, "MPLAS"), // 59
            T(0x5, 0, 2, 2, 28, 29, 1, 1, 0, 2, -1, 626, 0, 280, 3, 1, 0, 0, 9000, 18, 11, 72, "SPLAS"), // 60
            T(0x85, 0, 12, -1, 23, 24, 0, 1, 1, 1, 6, 618, 1, 80, 0, 4, 0, 0, 27000, 2534, 45, 1448, "NARC"), // 61
            T(0xa, 0, 5, 5, 23, 24, 1, 0, 0, 24, 20, 204, 1, 600, 0, 0, 0, 0, 10000, 22, 11, 181, "AMS"), // 62
            T(0x5, 0, 3, 10, 26, 27, 0, 1, 0, 2, 50, 595, 0, 70, 0, 1, 0, 0, 49686, 905, 60, 543, "inSRM"), // 63
            T(0x5, 0, 3, 10, 26, 27, 0, 1, 1, 2, 50, 595, 0, 70, 0, 1, 0, 0, 49686, 905, 60, 543, "inSSRM"), // 64
            T(0x5, 0, 10, 35, -1, -1, 0, 1, 1, 1, 5, 281, 0, 100, 7, 10, 0, 0, 150000, 3620, 60, 1784, "ArrowIV"), // 65
        };

        // Ghost Bear's Legacy's g_weaponDefs (GBL MW2.flat 0xafed0, 35 entries of 0x68: a handler, then the flags and
        // the flamer's slot before the classic fields, the heats 16.16, the name inline): the classic Clan types with the
        // Inferno SRM-2 (31), Torpedo (32), Inferno SSRM-2 (33) and ARROW IV (34) after them. Its sounds are its PRJ's
        // ids, which number 1-365 as the classic PRJ does and go on to 497. Values as written (NARC's heat 4 / 65536).
        public static readonly WeaponDef[] GblTable =
        {
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 20, 6, 189, 0, 100, 2, 0.599991f, 0, 7500, 100100, 2548, 11, 1001, "LRM20"), // 0
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 15, 8, 189, 0, 100, 2, 0.599991f, 0, 7500, 100100, 2184, 18, 1001, "LRM15"), // 1
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 10, 12, 189, 0, 100, 2, 0.799988f, 0, 7500, 100100, 1820, 45, 1001, "LRM10"), // 2
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 5, 24, 189, 0, 100, 2, 0.799988f, 0, 7500, 100100, 1456, 45, 1001, "LRM5"), // 3
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 6, 15, 258, 0, 70, 3, 1.339996f, 0, 0, 49686, 1456, 60, 709, "SRM6"), // 4
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 4, 25, 258, 0, 70, 3, 1.5f, 0, 0, 49686, 1274, 45, 709, "SRM4"), // 5
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 2, 50, 258, 0, 100, 3, 2, 0, 0, 49686, 1092, 45, 709, "SRM2"), // 6
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 6, 15, 189, 1, 70, 3, 1.334f, 0, 25, 49686, 1092, 45, 709, "SSRM6"), // 7
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 4, 25, 189, 1, 70, 3, 1.5f, 0, 25, 49686, 1456, 45, 709, "SSRM4"), // 8
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 2, 50, 189, 1, 70, 3, 2, 0, 25, 49686, 1456, 45, 709, "SSRM2"), // 9
            T(0x5, 0, 5, 5, 23, 24, 1, 1, 0, 1, 200, 226, 1, 300, 1, 0, 0, 0, 17500, 11, 11, 58, "MGun"), // 10
            T(0x5, 0, 7, 11, 23, 24, 0, 1, 0, 1, 8, 218, 0, 280, 18, 0.5f, 0, 0, 182000, 728, 91, 649, "GAUSS"), // 11
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 45, 1, 0, 240, 4, 0.5f, 0, 0, 80000, 45, 45, 333, "xAC2"), // 12
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 20, 1, 0, 240, 10, 0.5f, 0, 0, 70000, 45, 45, 291, "xAC5"), // 13
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 10, 1, 0, 240, 20, 1, 0, 0, 60000, 45, 11, 249, "xAC10"), // 14
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 5, 1, 0, 240, 40, 3, 0, 0, 45000, 45, 11, 187, "xAC20"), // 15
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 45, 187, 0, 220, 4, 0.5f, 0, 0, 70000, 22, 11, 318, "uAC2"), // 16
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 20, 187, 0, 220, 10, 0.5f, 0, 0, 60000, 22, 11, 273, "uAC5"), // 17
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 10, 187, 0, 220, 25, 1.5f, 0, 0, 50000, 22, 11, 227, "uAC10"), // 18
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 5, 187, 0, 220, 50, 3.5f, 0, 0, 40000, 22, 11, 182, "uAC20"), // 19
            T(0x65, 118, 8, 10, 26, 27, 1, 1, 0, 10, -1, 456, 0, 20, 0, 3.5f, 5, 0, 5000, 182, 4, 364, "FLAMER"), // 20
            T(0x5, 0, 6, 6, 30, 31, 0, 1, 0, 1, -1, 227, 0, 70, 17, 12.5f, 3.5f, 0, 120000, 455, 22, 1714, "PPC"), // 21
            T(0x5, 0, 0, 0, 28, 29, 1, 1, 0, 1, -1, 199, 0, 280, 8, 6, 0, 0, 101920, 109, 60, 364, "LLASER"), // 22
            T(0x5, 0, 1, 1, 28, 29, 1, 1, 0, 1, -1, 228, 0, 280, 5, 2.5f, 0, 0, 50960, 91, 60, 182, "MLASER"), // 23
            T(0x5, 0, 2, 2, 28, 29, 1, 1, 0, 1, -1, 243, 0, 280, 3, 1, 0, 0, 25480, 72, 60, 91, "SLASER"), // 24
            T(0x5, 0, 0, 0, 28, 29, 1, 1, 0, 2, -1, 277, 0, 280, 4, 3.5f, 0, 0, 81536, 45, 60, 291, "LPLAS"), // 25
            T(0x5, 0, 1, 1, 28, 29, 1, 1, 0, 2, -1, 234, 0, 280, 2, 1.5f, 0, 0, 40768, 36, 11, 145, "MPLAS"), // 26
            T(0x5, 0, 2, 2, 28, 29, 1, 1, 0, 2, -1, 278, 0, 280, 1, 0.5f, 0, 0, 20384, 18, 11, 72, "SPLAS"), // 27
            T(0x85, 0, 12, -1, 23, 24, 0, 1, 1, 1, 6, 189, 1, 80, 0, 0.000061f, 0, 0, 200000, 2548, 45, 1456, "NARC"), // 28
            T(0xa, 0, 5, 5, 23, 24, 1, 0, 0, 24, 20, 455, 1, 600, 0, 0, 0, 0, 10000, 22, 11, 182, "AMS"), // 29
            T(0x5, 0, 4, 22, -1, -1, 1, 1, 0, 1, 1, 461, 0, 100, 1000, 0, 50, 0, 200000, 182, 182, 5460, "NUKE"), // 30
            T(0x5, 0, 3, 10, 26, 27, 0, 1, 0, 2, 50, 258, 0, 70, 0, 1.339996f, 0, 0, 49686, 910, 60, 709, "Inf SRM"), // 31
            T(0x15, 0, 9, 34, 32, 33, 0, 1, 1, 2, 50, 466, 0, 10, 50, 1.339996f, 0, 0, 49686, 910, 60, 2730, "Torpedo"), // 32
            T(0x5, 0, 3, 10, 26, 27, 0, 1, 1, 2, 50, 258, 0, 70, 0, 1.339996f, 0, 0, 49686, 910, 60, 709, "InfSSRM"), // 33
            T(0x5, 0, 10, 35, -1, -1, 0, 1, 1, 1, 5, 464, 0, 100, 7, 10, 0, 0, 200200, 3640, 60, 2002, "ArrowIV"), // 34
        };

#if UNITY_EDITOR || MEKENGINE_MERCS
        // Mercenaries' g_weaponDefs (DOS-Win95 MERCSW.DLL 0x100a7ba0, 66 entries of 0x68; both Mercenaries editions):
        // Titanium's layout with the damage an integer and the heats 16.16 (here raw / 65536, exact), its sounds its PRJ's
        // ids, the Clan names prefixed 'c'. Values as written (NARC's heat 4 / 65536, the NUKE's shot type 4).
        public static readonly WeaponDef[] MercsTable =
        {
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 20, 6, 634, 0, 100, 1, 39321 / 65536f, 0, 3000, 63000, 2534, 11, 905, "cLRM20"), // 0
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 15, 8, 634, 0, 100, 1, 39321 / 65536f, 0, 3000, 63000, 2172, 22, 905, "cLRM15"), // 1
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 10, 12, 634, 0, 100, 1, 52428 / 65536f, 0, 3000, 63000, 1810, 45, 905, "cLRM10"), // 2
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 5, 24, 634, 0, 100, 1, 52428 / 65536f, 0, 3000, 63000, 1448, 45, 905, "cLRM5"), // 3
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 6, 15, 628, 0, 70, 2, 87818 / 65536f, 0, 3000, 27000, 1448, 60, 543, "cSRM6"), // 4
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 4, 25, 645, 0, 70, 2, 98304 / 65536f, 0, 3000, 27000, 1267, 45, 543, "cSRM4"), // 5
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 2, 50, 644, 0, 100, 2, 2, 0, 3000, 27000, 1086, 45, 543, "cSRM2"), // 6
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 6, 15, 634, 1, 70, 2, 87425 / 65536f, 0, 3000, 36000, 1086, 45, 543, "cSSRM6"), // 7
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 4, 25, 634, 1, 70, 2, 98304 / 65536f, 0, 3000, 36000, 1448, 45, 543, "cSSRM4"), // 8
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 2, 50, 634, 1, 70, 2, 2, 0, 3000, 36000, 1448, 45, 543, "cSSRM2"), // 9
            T(0x5, 0, 5, 5, 23, 24, 1, 1, 0, 1, 200, 636, 1, 300, 1, 0, 0, 0, 9000, 11, 11, 57, "cMGun"), // 10
            T(0x5, 0, 7, 11, 23, 24, 0, 1, 0, 1, 8, 629, 0, 322, 15, 32768 / 65536f, 0, 0, 66000, 724, 22, 646, "cGAUSS"), // 11
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 45, 646, 0, 240, 2, 32768 / 65536f, 0, 0, 90000, 45, 45, 331, "cLBAC2"), // 12
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 20, 646, 0, 240, 5, 32768 / 65536f, 0, 0, 72000, 45, 45, 289, "cLBAC5"), // 13
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 10, 646, 0, 240, 10, 1, 0, 0, 44000, 45, 11, 247, "cLBAC10"), // 14
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 5, 646, 0, 240, 20, 3, 0, 0, 36000, 45, 11, 186, "cLBAC20"), // 15
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 45, 646, 0, 220, 2, 32768 / 65536f, 0, 0, 81000, 22, 11, 316, "cuAC2"), // 16
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 20, 646, 0, 220, 5, 32768 / 65536f, 0, 0, 63000, 22, 11, 271, "cuAC5"), // 17
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 10, 646, 0, 220, 10, 98304 / 65536f, 0, 0, 44000, 22, 11, 226, "cuAC10"), // 18
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 5, 646, 0, 220, 20, 229376 / 65536f, 0, 0, 36000, 22, 11, 181, "cuAC20"), // 19
            T(0x65, 118, 8, 10, 26, 27, 1, 1, 0, 10, -1, 235, 0, 20, 0, 98304 / 65536f, 3, 0, 9000, 181, 4, 362, "cFLAMER"), // 20
            T(0x5, 0, 6, 6, 30, 31, 0, 1, 0, 1, -1, 640, 0, 88, 15, 491520 / 65536f, 229376 / 65536f, 0, 69000, 452, 22, 1705, "cerPPC"), // 21
            T(0x5, 0, 0, 0, 28, 29, 1, 1, 0, 1, -1, 632, 0, 280, 10, 6, 0, 0, 75000, 108, 60, 362, "cerLLAS"), // 22
            T(0x5, 0, 1, 1, 28, 29, 1, 1, 0, 1, -1, 637, 0, 280, 7, 163840 / 65536f, 0, 0, 45000, 90, 60, 181, "cerMLAS"), // 23
            T(0x5, 0, 2, 2, 28, 29, 1, 1, 0, 1, -1, 642, 0, 280, 5, 1, 0, 0, 18000, 72, 60, 90, "cerSLAS"), // 24
            T(0x5, 0, 0, 0, 28, 29, 1, 1, 0, 2, -1, 633, 0, 280, 10, 5, 0, 0, 60000, 45, 60, 289, "cLPLAS"), // 25
            T(0x5, 0, 1, 1, 28, 29, 1, 1, 0, 2, -1, 638, 0, 280, 7, 2, 0, 0, 36000, 36, 11, 144, "cMPLAS"), // 26
            T(0x5, 0, 2, 2, 28, 29, 1, 1, 0, 2, -1, 643, 0, 280, 3, 1, 0, 0, 18000, 18, 11, 72, "cSPLAS"), // 27
            T(0x85, 0, 12, -1, 23, 24, 0, 1, 1, 1, 6, 644, 1, 80, 0, 4 / 65536f, 0, 0, 36000, 2534, 45, 1448, "cNARC"), // 28
            T(0xa, 0, 5, 5, 23, 24, 1, 0, 0, 24, 20, 204, 1, 600, 0, 0, 0, 0, 10000, 22, 11, 181, "cAMS"), // 29
            T(0x5, 0, 4, 22, -1, -1, 1, 1, 0, 1, 1, 284, 0, 100, 1000, 0, 50, 0, 200000, 181, 181, 5430, "cNUKE"), // 30
            T(0x5, 0, 3, 10, 26, 27, 0, 1, 0, 2, 50, 644, 0, 70, 0, 87818 / 65536f, 0, 0, 27000, 905, 60, 543, "cInfSRM"), // 31
            T(0x15, 0, 9, 34, 32, 33, 0, 1, 1, 2, 50, 340, 0, 10, 50, 87818 / 65536f, 0, 0, 49686, 905, 60, 2715, "cTorpedo"), // 32
            T(0x5, 0, 3, 10, 26, 27, 0, 1, 1, 2, 50, 644, 0, 70, 0, 87818 / 65536f, 0, 0, 27000, 905, 60, 543, "cInfSSRM"), // 33
            T(0x5, 0, 10, 35, -1, -1, 0, 1, 1, 1, 5, 281, 0, 100, 7, 10, 0, 0, 200200, 3620, 60, 1991, "cArrowIV"), // 34
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 20, 6, 618, 0, 100, 1, 39321 / 65536f, 0, 3000, 63000, 2534, 11, 905, "LRM20"), // 35
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 15, 8, 618, 0, 100, 1, 39321 / 65536f, 0, 3000, 63000, 2172, 22, 905, "LRM15"), // 36
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 10, 12, 618, 0, 100, 1, 52428 / 65536f, 0, 3000, 63000, 1810, 45, 905, "LRM10"), // 37
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 5, 24, 618, 0, 100, 1, 52428 / 65536f, 0, 3000, 63000, 1448, 45, 905, "LRM5"), // 38
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 6, 15, 609, 0, 70, 2, 87818 / 65536f, 0, 0, 27000, 1448, 60, 543, "SRM6"), // 39
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 4, 25, 608, 0, 70, 2, 98304 / 65536f, 0, 0, 27000, 1267, 45, 543, "SRM4"), // 40
            T(0x5, 0, 3, 3, 26, 27, 0, 1, 0, 2, 50, 595, 0, 100, 2, 2, 0, 0, 27000, 1086, 45, 543, "SRM2"), // 41
            T(0x5, 0, 4, 3, 26, 27, 0, 1, 1, 2, 50, 618, 1, 70, 2, 2, 0, 25, 27000, 1448, 45, 543, "SSRM2"), // 42
            T(0x5, 0, 5, 5, 23, 24, 1, 1, 0, 1, 200, 621, 1, 300, 1, 0, 0, 0, 9000, 11, 11, 57, "MGun"), // 43
            T(0x5, 0, 7, 11, 23, 24, 0, 1, 0, 1, 8, 276, 0, 322, 15, 32768 / 65536f, 0, 0, 66000, 724, 22, 646, "GAUSS"), // 44
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 45, 622, 0, 240, 2, 32768 / 65536f, 0, 0, 72000, 45, 45, 331, "AC2"), // 45
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 20, 622, 0, 240, 5, 32768 / 65536f, 0, 0, 54000, 45, 45, 289, "AC5"), // 46
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 10, 622, 0, 240, 10, 98304 / 65536f, 0, 0, 45000, 45, 11, 247, "AC10"), // 47
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 5, 622, 0, 240, 20, 229376 / 65536f, 0, 0, 27000, 45, 11, 186, "AC20"), // 48
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 10, 622, 0, 220, 5, 32768 / 65536f, 0, 0, 60000, 22, 11, 226, "uAC5"), // 49
            T(0x5, 0, 5, 11, 23, 24, 1, 1, 0, 1, 10, 622, 0, 240, 10, 1, 0, 0, 54000, 45, 11, 247, "LBAC10"), // 50
            T(0x65, 118, 8, 10, 26, 27, 1, 1, 0, 10, -1, 235, 0, 20, 0, 98304 / 65536f, 3, 0, 9000, 181, 4, 362, "FLAMER"), // 51
            T(0x5, 0, 6, 6, 30, 31, 0, 1, 0, 1, -1, 584, 0, 88, 10, 491520 / 65536f, 229376 / 65536f, 0, 69000, 452, 22, 1705, "erPPC"), // 52
            T(0x5, 0, 6, 6, 30, 31, 0, 1, 0, 1, -1, 584, 0, 88, 10, 5, 229376 / 65536f, 0, 54000, 452, 22, 1705, "PPC"), // 53
            T(0x5, 0, 0, 0, 28, 29, 1, 1, 0, 1, -1, 615, 0, 280, 8, 6, 0, 0, 57000, 108, 60, 362, "erLLAS"), // 54
            T(0x5, 0, 0, 0, 28, 29, 1, 1, 0, 1, -1, 616, 0, 280, 8, 4, 0, 0, 45000, 108, 60, 362, "LLAS"), // 55
            T(0x5, 0, 1, 1, 28, 29, 1, 1, 0, 1, -1, 181, 0, 280, 5, 98304 / 65536f, 0, 0, 27000, 90, 60, 181, "MLAS"), // 56
            T(0x5, 0, 2, 2, 28, 29, 1, 1, 0, 1, -1, 625, 0, 280, 3, 32768 / 65536f, 0, 0, 9000, 72, 60, 90, "SLAS"), // 57
            T(0x5, 0, 0, 0, 28, 29, 1, 1, 0, 2, -1, 617, 0, 280, 9, 5, 0, 0, 30000, 45, 60, 289, "LPLAS"), // 58
            T(0x5, 0, 1, 1, 28, 29, 1, 1, 0, 2, -1, 623, 0, 280, 6, 2, 0, 0, 18000, 36, 11, 144, "MPLAS"), // 59
            T(0x5, 0, 2, 2, 28, 29, 1, 1, 0, 2, -1, 626, 0, 280, 3, 1, 0, 0, 9000, 18, 11, 72, "SPLAS"), // 60
            T(0x85, 0, 12, -1, 23, 24, 0, 1, 1, 1, 6, 618, 1, 80, 0, 4 / 65536f, 0, 0, 27000, 2534, 45, 1448, "NARC"), // 61
            T(0xa, 0, 5, 5, 23, 24, 1, 0, 0, 24, 20, 204, 1, 600, 0, 0, 0, 0, 10000, 22, 11, 181, "AMS"), // 62
            T(0x5, 0, 3, 10, 26, 27, 0, 1, 0, 2, 50, 595, 0, 70, 0, 87818 / 65536f, 0, 0, 49686, 905, 60, 543, "Inf SRM"), // 63
            T(0x5, 0, 3, 10, 26, 27, 0, 1, 1, 2, 50, 595, 0, 70, 0, 87818 / 65536f, 0, 0, 49686, 905, 60, 543, "InfSSRM"), // 64
            T(0x5, 0, 10, 35, -1, -1, 0, 1, 1, 1, 5, 281, 0, 100, 7, 10, 0, 0, 150000, 3620, 60, 1991, "ArrowIV"), // 65
        };
#endif

        // The running edition's own weapon model, whatever the rules: its table holds the shot types, effects and sounds
        // its files have (assets). BattlePack is Titanium's (BATTLE.DLL).
#if UNITY_EDITOR || MEKENGINE_MERCS
        public static bool NativeMercs => Host.Variant.IsMercenaries;
        public static bool NativeTitanium => Host.Variant.IsTitanium || Host.Variant.IsBattlePack || NativeMercs;
#else
        public static bool NativeTitanium => Host.Variant.IsTitanium || Host.Variant.IsBattlePack;
#endif
        static bool NativeGbl => Host.Variant.IsGhostBearsLegacy;

#if UNITY_EDITOR || MEKENGINE_MERCS
        // Mercenaries (both editions; MechWarriorGames.Current): DOS-Win95 MERCSW.DLL's weapon model, Titanium's in shape.
        // Not in a MekNet match, which runs Titanium's.
        public static bool Mercs => NativeMercs && !Host.Net.NetRules;
#endif
        // Titanium's weapon model (pools, fourteen slots, five groups, the AMS, flag 4 selection): Titanium's own,
        // BattlePack's, and Mercenaries' (the DOS MERCSW.DLL it descends from; its differences gated on Mercs). Not the
        // original's: every MekNet match runs it (WeaponCatalog), whatever the edition.
        public static bool Titanium => Host.Net.NetRules || NativeTitanium;
        static bool Gbl => NativeGbl && !Host.Net.NetRules;
#if UNITY_EDITOR || MEKENGINE_MERCS
        public static WeaponDef[] Defs => Mercs ? MercsTable : Titanium ? TitaniumTable : Gbl ? GblTable : Table;
        static WeaponDef[] NativeDefs => NativeMercs ? MercsTable : NativeTitanium ? TitaniumTable : NativeGbl ? GblTable : Table;
#else
        public static WeaponDef[] Defs => Titanium ? TitaniumTable : Gbl ? GblTable : Table;
        static WeaponDef[] NativeDefs => NativeTitanium ? TitaniumTable : NativeGbl ? GblTable : Table;
#endif

        public static WeaponDef Of(int p_type)
        {
            WeaponDef[] defs = Defs;
            return p_type >= 0 && p_type < defs.Length ? defs[p_type] : default;
        }

        // The running edition's def of a type the rules know (its nearest, WeaponCatalog.FromInternal), for its assets:
        // sounds, the flamer's animations. Of itself outside a match.
        public static WeaponDef Local(int p_type)
        {
            if (!Host.Net.NetRules) return Of(p_type);
            WeaponDef[] defs = NativeDefs;
            int type = Host.Variant.LocalWeaponType(p_type);
            return type >= 0 && type < defs.Length ? defs[type] : default;
        }

        // Titanium's AMS types (29 Clan, 62 Inner Sphere).
        public static bool IsAms(int p_type) => p_type == 0x1d || p_type == 0x3e;

        // The select functions' test: Titanium's flag 4 (every weapon elsewhere).
        public bool Selectable => !Titanium || (m_flags & FlagSelectable) != 0;
    }
}
