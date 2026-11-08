using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // A mech's damage, heat and death (demech2 MW2/src/mechdamage.c: ApplyDamageToMech, DestroySection,
    // DestroyCriticalSlot, DestroyMech, KillMech, CalculateHeat, EjectPlayer; mechcollision.c KnockMechOver; fadepal.c
    // EmitWreckSmoke, PlayMechLanding; shots.c DamageMechsInRadius). Its parts are shapes (kind 0x100, owned by the player)
    // of collision type 6, each the section (m_partId) its record's objloc gives it; a section's damage shades its parts and
    // a destroyed one blows them off as debris (RaisePartDamageLevel, BlowOffPart, FindObjByPart: the game's objects, through
    // m_host).
    public sealed partial class Mech
    {
        // KnockMechOver: the whole model blown apart (a wreck still falling, or knocked over).
        public void KnockMechOver()
        {
            m_host.BlowOffMech();
            m_flags |= 0x100;
            EffectSound.PlaySoundAt(EyeOffset(), Host.Anim.MapSoundId(0xb5), Eyepoint.g_inCockpitView, true);
        }

        // PlayMechLanding: the thud, and the camera's shake for the local player.
        public void PlayMechLanding(int p_speed)
        {
            if (IsLocal) CameraShake.PlayPlayerHitFeedback(new Vector3(0, -p_speed / 65536f, 0), Host.Fx.RenderTicks);
            int sound = p_speed >= -0x102762 ? 0xe6 : 0xe5;
            EffectSound.PlaySoundAt(EyeOffset(), Host.Anim.MapSoundId(sound), Eyepoint.g_inCockpitView);
        }

        // EmitWreckSmoke.
        public void EmitWreckSmoke() => Effects.EmitWreckSmoke(m_killer, ObjectPosition, m_stateTime, ReactorKelvin);

        // Not the original's: its reactor fireball's peak temperature by engine rating (tons x walking MP, a top speed of
        // 16.2 kph an MP), 2500 K at 100 up to 4000 K at 400.
        public float ReactorKelvin => m_config != null ? Mathf.Lerp(2500f, 4000f, (m_config.m_tons * m_config.m_kph / 16.2f - 100f) / 300f) : 3500f;

        // ---- ApplyDamageToMech ------------------------------------------------------------------------------------

        // ApplyDamageToMech: p_damage (16.16) to section p_section (1 to 8; 0x8000 its rear armor, for the torsos; 3
        // a random one of the torsos), on behalf of player p_attacker: the armor, then the internal structure, which may
        // destroy the section or hit its critical slots; then its parts darken.
        // A networked match's damage is the host's alone, each hit reported (Host.Net.MechDamaged).
        public void ApplyDamage(int p_attacker, int p_damage, int p_section)
        {
            if (!Host.Net.DamagesHere) return;
            // A regenerated mech's spawn protection (the host's option).
            if (Host.Net.SpawnProtected(this)) return;
            if (Host.Net.BeginDamage()) Host.Net.MechDamaged(this, p_attacker, p_damage, p_section);
            try { ApplyDamageToMech(p_attacker, p_damage, p_section); }
            finally { Host.Net.EndDamage(); }
        }

        public void ApplyDamageToMech(int p_attacker, int p_damage, int p_section)
        {
            if (IsPilot && SimMain.g_difficulty.m_invulnerable) return;
            if (!SimMain.g_mechPoweredUp) return;
            if (p_damage <= 0 || m_config == null) return;

            bool rear = false;
            int side = 0;
            if ((p_section & 0x8000) != 0)
            {
                rear = true;
                p_section &= ~0x8000;
            }

            if (p_section < 1 || p_section > 8) return;
            if (p_section == 3)
            {
                switch (SimRandom.RandomIntBelow(3))
                {
                    case 0:
                        p_section = 2;
                        break;
                    case 1:
                        p_section = 3;
                        break;
                    default:
                        p_section = 4;
                        break;
                }
            }

            MechSection section = m_config.m_sections[p_section - 1];
            int levels;
            if (rear && (p_section == 2 || p_section == 4 || p_section == 3))
            {
                side = 1;
                levels = (section.m_flags & 0xf0) >> 4;
            }
            else levels = section.m_flags & 0xf;

            section.m_armor[side] -= p_damage;
            section.m_flags |= 0x8000;
            if (section.m_armor[side] <= 0)
            {
                if ((section.m_flags & 0x4000) == 0 && IsLocal && PowerState == 2) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xec), 100, 0x40, 5, 0x50);
                section.m_flags |= 0x4000;
                section.m_internal += section.m_armor[side];
                section.m_armor[side] = 0;
                if (IsLocal && (p_section == 1 || p_section == 3) && PowerState != 4 && p_damage > 0x20000)
                {
                    SimMain.g_hitFadePending = true;
                }

                if (section.m_internal <= 0)
                {
                    DestroySection(p_attacker, p_section);
                    return;
                }

                for (int i = 0; i < SimRandom.RandomIntBelow(5); i++)
                {
                    int roll = SimRandom.RandomIntBelow(100);
                    if (roll < 20 && section.m_slotCount > 0)
                    {
                        if (roll == 12)
                        {
                            DestroySection(p_attacker, p_section);
                            return;
                        }

                        DestroyCriticalSlot(p_attacker, p_section, SimRandom.RandomIntBelow(section.m_slotCount), false);
                    }
                }
            }

            int level = 0;
            if (levels != 0)
            {
                int perLevel = MechConfig.ArmorPerLevel(IsLocal);
                level = 15 - (int)((section.m_internal + (long)section.m_armor[side] / perLevel) * 3 / ((long)levels << 16));
            }

            m_host.RaisePartDamageLevel(level, p_section);
        }

        // DestroySectionSlots: every critical slot of the section destroyed, quietly.
        public void DestroySectionSlots(int p_attacker, int p_section)
        {
            int count = m_config.m_sections[p_section - 1].m_slotCount;
            for (int i = 0; i < count; i++) DestroyCriticalSlot(p_attacker, p_section, 0, true);
        }

        // DestroySection: section p_section and what it takes with it: the center torso every other but the legs; a side
        // torso its arm (and stays itself); the first leg lost only lames the mech, the second takes the head and the
        // center torso. The head or the center torso destroys the mech.
        public void DestroySection(int p_attacker, int p_section)
        {
            if (p_section == 0) return;
            MechSection section = m_config.m_sections[p_section - 1];
            if ((section.m_flags & 0x2000) != 0) return;
            section.m_internal = 0;
            section.m_flags |= 0x2000;
            DestroySectionSlots(p_attacker, p_section);
            switch (p_section)
            {
                case 2:
                    DestroySection(p_attacker, 5);
                    section.m_flags &= ~0x2000;
                    return;
                case 4:
                    DestroySection(p_attacker, 6);
                    section.m_flags &= ~0x2000;
                    return;
                case 7:
                case 8:
                    if ((m_flags & 0x20) == 0)
                    {
                        m_host.BlowOffPart(p_section);
                        m_mobility = 0;
                        m_flags |= 0x20;
                        return;
                    }

                    DestroySection(p_attacker, 1);
                    DestroySection(p_attacker, 3);
                    break;
                case 3:
                    DestroySection(p_attacker, 5);
                    DestroySection(p_attacker, 2);
                    DestroySection(p_attacker, 4);
                    DestroySection(p_attacker, 6);
                    DestroySection(p_attacker, 1);
                    break;
                case 5:
                case 6:
                    m_host.BlowOffPart(p_section);
                    return;
            }

            m_host.BlowOffPart(p_section);
            if (PowerState != 4 && PowerState != 5) KillMech(p_attacker);
        }

        // DestroyCriticalSlot: slot p_slot of section p_section, and drops it from the section: a weapon (ids below
        // 5000) stops working; an ammunition bin (above 10000) explodes with its shots, damaging the section; equipment
        // loses its function (jump jets, heat sinks, life support, engine, gyro, sensors, legs, hips, arms). The local
        // player hears which, unless p_recursing; 8000 and 9000 hit another, random slot instead.
        public void DestroyCriticalSlot(int p_attacker, int p_section, int p_slot, bool p_recursing)
        {
            if (Host.Mechs.DestroyCriticalSlot(this, p_attacker, p_section, p_slot, p_recursing)) return;

            MechSection section = m_config.m_sections[p_section - 1];
            if (p_slot >= section.m_slotCount || p_slot < 0) return;
            int id = section.m_slots[p_slot];
            int kind = id / 100;
            if (kind < 50)
            {
                foreach (WeaponSlot weapon in m_config.m_weapons)
                {
                    if (weapon.m_type < 0 || weapon.m_slotId != id) continue;
                    if (!p_recursing && IsLocal && weapon.m_status != 0)
                    {
                        int part = CriticalWeaponPart(kind);
                        if (part >= 0) SayCriticalHit(part);
                        if (PowerState == 2) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xd0), 100, 0x40, 5, 0x50);
                        SayCriticalHit(0);
                    }

                    weapon.m_status = 0;
                    weapon.m_state = WeaponSlot.Empty;
                    break;
                }
            }
            else if (kind > 100)
            {
                for (int i = 0; i < m_config.m_ammoBinCount && i < m_config.m_ammoBins.Length; i++)
                {
                    AmmoBin bin = m_config.m_ammoBins[i];
                    if (bin.m_id != (short)id) continue;
                    WeaponSlot weapon = bin.m_weapon >= 0 && bin.m_weapon < Host.Mechs.BinWeaponSlots ? m_config.m_weapons[bin.m_weapon] : null;
                    if (weapon == null || weapon.m_ammo < 1) break;

                    weapon.m_ammo -= bin.m_shots;
                    if (weapon.m_ammo == 0)
                    {
                        weapon.m_status = 0;
                        weapon.m_state = WeaponSlot.Empty;
                    }

                    if (!p_recursing && bin.m_shots != 0)
                    {
                        if (IsLocal)
                        {
                            if (PowerState == 2) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xd0), 100, 0x40, 5, 0x50);
                            SimMain.g_hitFadePending = true;
                            EffectSound.PlaySoundAt(Host.Audio.EyeUnits - ObjectPosition, Host.Anim.MapSoundId(0xbc), Eyepoint.g_inCockpitView);
                            SayCriticalHit(1);
                        }

                        Transform obj = m_host.FindObjByPart(p_section) ?? m_host.FindObjByPart(3);
                        Shape shape = m_host.PartOf(obj);
                        if (shape != null) Effects.SpawnExplosion(p_attacker, 7, shape.FxCenter, shape.FxCenter);

                        int damage = bin.m_shots * bin.m_damageFx;
                        section.m_internal -= damage;
                        if (section.m_internal < 0)
                        {
                            section.m_internal = 0;
                            section.m_slots[p_slot] = 0;
                            DestroySection(p_attacker, p_section);
                        }

                        if (AutoEjects)
                        {
                            PowerState = 5;
                            KillMech(p_attacker);
                        }
                    }

                    bin.m_shots = 0;
                }
            }
            else
            {
                switch (id / 10 * 10)
                {
                    case 9000:
                    case 8000:
                        if (!p_recursing)
                        {
                            DestroyCriticalSlot(p_attacker, p_section, SimRandom.RandomIntBelow(section.m_slotCount), true);
                            return;
                        }

                        break;
                    case 7000:
                        if (!p_recursing && IsLocal && m_jumpFuel != 2) CriticalSound(4);
                        if (m_jumpJets > 0) m_jumpThrust -= m_jumpThrust / m_jumpJets;
                        else m_jumpThrust = 0;
                        if (m_jumpJets > 0) m_jumpJets--;
                        if (m_jumpJets == 0) m_jumpFuel = -2;
                        break;
                    case 6000:
                        if (!p_recursing && IsLocal && m_cooling != 0) CriticalSound(5);
                        if (m_cooling > 0) m_cooling -= 50;
                        else m_cooling = 0;
                        break;
                    case 5900:
                        if (!p_recursing && IsLocal) CriticalSound(6);
                        if (Host.World.HostileAtmosphere) KillMech(p_attacker);
                        break;
                    case 5850:
                        if (!p_recursing && IsLocal) CriticalSound(7);
                        m_mobility -= 0x199a;
                        if (m_mobility < 0) m_mobility = 0;
                        break;
                    case 5800:
                        if (!p_recursing && IsLocal) CriticalSound(8);
                        m_mobility -= 0x199a;
                        if (m_mobility < 0) m_mobility = 0;
                        m_jumpFuel = -2;
                        m_jumpThrust = 0;
                        m_jumpJets = 0;
                        break;
                    case 5750:
                        KillMech(p_attacker);
                        break;
                    case 5700:
                        if (!p_recursing && IsLocal) CriticalSound(9);
                        DamageCockpitPanels(true);
                        Host.Mechs.SensorsHit(this);
                        break;
                    case 5550:
                    case 5600:
                    case 5650:
                        if (!p_recursing && IsLocal) CriticalSound(10);
                        m_mobility -= 0x199a;
                        if (m_mobility < 0) m_mobility = 0;
                        break;
                    case 5500:
                        if (!p_recursing && IsLocal) CriticalSound(11);
                        m_mobility -= 0x199a;
                        if (m_mobility < 0) m_mobility = 0;
                        break;
                    case 5350:
                    case 5400:
                    case 5450:
                        if (!p_recursing && IsLocal) CriticalSound(12);
                        DamageCockpitPanels(false);
                        break;
                    case 5300:
                        if (!p_recursing && IsLocal) CriticalSound(9);
                        DamageCockpitPanels(false);
                        break;
                }

                if (m_mobility != 0 && m_mobility < 0x6666) m_mobility = 0x6666;
            }

            int last;
            for (last = p_slot; last < section.m_slotCount - 1; last++) section.m_slots[last] = section.m_slots[last + 1];
            if (last >= 0 && last < 12) section.m_slots[last] = 0;
            section.m_slotCount--;
        }

        // The "Critical hit : " line of a weapon slot's kind (its id / 100).
        public static int CriticalWeaponPart(int p_kind)
        {
            if (Host.Mechs.CriticalWeaponPart(p_kind, out int part)) return part;
            switch (p_kind)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                    return 0x16;
                case 4:
                case 5:
                case 6:
                    return 0x17;
                case 7:
                case 8:
                case 9:
                    return 0x18;
                case 10:
                    return 0x19;
                case 11:
                    return 0x1a;
                case 12:
                case 13:
                case 14:
                case 15:
                    return 0x1b;
                case 16:
                case 17:
                case 18:
                case 19:
                    return 0x1c;
                case 21:
                    return 0x1d;
                case 22:
                    return 0x1e;
                case 23:
                    return 0x1f;
                case 24:
                    return 0x20;
                case 25:
                    return 0x21;
                case 26:
                    return 0x22;
                case 27:
                    return 0x23;
            }

            return -1;
        }

        // The alarm of a running mech, and the line naming the part hit.
        public void CriticalSound(int p_part)
        {
            if (PowerState == 2) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xd0), 100, 0x40, 5, 0x50);
            SayCriticalHit(p_part);
        }

        // The SNDS ids of the part a "Critical hit : " names (Tables/CritCallouts.json), read on first use; in a nested
        // class so the state walk (SimState, which walks Mech's own statics) never sees it.
        public static class CritCallouts
        {
            internal static readonly int[] Ids = SimTables.CritCallouts;
        }

        // SayCriticalHit: "Critical hit : " (g_cockpitSpeech[11]) and the part.
        public static void SayCriticalHit(int p_part)
        {
            if (p_part < 0 || p_part >= CritCallouts.Ids.Length) return;
            MissionSpeech.QueueSpeech(0x36, CritCallouts.Ids[p_part], -1, true);
        }

        // DamageCockpitPanels: the local player's cockpit panels damaged (heavily with p_heavy).
        public void DamageCockpitPanels(bool p_heavy)
        {
            if (IsLocal) Cockpit.Current?.DamageCockpitPanels(p_heavy);
        }

        // ---- Death --------------------------------------------------------------------------------------------------

        // DestroyMech: an intact section's ammunition bin with shots left blows up first (DestroyCriticalSlot) and that's
        // all; else the mech is destroyed. Nothing happens before any mech first ran.
        public void DestroyMech(int p_killer)
        {
            if (!SimMain.g_mechPoweredUp || m_config == null) return;
            if (Host.Mechs.DestroyMech(this, p_killer)) return;
            for (int i = 0; i < 8; i++)
            {
                MechSection section = m_config.m_sections[i];
                if ((section.m_flags & 0x2000) != 0) continue;
                for (int j = 0; j < section.m_slotCount; j++)
                {
                    if (section.m_slots[j] <= 10000) continue;
                    for (int k = 0; k < m_config.m_ammoBinCount && k < m_config.m_ammoBins.Length; k++)
                    {
                        AmmoBin bin = m_config.m_ammoBins[k];
                        if (section.m_slots[j] != bin.m_id) continue;
                        WeaponSlot weapon = bin.m_weapon >= 0 && bin.m_weapon < 10 ? m_config.m_weapons[bin.m_weapon] : null;
                        if (weapon != null && weapon.m_ammo > 0)
                        {
                            DestroyCriticalSlot(p_killer, i + 1, j, false);
                            return;
                        }
                    }
                }
            }

            if (IsLocal) MissionSpeech.PlayCockpitSound(0, -1);
            KillMech(p_killer);
        }

        // KillMech: the mech destroyed on behalf of player p_killer (an ejecting one ejects): the kill counted (by side,
        // mech or vehicle), the enemy's kill announced, the player out (flags 6) and the wreck left (state 4, its parts
        // without a section hidden: LoadClassLevel's level 1 of a dead player); its AI reset (dead), and a dead leader's
        // star gets a new one, its members' goals that were the leader turned on the new one.
        public void KillMech(int p_killer)
        {
            if (IsPilot && SimMain.g_difficulty.m_invulnerable) return;
            if ((PlayerFlags & 2) != 0 || (PlayerFlags & 4) != 0) return;
            if (SimMain.g_aimedShape != null && (SimMain.g_aimedShape.Kind & 0x100) != 0 && SimMain.g_aimedShape.Owner == m_player.m_index)
            {
                SimMain.g_aimedShape = null;
            }

            SimMain.ForgetAimed(0x100, m_player.m_index);

            m_killer = p_killer;
            if (m_killer >= 0 && m_killer < 8 && m_player.m_index < 8) Host.World.CountKill(m_killer, m_player.m_index);
            Objectives.ChooseNetworkWinner(SimMain.g_world);

            if (PowerState == 5)
            {
                if (Host.World.HostileAtmosphere)
                {
                    if (IsLocal)
                    {
                        Host.World.SetOutcome(4);
                        MissionSpeech.PlayCockpitSound(0x20, -1);
                    }

                    PowerState = 4;
                }
                else if (IsLocal)
                {
                    Host.World.SetOutcome(2);
                    MissionSpeech.PlayCockpitSound(0xc, -1);
                }
            }

            int side = SimMain.PlayerSide(m_player.m_index);
            bool isMech = m_type == 1;

            Host.Mechs.Killing(this, p_killer, side, isMech);
            if (p_killer == SimMain.g_localPlayerId)
            {
                if (isMech)
                {
                    Host.World.KillCount += IsLocal ? -1 : 1;
                    Host.World.CountKillBySide(true, true, side);
                }
                else Host.World.CountKillBySide(true, false, side);
            }

            if ((PlayerFlags & 0x1400) != 0)
            {
                Host.World.CountKillBySide(false, isMech, side);
            }

            Player local = SimMain.PlayerOf(SimMain.g_localPlayerId);
            if (local != null && local.m_team == m_player.m_team)
            {
                Host.World.CountWingmanLost(PowerState == 5);
            }

            if (side == 1)
            {
                switch (m_type)
                {
                    case 1:
                        MissionSpeech.PlayCockpitSound(0x15, -1);
                        break;
                    case 2:
                    case 4:
                        MissionSpeech.PlayCockpitSound(0x16, -1);
                        break;
                    case 3:
                        MissionSpeech.PlayCockpitSound(0x19, -1);
                        break;
                    case 5:
                    case 8:
                        MissionSpeech.PlayCockpitSound(0x17, -1);
                        break;
                }
            }

            PlayerFlags |= 6;
            m_stateTime = 0;
            if (PowerState != 5 || !IsPilot) PowerState = 4;
            ResetAI();
            if (Teams.GetTeamLeader(SimMain.g_world, m_player.m_team) == m_player.m_index)
            {
                int leader = StarOrders.ChooseTeamLeader(m_player.m_team);
                StarOrders.RetargetGoals(m_player.m_team, m_player.m_index, leader);
            }

            if (!IsLocal && Teams.GetTeamLeader(SimMain.g_world, m_player.m_team) == SimMain.g_localPlayerId) StarOrders.SayLancemateReport(6, m_player.m_slot);

            // LoadClassLevel(index, 1): a dead player's shapes without a section are hidden.
            m_host.HideSectionlessParts();
        }

        // EjectPlayer: unless already destroyed or ejecting, the local player (p_eject) ejects with its sound, or hears
        // the ejection is aborted in a hostile atmosphere; the mech is destroyed.
        public void EjectPlayer(bool p_eject)
        {
            if (PowerState == 4 || PowerState == 5) return;
            if (p_eject && IsPilot)
            {
                if (!Host.World.HostileAtmosphere)
                {
                    PowerState = 5;
                    if (IsLocal) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xc5), 100, 0x40, 5, 0x32);
                }
                else if (IsLocal) MissionSpeech.PlayCockpitSound(0x20, -1);
            }

            KillMech(m_player.m_index);
        }

        // ---- Heat ---------------------------------------------------------------------------------------------------

        // CalculateHeat: the heat (16.16 percent) gathers this tick's rise less the cooling (doubled while shut down).
        // Overheating (above 80) shuts the mech down after 6 seconds unless overridden; above 100 an overridden mech's
        // ammunition may explode, any other is destroyed after 25 seconds; the local player hears the warnings. Below 65
        // the shutdown ends after 4 seconds. Without heat tracking the local mech stays cool.
        public void CalculateHeat()
        {
            if ((PlayerFlags & 2) != 0 || (PlayerFlags & 4) != 0) return;
            if (!SimMain.g_difficulty.m_heatTracking && IsPilot)
            {
                m_heat = 0;
                m_deltaHeat = 0;
                return;
            }

            int shift = PowerState == 3 ? 1 : 0;
            int cooling = (m_cooling * Clock.g_deltaTime) << shift;
            int delta = m_deltaHeat - cooling;
            m_heat += delta;
            if (m_heat < 0) m_heat = 0;
            int heat = m_heat >> 16;

            if ((m_flags & 4) != 0 && (m_flags & 8) == 0 && PowerState != 3 && Clock.g_currentClock - m_stateTime > 1086)
            {
                PowerState = 3;
                m_stateTime = Clock.g_currentClock;
                if (IsLocal) MissionSpeech.PlayCockpitSound(13, -1);
            }

            if (heat > 100)
            {
                if (IsPilot && SimMain.g_difficulty.m_invulnerable) return;
                if ((m_flags & 8) != 0)
                {
                    // The original rolls RandomIntBelow(4000 / g_deltaTime) < 3 a frame (~3 * dt / 4000 a tick). Not in the
                    // original: at a 6-tick step 4000 / 6 = 666 and no entry of the 127-entry table (seed 0) passes, so the
                    // step rolls against a 3x divisor (222, two entries pass) and then a 1-in-3, keeping ~3 * dt / 4000.
                    if (SimRandom.RandomIntBelow(4000 / (Clock.g_deltaTime * 3)) < 3 && SimRandom.RandomIntBelow(3) == 0)
                        DestroyMech(m_player.m_index);
                }
                else if (Clock.g_currentClock - m_stateTime > Host.Mechs.OverheatKillTicks) KillMech(m_player.m_index);
            }
            else if (heat > 80)
            {
                if ((m_flags & 4) == 0)
                {
                    m_stateTime = Clock.g_currentClock;
                    m_flags |= 4;
                    if ((m_flags & 0x1000) == 0 && IsLocal)
                    {
                        m_flags |= 0x1000;
                        MissionSpeech.PlayCockpitSound(3, -1);
                        CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xe9), 100, 0x40, 5, 0x50);
                    }
                }
            }
            else if (heat > 65)
            {
                if (IsLocal && !SimMain.g_criticalHeatWarned && delta > 0)
                {
                    SimMain.g_criticalHeatWarningTime = Clock.g_currentClock;
                    SimMain.g_criticalHeatWarned = true;
                    MissionSpeech.PlayCockpitSound(0, -1);
                }
            }
            else if ((m_flags & 4) != 0)
            {
                if (PowerState == 3 && Clock.g_currentClock - m_stateTime > 724)
                {
                    m_flags &= ~4;
                    PowerState = 0;
                }

                if ((m_flags & 8) != 0)
                {
                    m_flags &= ~8;
                    m_flags &= ~4;
                }
            }
            else
            {
                if (SimMain.g_criticalHeatWarned && Clock.g_currentClock - SimMain.g_criticalHeatWarningTime > 724 && IsLocal &&
                    Host.Mechs.HeatWarningRearms(heat))
                {
                    SimMain.g_criticalHeatWarned = false;
                }

                m_flags &= ~0x1000;
            }
        }

        // ---- Splash damage ----------------------------------------------------------------------------------------

        // DamageMechsInRadius: every mech within p_radius (plus its own radius) of p_at takes p_rate per tick (16.16) in
        // each section, half in the head, twice as much once destroyed. 16.16 units.
        public static void DamageMechsInRadius(int p_owner, FixVec p_at, long p_radius, int p_rate)
        {
            for (int i = SimMain.g_mechs.Count; i-- > 0;)
            {
                Mech mech = SimMain.MechOf(i);
                if (mech == null || (mech.m_flags & 0x100) != 0) continue;
                FixVec d = FixVec.FromUnits(mech.ObjectPosition) - p_at;
                long reach = ((long)mech.m_radius << 16) + p_radius;
                if (d.ApproxLength < reach)
                {
                    long damage = (long)p_rate * Clock.g_deltaTime;
                    if (mech.PowerState == 4) damage *= 2;
                    int fixedDamage = FloatToInt(damage);
                    mech.ApplyDamage(p_owner, fixedDamage >> 2, 1); // 1.1 stats: a quarter
                    for (int section = 2; section <= 8; section++) mech.ApplyDamage(p_owner, fixedDamage, section);
                }
            }
        }

        // The floats' (int)(damage * 65536f): past an int's range the x64 conversion's 0x80000000.
        public static int FloatToInt(long p_value) => p_value > int.MaxValue || p_value < int.MinValue ? int.MinValue : (int)p_value;
    }
}
