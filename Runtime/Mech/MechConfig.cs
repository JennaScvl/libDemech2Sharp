namespace Demech2
{
    // One of a mech's ten weapons (WeaponSlot), as InitMechArrays empties it.
    public sealed class WeaponSlot
    {
        public const int Empty = -1, Recycling = 0, Ready = 1, Firing = 2, Held = 3;

        public int m_status = -1;     // 1 working, 0 destroyed, -1 none
        public int m_type = -1;       // an index into g_weaponDefs, negative for none
        public int m_state = Empty;
        public int m_group;           // 0 to 2
        public int m_time;            // the clock of the last volley, or ticks into the current one
        public int m_target = -1;     // the locked target's index, for guided shots
        public int m_targetKind;      // 0x200 a player, 0x400 a game thing
        public int m_ammo = -1;       // -1 unlimited
        public int m_volley;          // shots left in the current volley
        public int m_hardpoint = -1;  // an index into the mech's objects
        public int m_slotId;          // the id in MechSection::m_slots
        public int m_index;           // its bit in the network weapons message
        public int[] m_bins = new int[10];
        public int m_binCount;
        public int m_binIndex;
        public int m_bin;             // the index in MechConfig.m_ammoBins of the bin being used
        public object m_ext;          // the host's data for this slot, or null (copied by Clone when ICloneable)

        public WeaponSlot Clone()
        {
            var copy = (WeaponSlot)MemberwiseClone();
            copy.m_bins = (int[])m_bins.Clone();
            if (m_ext is System.ICloneable ext) copy.m_ext = ext.Clone();
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
    public sealed class AmmoBin
    {
        public int m_type = -1, m_shots, m_weapon = -1, m_id;
        public float m_damage, m_shotHeat, m_heat;
        public int m_damageFx; // Damage, 16.16 (the sim's)
        public object m_ext; // the host's data for this bin, or null
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
        public object m_ext; // the host's data for this configuration, or null

        // The weapon slots (the host's count; ten by default).
        public static int SlotCount => Host.Weapons.SlotCount;

        // A weapon's ammunition (its own count by default, -1 unlimited).
        public int AmmoOf(WeaponSlot p_slot) => Host.Weapons.AmmoOf(this, p_slot);

        // g_localArmorPerLevel, g_otherArmorPerLevel.
        public static int LocalArmorPerLevel = 4, OtherArmorPerLevel = 3;

        // A networked match's armor multiplier, every mech's on every machine: NetMech's (LoadDifficultyCfg's network
        // enemy skill 2: 4 for the local player, its side and the others alike).
        public const int NetArmorPerLevel = 4;

        // The armor multiplier a mech was loaded with (Load): NetArmorPerLevel in a networked match, on every machine alike.
        public static int ArmorPerLevel(bool p_local) => Host.Net.NetRules ? NetArmorPerLevel : p_local ? LocalArmorPerLevel : OtherArmorPerLevel;

        public MechConfig()
        {
            for (int i = 0; i < 8; i++) m_sections[i] = new MechSection();
            m_weapons = new WeaponSlot[SlotCount];
            for (int i = 0; i < m_weapons.Length; i++) m_weapons[i] = new WeaponSlot();
            for (int i = 0; i < 25; i++) m_ammoBins[i] = new AmmoBin();
        }
    }
}
