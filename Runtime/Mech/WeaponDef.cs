using UnityEngine;

namespace Demech2
{
    // A weapon type (demech2 MW2/include/weapondef.h; weapondata.c g_weaponDefs): its shot, the effects it sets off,
    // its volley and ammunition, the shot's flight, damage and heat, the lock's ranges and its timing (ticks). The
    // table in use is the host's (Host.Weapons.Defs; the base game's by default).
    public readonly struct WeaponDef
    {
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
        public readonly object m_ext;        // the host's data for this type, or null
        readonly bool prjSound;            // Sound is the loaded PRJ's id, not a DOS one

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
            m_ext = null;
            prjSound = false;
        }

        // A copy of p_def with the host's data, its sound the loaded PRJ's id when p_soundIsPrjId.
        public WeaponDef(WeaponDef p_def, bool p_soundIsPrjId, object p_ext)
        {
            this = p_def;
            m_ext = p_ext;
            prjSound = p_soundIsPrjId;
        }

        public static WeaponDef Create(int p_shotType, int p_impact, int p_launchEffect, int p_launchEffect2, int p_autoRepeat,
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
                defs[i] = Create(r.projectile, r.hitEffect, r.muzzleFx, r.muzzleFxAlt, r.repeats, r.spawnsShot, r.homing, r.burst,
                    r.burstsPerMag, r.soundId, r.gravityScale, r.velocity, r.dmg, r.firingHeat, r.targetHeat, r.minLock,
                    r.maxLock, r.cooldown, r.burstGap, r.ttl, r.label);
            }
            return defs;
        }

        // The weapon table in use.
        public static WeaponDef[] Defs => Host.Weapons.Defs;

        public static WeaponDef Of(int p_type)
        {
            WeaponDef[] defs = Defs;
            return p_type >= 0 && p_type < defs.Length ? defs[p_type] : default;
        }

        // The def of p_type whose assets (sounds, animations) the running game shows: Of itself by default.
        public static WeaponDef Local(int p_type) => Host.Weapons.Local(p_type);

        // The select functions' test (every weapon by default).
        public bool Selectable => Host.Weapons.Selectable(this);
    }
}

