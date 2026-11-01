namespace Demech2
{
    // One of a mech's ten weapons (WeaponSlot), as InitMechArrays empties it; Titanium's fourteen (0x40 bytes) feed from
    // the mech's ammunition pools instead of bins of their own.
    public sealed class WeaponSlot
    {
        public const int Empty = -1, Recycling = 0, Ready = 1, Firing = 2, Held = 3, Off = 4; // Off: Titanium's AMS switched off
        public const int NoPool = 9999;

        public int m_status = -1;     // 1 working, 0 destroyed, -1 none
        public int m_type = -1;       // an index into g_weaponDefs, negative for none
        public int m_state = Empty;
        public int m_group;           // 0 to 2
        public int m_time;            // the clock of the last volley, or ticks into the current one
        public int m_target = -1;     // the locked target's index, for guided shots
        public int m_targetKind;      // 0x200 a player, 0x400 a game thing
        public int m_catchUp;         // not the original's: a reported volley's ticks begun elsewhere (MekNet), still to run
        public int m_ammo = -1;       // -1 unlimited
        public int m_volley;          // shots left in the current volley
        public int m_hardpoint = -1;  // an index into the mech's objects
        public int m_slotId;          // the id in MechSection::m_slots
        public int m_index;           // its bit in the network weapons message
        public int[] m_bins = new int[10];
        public int m_binCount;
        public int m_binIndex;
        public int m_bin;             // the index in MechConfig.m_ammoBins of the bin being used
        public int m_pool = NoPool;   // Titanium's +0x20: the index in MechConfig.m_pools of its class's pool, or NoPool
        public bool m_destroyed;      // Titanium's +0x38: lost to a critical hit

        public WeaponSlot Clone()
        {
            var copy = (WeaponSlot)MemberwiseClone();
            copy.m_bins = (int[])m_bins.Clone();
            return copy;
        }
    }

    // One of a mech's eight sections (MechSection): 16.16 armor and internal structure, and its critical slots (weapon
    // ids below 5000, equipment 5000 to 9999, ammunition bin ids above 10000). Sections 1 to 8 are the head, the right,
    // center and left torso, the right and left arm, the right and left leg.
    public sealed class MechSection
    {
        public int[] m_armor = new int[2]; // front, rear
        public int m_internal;
        public readonly int[] m_slots = new int[12]; // (unsigned 16-bit)
        public int m_slotCount;
        public int m_flags;                 // 0x2000 destroyed, 0x4000 armor breached, 0x8000 hit; low nibbles: damage level scales
    }

    // An ammunition bin (AmmoBin): the weapon it feeds, its critical slot id, and the damage and heat of its shots.
    // Titanium's (0x20 bytes) belong to a pool (Pool) rather than a weapon.
    public sealed class AmmoBin
    {
        public int m_type = -1, m_shots, m_weapon = -1, m_id;
        public float m_damage, m_shotHeat, m_heat;
        public int m_damageFx; // Damage, 16.16 (the sim's)
        public int m_pool = -1;
    }

    // Titanium's ammunition pool (0xda bytes at mech+0x1a4): every bin of one class (weapon type), shared by all the
    // weapons of that class.
    public sealed class AmmoPool
    {
        public int m_class;                         // +0: the weapon type
        public int m_shots;                         // +2: the bins' shots together
        public readonly int[] m_items = new int[25]; // +0xe: the bins, as indices in MechConfig.m_ammoBins
        public int m_itemCount;                     // +6
        public int m_itemIndex;                     // +0xa: the bin in use, as an index in m_items
        public int m_current = -1;                  // +0xd6: that bin, as an index in MechConfig.m_ammoBins
    }

    // The difficulty settings (DifficultyCfg, mw2dif.cfg read as one block; zeroed without the file).
    public sealed class DifficultyCfg
    {
        public bool m_unlimitedAmmo, m_invulnerable, m_splashDamage, m_collisionDamage, m_heatTracking;
        public int m_enemySkill;
        public bool m_radar;
    }

    // A mech's configuration (demech2 MW2/src/mekfile.c: LoadMechConfig): mek\<config>.mek, or the gpspec's MEK resource
    // when the file is missing. The sections (their armor scaled by the difficulty's armor per level), the weapons and
    // their ammunition bins, the heat sinks' cooling and the jump jets.
    public sealed class MechConfig
    {
        public int m_tons, m_topSpeed, m_kph, m_cooling, m_jumpFuel, m_jumpJets, m_jumpThrust, m_flags, m_weaponCount, m_ammoBinCount;
        public int m_value; // GetMechValue: the AI's m_value
        public readonly MechSection[] m_sections = new MechSection[8];
        public readonly WeaponSlot[] m_weapons;
        public readonly AmmoBin[] m_ammoBins = new AmmoBin[25];
        // Titanium: the ammunition pools (mech+0x1a0 their count) and the .mek trailer's heat sink type (mech+0xeab: 2
        // doubles).
        public readonly AmmoPool[] m_pools = new AmmoPool[25];
        public int m_poolCount;
        public int m_heatSinkType;
        // Not in the original: the SLDF Field Damper taken out (MechPilot.PpcHit), DamperRemovedFlag in the .mek's head
        // location word (+0x3e; the classic's never read, Titanium's 0x100, Mercenaries' damage scale and 0x800).
        public bool m_damperRemoved;
        public const ushort DamperRemovedFlag = 0x4000;
#if UNITY_EDITOR || MEKENGINE_MERCS
        // Mercenaries' LoadMechConfig: the config's name when read from its file (+0x470; mech +0x442 then 2), the .mek
        // trailer's name (+0x489) and five dwords after it (+0x4a7), and the header's first six dwords (+0x4bb).
        public string m_configName;
        public bool m_fromFile;
        public string m_trailerName;
        public readonly int[] m_trailerValues = new int[5];
        public readonly int[] m_headerValues = new int[6];

        // Mercenaries' g_isNetworkGame && DAT_100b4838 != -1 (a network respawn's reload): the fire groups aren't set.
        public static bool NetRespawnKeepsGroups;
#endif

        // The weapon slots: Titanium's and Mercenaries' fourteen (a MekNet match's too), ten elsewhere.
        // GBL (MW2.flat LoadMechConfig 0x4d670) has fourteen too, each with its own ammunition (no pools).
        public static int SlotCount => WeaponDef.Titanium || Host.Variant.IsGhostBearsLegacy ? 14 : 10;

        // A weapon's ammunition: Titanium's and Mercenaries' pool's shots (-1 without a pool), else its own count (-1
        // unlimited).
        public int AmmoOf(WeaponSlot p_slot)
        {
            if (!WeaponDef.Titanium) return p_slot.m_ammo;
            return p_slot.m_pool >= 0 && p_slot.m_pool < m_poolCount ? m_pools[p_slot.m_pool].m_shots : -1;
        }

        // Titanium's FUN_10006032 (the AI's and IsOutOfAmmo's test): a pool with shots, or a type 0x14-0x1b or 0x33-0x3c
        // (as written: not the energy weapons exactly).
        public bool TitaniumHasAmmo(WeaponSlot p_slot) =>
            (p_slot.m_pool >= 0 && p_slot.m_pool < m_poolCount && m_pools[p_slot.m_pool].m_shots != 0) ||
            (p_slot.m_type > 0x13 && p_slot.m_type < 0x1c) || (p_slot.m_type > 0x32 && p_slot.m_type < 0x3d);

        // g_localArmorPerLevel, g_otherArmorPerLevel.
        public static int LocalArmorPerLevel = 4, OtherArmorPerLevel = 3;

        // A MekNet match's armor multiplier, every mech's on every machine: NetMech's (LoadDifficultyCfg's network enemy
        // skill 2: 4 for the local player, its side and the others alike).
        public const int NetArmorPerLevel = 4;

        // The armor multiplier a mech was loaded with (Load): NetArmorPerLevel in a MekNet match, on every machine alike.
        public static int ArmorPerLevel(bool p_local) => Host.Net.NetRules ? NetArmorPerLevel : p_local ? LocalArmorPerLevel : OtherArmorPerLevel;

        public MechConfig()
        {
            for (int i = 0; i < 8; i++) m_sections[i] = new MechSection();
            m_weapons = new WeaponSlot[SlotCount];
            for (int i = 0; i < m_weapons.Length; i++) m_weapons[i] = new WeaponSlot();
            for (int i = 0; i < 25; i++) m_ammoBins[i] = new AmmoBin();
            for (int i = 0; i < 25; i++) m_pools[i] = new AmmoPool();
        }
    }
}
