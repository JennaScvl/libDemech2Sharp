using System;
using UnityEngine;

namespace Demech2
{
    // A mech's weapons (demech2 MW2/src/weapons.c): selecting them and their groups, firing volleys from their
    // hardpoints, the ammunition, the target lock of guided weapons, and the aim ray the weapons converge along; a volley
    // may make its target dodge (DodgeShot, Maneuvers.cs). The network's weapon messages aren't ported.
    // Titanium (mercsw.dll UpdateWeaponFireState 0x1006bf07 and its neighbours): fourteen slots and five groups, only
    // selectable types (flag 4) selected, fire all, ammunition from the class's pool, the AMS's own update and spawn (and
    // its off state), NARC's instant lock. Mercenaries (DOS-Win95 MERCSW.DLL UpdateWeaponFireState 0x10061d8b, both
    // editions) runs Titanium's model with its own differences: the pool's cursor steps to the next bin in memory (the AMS's
    // by one shot), only the Clan AMS (0x1d) toggles, the AMS measures the approximate distance, a type 5 shot leaves a
    // tracer, a volley's first shot carries a light, and the local player's sounding shots set its weapon's network bit.
    public sealed partial class Mech
    {
        public static bool TitaniumWeapons => WeaponDef.Titanium;
#if UNITY_EDITOR || MEKENGINE_MERCS
        public static bool MercsWeapons => WeaponDef.Mercs;

        // Mercenaries' g_netWeaponFlagsOut (0x100f2ff0): the local player's weapons (by WeaponSlot.Index) that fired a
        // sounding shot since the network weapons message last went out (not sent: the message isn't ported).
        public static readonly bool[] g_netWeaponFired = new bool[14];

        // Mercenaries' g_firstShotOfVolley (0x100b3790) as SpawnShot reads it.
        public bool m_firstShotOfVolley;

#endif
        // c_shotTargetPlayer (Shot::m_targetKind): a guided shot's target is a player.
        public const int c_shotTargetPlayer = 0x200;

        // GBL's weapon rules; a MekNet match runs Titanium's (WeaponDef.Titanium).
        public static bool GblWeapons => Host.Variant.IsGhostBearsLegacy && !Host.Net.NetRules;

        // g_singleWeaponFire: the fire button fires only the selected weapon; cleared, it fires its whole group. Not the
        // original's: the mech's own (each pilot toggles its own); SingleWeaponFire the local mech's, for the cockpit.
        public bool m_singleWeaponFire = true;
        public static bool SingleWeaponFire => SimMain.LocalMech == null || SimMain.LocalMech.m_singleWeaponFire;

        // g_manualWeaponCycle: the weapon key cycles through empty weapons too.
        public static bool g_manualWeaponCycle;

        // ReleaseWeaponTriggers: weapons firing or held go back to ready.
        public void ReleaseWeaponTriggers()
        {
            if (m_config == null) return;
            foreach (WeaponSlot slot in m_config.m_weapons)
            {
                if (slot.m_state == WeaponSlot.Firing || slot.m_state == WeaponSlot.Held) slot.m_state = WeaponSlot.Ready;
            }
        }

        public bool AnyFireHeld() =>
            m_steering.Pressed("arms.fire") || m_steering.Pressed("arms.fire.group") || m_steering.Pressed("arms.fire.group1") ||
            m_steering.Pressed("arms.fire.group2") || m_steering.Pressed("arms.fire.group3") ||
            m_steering.Pressed("arms.fire.group4") || m_steering.Pressed("arms.fire.group5");

        // Starts a volley of a ready weapon (and its lock on the target, for a guided weapon while locked). In a MekNet
        // match a mech's volleys are its firing machine's alone (the others' come by NetFire).
        public void StartVolley(WeaponSlot p_slot, WeaponDef p_def)
        {
            if (Host.Net.FiresByEvent(this)) return;
            StartVolleyHere(p_slot, p_def);
            Host.Net.Fired(this, Array.IndexOf(m_config.m_weapons, p_slot), p_slot.m_target, p_slot.m_targetKind);
        }

        // Not in the original: the firing machine's volley (NetMatchDriver.Fired's argument): u8 the slot, u8 the target,
        // the target's kind (0xf00) above, u8 the weapon's type on top. The type is what fires: the slot if it holds that
        // type here, else this mech's first slot of it (a ready one first). p_age: the ticks since it fired there, run on
        // at its next step (the volley's shots launched and flown as far as they'd be: WeaponSlot.CatchUp).
        // p_validate (the host, a client's volley): refused unless the weapon is ready here, or its recycle ends within
        // p_age plus FireSlackTicks (the lag between the machines' clocks); a modified client can't out-fire its weapons.
        // Power, ammunition and heat need no check: the volley runs in this machine's UpdateWeaponFireState.
        public const int FireSlackTicks = Clock.TicksPerSecond / 4;

        public void NetFire(int p_arg, int p_age, bool p_validate = false)
        {
            if (m_config == null) return;
            int index = p_arg & 0xff, type = (p_arg >> 24) & 0xff;
            WeaponSlot[] weapons = m_config.m_weapons;
            int count = Math.Min(weapons.Length, MechConfig.SlotCount);
            if (index >= count || weapons[index].m_type != type)
            {
                index = -1;
                for (int i = 0; i < count; i++)
                {
                    if (weapons[i].m_type != type) continue;
                    if (index < 0 || (weapons[index].m_state != WeaponSlot.Ready && weapons[i].m_state == WeaponSlot.Ready)) index = i;
                }
            }

            if (index < 0)
            {
                Debug.LogWarning($"NetFire: {m_player.m_name} has no weapon of type {type}");
                return;
            }

            // Not ready here (the machines' timers a little apart, a chain of like weapons fired as each recycles): another
            // of its type that is, so an honest chain is never refused; none, refused.
            if (p_validate && !CanNetFire(weapons[index], p_age))
            {
                index = -1;
                for (int i = 0; i < count && index < 0; i++)
                    if (weapons[i].m_type == type && CanNetFire(weapons[i], p_age)) index = i;
                if (index < 0)
                {
                    Debug.LogWarning($"NetFire: {m_player.m_name}'s weapon type {type} fired before it was ready; refused");
                    return;
                }
            }

            WeaponSlot slot = weapons[index];

            StartVolleyHere(slot, WeaponDef.Of(slot.m_type));
            slot.m_target = (p_arg >> 8) & 0xff;
            slot.m_targetKind = (p_arg >> 8) & 0xf00;
            // A player target came by its net id (NetMatchDriver.Fired); the host's relay sends it on by the player here.
            if (slot.m_targetKind == c_shotTargetPlayer)
            {
                slot.m_target = Host.Net.PlayerOfNetTarget(slot.m_target);
                if (slot.m_target < 0) slot.m_target = slot.m_targetKind = 0;
            }

            slot.m_catchUp = p_age;
            Host.Net.Fired(this, index, slot.m_target, slot.m_targetKind, p_age);
        }

        public static bool CanNetFire(WeaponSlot p_slot, int p_age)
        {
            if (p_slot.m_state == WeaponSlot.Ready) return true;
            if (p_slot.m_state != WeaponSlot.Recycling && p_slot.m_state != WeaponSlot.Held) return false;
            int left = WeaponDef.Of(p_slot.m_type).m_recycle - (Clock.g_currentClock - p_slot.m_time);
            return left <= p_age + FireSlackTicks;
        }

        public void StartVolleyHere(WeaponSlot p_slot, WeaponDef p_def)
        {
            p_slot.m_state = WeaponSlot.Firing;
            p_slot.m_volley = p_def.m_volley;
            p_slot.m_time = 0;
            DodgeShot(p_slot);
            if (p_def.m_guided != 0 && (m_flags & 0x80) != 0)
            {
                p_slot.m_target = m_targetInfo.m_target & 0xff;
                p_slot.m_targetKind = m_targetInfo.m_target & 0xf00;
            }
            else p_slot.m_target = p_slot.m_targetKind = 0;
        }

        // UpdateWeaponFireState: the fire buttons (the selected weapon, every weapon of its group, or groups 1 to 3; a
        // trigger fires once until let go), then each weapon's state: recycling until ready, a volley's shots launched
        // every m_interval ticks (with their heat, effects and sound), an auto-repeating weapon held while the trigger is.
        public void UpdateWeaponFireState()
        {
            if (PowerState != 2 || m_config == null) return;
            bool first = false;
            WeaponSlot[] weapons = m_config.m_weapons;
            m_lastSelectedWeapon = m_selectedWeapon;
            if (m_steering.Pressed("arms.chain.toggle"))
            {
                m_steering["arms.chain.toggle"] = 0;
                m_singleWeaponFire = !m_singleWeaponFire;
                if (IsLocal) MissionSpeech.PlayCockpitSound(m_singleWeaponFire ? 6 : 7, 1);
            }

            if ((m_flags & 0x4000) != 0) AddNextWeaponToGroup();
            else if (m_selectedWeapon != -1)
            {
                WeaponSlot slot = weapons[m_selectedWeapon];
                WeaponDef def = WeaponDef.Of(slot.m_type);
                // Titanium leaves the trigger alone while the group cycles (FIRE_CYCLE_WEAPON_GROUP).
                if (!m_singleWeaponFire && IsPilot && !(TitaniumWeapons && m_steering.Pressed("arms.group.next")))
                {
                    m_steering["arms.fire.group"] = m_steering["arms.fire"];
                    m_steering["arms.fire"] = 0;
                }

                if (m_steering.Pressed("arms.fire.group"))
                {
                    if ((m_flags & 1) == 0)
                    {
                        m_flags |= 1;
                        if (TitaniumWeapons) FireOwnWeaponGroup();
                        else FireWeaponGroup(this);
                    }
                }
                // Titanium's fire all; MekEngine's in every edition.
                else if (m_steering.Pressed("arms.fire.all"))
                {
                    if ((m_flags & 1) == 0)
                    {
                        m_flags |= 1;
                        FireAllWeapons();
                    }

                    m_steering["arms.fire.all"] = 0;
                }
                else if (m_steering.Pressed("arms.fire.group1"))
                {
                    if ((m_flags & 1) == 0)
                    {
                        m_flags |= 1;
                        if (SelectWeaponGroup(0)) FireGroup();
                    }
                }
                else if (m_steering.Pressed("arms.fire.group2"))
                {
                    if ((m_flags & 1) == 0)
                    {
                        m_flags |= 1;
                        if (SelectWeaponGroup(1)) FireGroup();
                    }
                }
                else if (m_steering.Pressed("arms.fire.group3"))
                {
                    if ((m_flags & 1) == 0)
                    {
                        m_flags |= 1;
                        if (SelectWeaponGroup(2)) FireGroup();
                    }
                }
                // Not the original's: groups 4 and 5 in every edition.
                else if (m_steering.Pressed("arms.fire.group4"))
                {
                    if ((m_flags & 1) == 0)
                    {
                        m_flags |= 1;
                        if (SelectWeaponGroup(3)) FireGroup();
                    }
                }
                else if (m_steering.Pressed("arms.fire.group5"))
                {
                    if ((m_flags & 1) == 0)
                    {
                        m_flags |= 1;
                        if (SelectWeaponGroup(4)) FireGroup();
                    }
                }
                else if (m_steering.Pressed("arms.fire"))
                {
                    if ((m_flags & 1) == 0)
                    {
                        m_flags |= 1;
                        if (slot.m_state == WeaponSlot.Ready) StartVolley(slot, def);
                        else if (IsLocal) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0x149), 100, 0x40, 5, 0x32);
                    }
                }
                else if ((m_flags & 1) != 0)
                {
                    m_flags &= ~1;
                    SelectNextWeaponInGroup(false);
                }
            }

            for (int i = 0; i < MechConfig.SlotCount && i < weapons.Length; i++)
            {
                WeaponSlot slot = weapons[i];
                if (slot.m_type < 0) continue;
                WeaponDef def = WeaponDef.Of(slot.m_type);
                // Titanium's per-frame function (+0): the AMS's watch for missiles.
                if (TitaniumWeapons && WeaponDef.IsAms(slot.m_type)) AmsUpdate(slot, def);
                // GBL: the AMS (type 0x1d) has the per-frame function too (MW2.flat 0x54940).
                else if (GblWeapons && slot.m_type == 0x1d) AmsUpdateGbl(slot, def);
                switch (slot.m_state)
                {
                    case WeaponSlot.Recycling:
                        if (Clock.g_currentClock - slot.m_time >= def.m_recycle)
                        {
                            slot.m_state = WeaponSlot.Ready;
                            slot.m_time = 0;
                        }

                        break;
                    case WeaponSlot.Held:
                        if (Clock.g_currentClock - slot.m_time >= def.m_recycle)
                        {
                            // A MekNet match: the held weapon's next volley its firing machine's (NetFire) too.
                            if (!Host.Net.FiresByEvent(this) &&
                                ((m_selectedWeapon >= 0 && weapons[m_selectedWeapon] == slot) || m_steering.Pressed("arms.fire.group") ||
                                m_steering.Pressed("arms.fire.group1") || m_steering.Pressed("arms.fire.group2") ||
                                m_steering.Pressed("arms.fire.group3") || m_steering.Pressed("arms.fire.group4") ||
                                m_steering.Pressed("arms.fire.group5")))
                            {
                                slot.m_state = WeaponSlot.Firing;
                                Host.Net.Fired(this, i, slot.m_target, slot.m_targetKind);
                            }
                            else slot.m_state = WeaponSlot.Ready;

                            slot.m_time = 0;
                        }

                        break;
                    case WeaponSlot.Firing:
                        if (slot.m_volley > 0)
                        {
                            if (slot.m_volley == def.m_volley) first = true;
                            // A reported volley (MekNet): the ticks it began before here run now, each shot launched in
                            // them flown on as far as it would be.
                            int catchUp = slot.m_catchUp;
                            slot.m_catchUp = 0;
                            int time = slot.m_time + Clock.g_deltaTime + catchUp;
                            while (def.m_interval <= time && slot.m_volley > 0)
                            {
                                m_firingObj = slot.m_hardpoint >= 0 && slot.m_hardpoint < 8 ? m_objects[slot.m_hardpoint] : null;
                                if (catchUp > 0) Shots.BeginCapture();
#if UNITY_EDITOR || MEKENGINE_MERCS
                                m_firstShotOfVolley = first;
#endif
                                int launched = TitaniumWeapons
                                    ? WeaponDef.IsAms(slot.m_type) ? AmsSpawn(slot, def) : SpawnShotTitanium(slot, def)
                                    : GblWeapons && slot.m_type == 0x1d ? AmsSpawnGbl(slot, def)
                                    : SpawnShot(slot) ? 1 : 0;
                                if (launched != 0)
                                {
                                    Effects.SpawnLaunchEffect(def.m_launchEffect, m_firingObj);
                                    Effects.SpawnLaunchEffect(def.m_launchEffect2, m_firingObj);
                                    slot.m_volley -= launched;
                                    time -= def.m_interval;
                                    slot.m_time = time;
                                    if (catchUp > 0) Shots.CatchUpCaptured(Math.Min(time, catchUp));
                                    m_deltaHeat += def.m_heatFx;

                                    if (TitaniumWeapons)
                                    {
                                        // The nuke's launch line; the sound with the volley's first shot (flag 1) or every
                                        // shot (flag 2).
                                        if (slot.m_type == 0x1e && WeaponDef.NativeTitanium) MissionSpeech.PlayCockpitSound(0x23, -1);
                                        if (((def.m_flags & WeaponDef.FlagFirstSound) != 0 && first) ||
                                            (def.m_flags & WeaponDef.FlagEverySound) != 0)
                                        {
                                            first = false;
#if UNITY_EDITOR || MEKENGINE_MERCS
                                            // Mercenaries: the local player's weapon fired (the network message's bit).
                                            if (MercsWeapons && IsLocal && slot.m_index >= 0 && slot.m_index < g_netWeaponFired.Length)
                                                g_netWeaponFired[slot.m_index] = true;
#endif
                                            if (WeaponDef.Local(slot.m_type).m_sound > 0) PlayLaunchSound(slot);
                                        }
                                    }
                                    else if (first)
                                    {
                                        first = false;
                                        if (WeaponDef.Local(slot.m_type).m_sound > 0) PlayLaunchSound(slot);
                                    }

                                    if (slot.m_volley == 0 && def.m_autoRepeat != 0 && AnyFireHeld())
                                    {
                                        slot.m_time = Clock.g_currentClock;
                                        slot.m_state = WeaponSlot.Held;
                                        slot.m_volley = def.m_volley;
                                        break;
                                    }
                                }
                                else
                                {
                                    m_flags &= ~1;
                                    slot.m_volley = 0;
                                }
                            }

                            if (slot.m_state != WeaponSlot.Held) slot.m_time = time;
                        }
                        else
                        {
                            slot.m_time = Clock.g_currentClock;
                            slot.m_state = WeaponSlot.Recycling;
                            slot.m_volley = 0;
                        }

                        break;
                }
            }
        }

        // A volley's sound: in the cockpit the local player's own (PlayWeaponLaunchSound: panned by the hardpoint's side),
        // else from the mech. The edition's own sound of the weapon (WeaponDef.Local).
        public void PlayLaunchSound(WeaponSlot p_slot)
        {
            int sound = WeaponDef.Local(p_slot.m_type).SoundId;
            if (IsLocal && Eyepoint.g_inCockpitView)
            {
                int pan;
                switch (p_slot.m_hardpoint)
                {
                    case 2:
                    case 5:
                        pan = 0x4f;
                        break;
                    case 4:
                    case 6:
                        pan = 0x2f;
                        break;
                    default:
                        pan = 0x40;
                        break;
                }

                // PlayWeaponLaunchSound: PlayDelayedSound(0, 0, sound, 0x32, pan, 0x32).
                CockpitSound.PlaySoundEffect(sound, 0x32, pan, 5, 0x32);
            }
            else EffectSound.PlaySoundAt(Host.Audio.EyeUnits - ObjectPosition, sound, Eyepoint.g_inCockpitView);
        }

        // SpawnShot: launches one shot of p_slot's weapon from the current hardpoint: the ammunition (bin by bin), and a
        // free shot of its type sent along the aim. False when the weapon can't fire again: no hardpoint, or out of
        // ammunition (the last shot still flies).
        public bool SpawnShot(WeaponSlot p_slot)
        {
            WeaponDef def = WeaponDef.Of(p_slot.m_type);
            bool result = true;
            if (m_firingObj == null) return false;
            if (p_slot.m_ammo == 0) return false;

            Player local = SimMain.PlayerOf(SimMain.g_localPlayerId);
            Host.World.CountShotFired(IsLocal, local != null && m_player.m_team == local.m_team);

            if (p_slot.m_ammo != -1 && (!SimMain.g_difficulty.m_unlimitedAmmo || !IsPilot))
            {
                p_slot.m_ammo--;
                AmmoBin bin = p_slot.m_bin >= 0 && p_slot.m_bin < m_config.m_ammoBins.Length ? m_config.m_ammoBins[p_slot.m_bin] : null;
                if (bin != null && bin.m_shots != 0) bin.m_shots--;
                else
                {
                    p_slot.m_binIndex++;
                    if (p_slot.m_binCount == p_slot.m_binIndex) p_slot.m_binIndex--;
                    else p_slot.m_bin++;
                }

                if (p_slot.m_ammo == 0)
                {
                    p_slot.m_state = WeaponSlot.Empty;
                    result = false;
                }
            }

            if (def.m_launchesShot != 0) LaunchShot(p_slot, def);

            return result;
        }

        // SelectNextWeaponInGroup: the next weapon of the selected one's group that still fires; p_wrap moves on to the
        // next group when there is none.
        public void SelectNextWeaponInGroup(bool p_wrap)
        {
            if (m_config == null || m_config.m_weaponCount <= 0 || m_selectedWeapon < 0) return;
            WeaponSlot[] weapons = m_config.m_weapons;
            int selected = m_selectedWeapon;
            int group = weapons[selected].m_group;
            int index = selected;
            bool found = false;
            int count = MechConfig.SlotCount;
            for (int i = 1; i < count; i++)
            {
                index = (index + 1) % count;
                WeaponSlot slot = weapons[index];
                if (slot.m_type < 0) continue;
                if (slot.m_status != -1 && slot.m_group == group && slot.m_state != WeaponSlot.Empty && index != selected &&
                    WeaponDef.Of(slot.m_type).Selectable)
                {
                    found = true;
                    m_selectedWeapon = index;
                    break;
                }
            }

            if (!found && p_wrap) SelectNextWeaponGroup();
        }

        // SelectNextWeapon: the next weapon that still has ammunition (with the weapon key, the next one at all).
        public void SelectNextWeapon()
        {
            if (m_config == null || m_config.m_weaponCount <= 0) return;
            WeaponSlot[] weapons = m_config.m_weapons;
            int tries = 0, count = MechConfig.SlotCount;
            while (true)
            {
                m_selectedWeapon = (m_selectedWeapon + 1) % count;
                int type = weapons[m_selectedWeapon].m_type;
                if (type < 0) continue;
                if (g_manualWeaponCycle && IsPilot) return;
                if (weapons[m_selectedWeapon].m_status == -1) m_selectedWeapon = 0;
                // Titanium tests flag 4 of the type it stepped onto, before the wrap to 0 (as written).
                if ((weapons[m_selectedWeapon].m_state != WeaponSlot.Empty && WeaponDef.Of(type).Selectable) || ++tries == count) return;
            }
        }

        // SelectNextWeaponGroup: the first weapon of the next group that has one.
        public void SelectNextWeaponGroup()
        {
            if (m_config == null || m_config.m_weaponCount <= 0 || m_selectedWeapon < 0) return;
            WeaponSlot[] weapons = m_config.m_weapons;
            int current = weapons[m_selectedWeapon].m_group;
            int count = MechConfig.SlotCount;
            int group = (current + 1) % count;
            bool found = false;
            while (group != current && !found)
            {
                for (int i = 0; i < count && !found; i++)
                {
                    if (weapons[i].m_type < 0) continue;
                    if (weapons[i].m_status != -1 && weapons[i].m_group == group && weapons[i].m_state != WeaponSlot.Empty &&
                        WeaponDef.Of(weapons[i].m_type).Selectable)
                    {
                        found = true;
                        m_selectedWeapon = i;
                        break;
                    }
                }

                group = (group + 1) % count;
            }
        }

        // SelectWeaponGroup: the first weapon of p_group (0 to 2; MekEngine's 3 and 4 too); whether the group has one.
        public bool SelectWeaponGroup(int p_group)
        {
            if (p_group < 0 || p_group >= 5) return false;
            if (m_config == null || m_config.m_weaponCount <= 0 || m_selectedWeapon < 0) return false;
            WeaponSlot[] weapons = m_config.m_weapons;
            if (weapons[m_selectedWeapon].m_group == p_group) return true;
            for (int i = 0; i < MechConfig.SlotCount; i++)
            {
                if (weapons[i].m_type < 0) continue;
                if (weapons[i].m_status != -1 && weapons[i].m_group == p_group && weapons[i].m_state != WeaponSlot.Empty &&
                    WeaponDef.Of(weapons[i].m_type).Selectable)
                {
                    m_selectedWeapon = i;
                    return true;
                }
            }

            return false;
        }

        // IsSelectedWeaponReady: 1 ready, 0 not, -1 without one.
        public int IsSelectedWeaponReady()
        {
            if (m_selectedWeapon == -1 || m_config == null) return -1;
            return m_config.m_weapons[m_selectedWeapon].m_state == WeaponSlot.Ready ? 1 : 0;
        }

        // JettisonAmmo: the selected weapon's ammunition dumped.
        public bool JettisonAmmo()
        {
            if (m_selectedWeapon == -1 || m_config == null) return false;
            if (TitaniumWeapons) return JettisonPool();
            WeaponSlot slot = m_config.m_weapons[m_selectedWeapon];
            if (WeaponDef.Of(slot.m_type).m_volley > 0 && slot.m_ammo > 0)
            {
                if (IsLocal) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xb3), 100, 0x40, 5, 0x50);
                if (IsLocal) TimedOverlays.ShowInGameMessage("Ammo for current weapon jettisonned.", 1, 0x16a, 0x32);
                if (IsLocal) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xb3), 100, 0x40, 5, 0x32);
                slot.m_ammo = 0;
                slot.m_state = WeaponSlot.Empty;
                return true;
            }

            return false;
        }

        // SetWeaponGroup.
        public void SetWeaponGroup(int p_index, int p_group)
        {
            if (p_group >= 0 && p_group < 5 && p_index >= 0 && p_index < MechConfig.SlotCount) m_config.m_weapons[p_index].m_group = p_group;
        }

        // Not the original's (GameplayOptions.AutoWeaponGroups): the local player's weapons, when all in group 1 as
        // configured, grouped by family in this order, the families present numbered from group 1: ACs (Gauss, LB-X and
        // Ultra too), machine guns, lasers, LRMs (torpedoes too), SRMs (Streak, Inferno), PPCs, then everything else.
        // Groups past 5 are reached only by cycling.
        public void AutoGroupWeapons()
        {
            if (!IsLocal || !Host.Variant.AutoWeaponGroups || m_config == null) return;
            WeaponSlot[] weapons = m_config.m_weapons;
            bool any = false;
            foreach (WeaponSlot slot in weapons)
            {
                if (slot.m_type < 0) continue;
                if (slot.m_group != 0) return;
                any = true;
            }

            if (!any) return;
            var present = new bool[7];
            foreach (WeaponSlot slot in weapons)
                if (slot.m_type >= 0) present[WeaponFamily(WeaponDef.Of(slot.m_type).m_name)] = true;
            var groups = new int[7];
            int next = 0;
            for (int i = 0; i < groups.Length; i++) groups[i] = present[i] ? next++ : 0;
            foreach (WeaponSlot slot in weapons)
                if (slot.m_type >= 0) slot.m_group = groups[WeaponFamily(WeaponDef.Of(slot.m_type).m_name)];
        }

        // AutoGroupWeapons' family of a weapon, by its def's name (Clan names prefixed 'c').
        public static int WeaponFamily(string p_name)
        {
            string name = (p_name ?? "").ToUpperInvariant();
            if (name.Contains("LAS")) return 2;
            if (name.Contains("PPC")) return 5;
            if (name.Contains("LRM") || name.Contains("TORPEDO")) return 3;
            if (name.Contains("SRM")) return 4;
            if (name.Contains("MGUN")) return 1;
            if (name.Contains("AC") || name.Contains("GAUSS")) return 0;
            return 6;
        }

        // SetSelectedWeaponGroup: the local player's selected weapon into p_group (ADD_WEAPON_TO_GROUP_1 to 3).
        public static void SetSelectedWeaponGroup(Mech mech, int p_group)
        {
            if (mech == null || mech.m_config == null || mech.m_selectedWeapon < 0) return;
            mech.m_config.m_weapons[mech.m_selectedWeapon].m_group = p_group;
        }

        // CycleLocalWeaponGroup.
        public void CycleLocalWeaponGroup() => SelectNextWeaponGroup();

        // FireWeaponGroup: every ready weapon of the local player's selected group; the selection stays.
        // As written the local player's, whoever fires; in a match the firing mech's own (the same everywhere).
        public static void FireWeaponGroup(Mech p_firer)
        {
            Mech mech = Host.Net.NetRules ? p_firer : SimMain.LocalMech;
            if (mech == null || mech.m_config == null || mech.m_selectedWeapon < 0) return;
            int selected = mech.m_selectedWeapon;
            mech.SelectNextWeaponInGroup(false);
            int first = mech.m_selectedWeapon;
            do
            {
                WeaponSlot slot = mech.m_config.m_weapons[mech.m_selectedWeapon];
                WeaponDef def = WeaponDef.Of(slot.m_type);
                if (slot.m_state == WeaponSlot.Ready) mech.StartVolley(slot, def);
                mech.SelectNextWeaponInGroup(false);
            } while (mech.m_selectedWeapon != first);

            mech.m_selectedWeapon = selected;
        }

        // AddNextWeaponToGroup: the next weapon into the selected one's group.
        public void AddNextWeaponToGroup()
        {
            m_flags ^= 0x4000;
            if (m_selectedWeapon < 0) return;
            int next = m_selectedWeapon;
            int group = m_config.m_weapons[next].m_group;
            next++;
            if (next >= MechConfig.SlotCount || m_config.m_weapons[next].m_status == -1) next = 0;
            SetWeaponGroup(next, group);
        }

        // ---- Titanium -----------------------------------------------------------------------------------------------

        public void FireGroup()
        {
            if (TitaniumWeapons) FireOwnWeaponGroup();
            else FireWeaponGroup(this);
        }

        // Titanium's FireWeaponGroup (0x1006d7b4): this mech's selected group, for any player; the selection stays.
        public void FireOwnWeaponGroup()
        {
            if (m_config == null || m_selectedWeapon < 0) return;
            int selected = m_selectedWeapon;
            SelectNextWeaponInGroup(false);
            int first = m_selectedWeapon;
            do
            {
                WeaponSlot slot = m_config.m_weapons[m_selectedWeapon];
                if (slot.m_state == WeaponSlot.Ready) StartVolley(slot, WeaponDef.Of(slot.m_type));
                SelectNextWeaponInGroup(false);
            } while (m_selectedWeapon != first);

            m_selectedWeapon = selected;
        }

        // FireAllWeapons (0x1006d8d7): every ready weapon, selectable or not.
        public void FireAllWeapons()
        {
            if (m_config == null) return;
            for (int i = 0; i < m_config.m_weaponCount && i < m_config.m_weapons.Length; i++)
            {
                WeaponSlot slot = m_config.m_weapons[i];
                if (slot.m_state == WeaponSlot.Ready) StartVolley(slot, WeaponDef.Of(slot.m_type));
            }
        }

        // ToggleAMS (0x1000cc90): the local mech's AMS off (state 4) or back on, and the line saying which.
        public static void ToggleAms(Mech mech)
        {
            if (mech?.m_config == null) return;
            int said = -1;
            foreach (WeaponSlot slot in mech.m_config.m_weapons)
            {
                // Mercenaries (0x1000f580) toggles the Clan AMS (0x1d) only.
#if UNITY_EDITOR || MEKENGINE_MERCS
                if (slot.m_type < 0 || (WeaponDef.Mercs ? slot.m_type != 0x1d : !WeaponDef.IsAms(slot.m_type))) continue;
#else
                if (slot.Type < 0 || !WeaponDef.IsAms(slot.Type)) continue;
#endif
                if (slot.m_state == WeaponSlot.Off)
                {
                    slot.m_state = WeaponSlot.Ready;
                    said = 1;
                }
                else if (slot.m_state == WeaponSlot.Ready)
                {
                    slot.m_state = WeaponSlot.Off;
                    said = 2;
                }
            }

            if (said != -1 && mech.IsLocal) MissionSpeech.PlayCockpitSound(10, said);
        }

        // JettisonAmmo (0x1006d424): the selected weapon's pool dumped: every weapon of its class empties, its bins too.
        public bool JettisonPool()
        {
            WeaponSlot slot = m_config.m_weapons[m_selectedWeapon];
            AmmoPool pool = slot.m_pool >= 0 && slot.m_pool < m_config.m_poolCount ? m_config.m_pools[slot.m_pool] : null;
            if (pool == null || WeaponDef.Of(slot.m_type).m_volley < 1 || pool.m_shots < 1) return false;
            if (IsLocal) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xb3), 100, 0x40, 5, 0x50);
            if (IsLocal) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xb3), 100, 0x40, 5, 0x32);
            if (IsLocal) MissionSpeech.PlayCockpitSound(0x22, -1);
            foreach (WeaponSlot other in m_config.m_weapons)
            {
                if (other.m_slotId / 100 == pool.m_class) other.m_state = WeaponSlot.Empty;
            }

            for (int i = 0; i < pool.m_itemCount; i++) m_config.m_ammoBins[pool.m_items[i]].m_shots = 0;
            pool.m_shots = 0;
            return true;
        }

        // Titanium's ammunition (SpawnShot 0x1006c804, AmsSpawn): p_count shots off the pool, bin by bin; whether the pool
        // still has shots.
        public bool DrawFromPool(AmmoPool p_pool, int p_count)
        {
            AmmoBin Current() => p_pool.m_current >= 0 && p_pool.m_current < m_config.m_ammoBins.Length ? m_config.m_ammoBins[p_pool.m_current] : null;
            p_pool.m_shots -= p_count;
            AmmoBin bin = Current();
            if (p_pool.m_shots < 1)
            {
                p_pool.m_shots = 0;
                if (bin != null) bin.m_shots = 0;
            }

            if (bin != null && bin.m_shots < 1)
            {
                bin.m_shots = 0;
                p_pool.m_itemIndex++;
                if (p_pool.m_itemIndex == p_pool.m_itemCount) p_pool.m_itemIndex--;
                else
                {
                    p_pool.m_current = p_pool.m_items[p_pool.m_itemIndex];
                    bin = Current();
                    if (bin != null) bin.m_shots = (short)(bin.m_shots - p_count);
                }
            }
            else if (bin != null) bin.m_shots = (short)(bin.m_shots - p_count);

            return p_pool.m_shots >= 1;
        }

        // Titanium's SpawnShot (0x1006c804): a pool with shots (or none, or unlimited ammunition) fires; the pool drawn
        // on (not for the local player with unlimited ammunition), and when it runs dry every weapon of its class
        // empties; a free shot of the type takes the def's flags, the flamer's animations restart, a missile type (3, 4,
        // 9, 10, 12) launches spread. 1 launched, 0 when it can't fire again (the last shot still flies).
        public int SpawnShotTitanium(WeaponSlot p_slot, WeaponDef p_def)
        {
            if (m_firingObj == null) return 0;
            AmmoPool pool = p_slot.m_pool >= 0 && p_slot.m_pool < m_config.m_poolCount ? m_config.m_pools[p_slot.m_pool] : null;
            bool unlimited = SimMain.g_difficulty.m_unlimitedAmmo;
            if (pool != null && pool.m_shots == 0 && !unlimited) return 0;

            int result = 1;
            Player local = SimMain.PlayerOf(SimMain.g_localPlayerId);
            Host.World.CountShotFired(IsLocal, local != null && m_player.m_team == local.m_team);

            if (pool != null && pool.m_shots != -1 && (!unlimited || !IsPilot))
            {
#if UNITY_EDITOR || MEKENGINE_MERCS
                if (MercsWeapons ? !DrawFromPoolMercs(pool, 1, false) : !DrawFromPool(pool, 1))
#else
                if (!DrawFromPool(pool, 1))
#endif
                {
                    foreach (WeaponSlot other in m_config.m_weapons)
                    {
                        if (other.m_slotId / 100 == pool.m_class) other.m_state = WeaponSlot.Empty;
                    }

                    result = 0;
                }
            }

            if (p_def.m_launchesShot == 0) return result;
            LaunchShotTitanium(p_slot, p_def);
            return result;
        }

#if UNITY_EDITOR || MEKENGINE_MERCS
        // Mercenaries' pool draw (SpawnShot 0x10062775, AmsSpawn 0x1000f815): p_count shots off the pool, and its cursor one
        // shot on: an empty current bin steps to the next bin in memory (AmmoBins index + 1, whatever its class; as
        // written), until the pool's last item; else the bin loses one. p_clamp (the AMS's): the pool not below 0. Whether
        // the pool still has shots (not exactly 0: a pool a critical hit drove below 0 never runs dry, as written).
        public bool DrawFromPoolMercs(AmmoPool p_pool, int p_count, bool p_clamp)
        {
            p_pool.m_shots -= p_count;
            if (p_clamp && p_pool.m_shots < 0) p_pool.m_shots = 0;
            AmmoBin bin = p_pool.m_current >= 0 && p_pool.m_current < m_config.m_ammoBins.Length ? m_config.m_ammoBins[p_pool.m_current] : null;
            if (bin != null && bin.m_shots == 0)
            {
                p_pool.m_itemIndex++;
                if (p_pool.m_itemIndex == p_pool.m_itemCount) p_pool.m_itemIndex--;
                else p_pool.m_current++;
            }
            else if (bin != null) bin.m_shots = (short)(bin.m_shots - 1);

            return p_pool.m_shots != 0;
        }
#endif

        // GBL's AMS shots (MW2.flat 0x54aa0): Titanium's AmsSpawn with the weapon's own ammunition (its bins), not a pool;
        // then the intercept (0x54bb0).
        public int AmsSpawnGbl(WeaponSlot p_slot, WeaponDef p_def)
        {
            if (m_firingObj == null || p_slot.m_ammo == 0) return 0;
            int rounds = 2;
            Player local = SimMain.PlayerOf(SimMain.g_localPlayerId);
            Host.World.CountShotFired(IsLocal, local != null && m_player.m_team == local.m_team);
            if (p_slot.m_ammo != -1 && (!SimMain.g_difficulty.m_unlimitedAmmo || !IsPilot))
            {
                rounds = SimRandom.RandomIntBelow(5) + 1;
                if (p_slot.m_volley - rounds < 0) rounds = p_slot.m_volley;
                p_slot.m_ammo -= rounds;
                if (p_slot.m_ammo < 0) p_slot.m_ammo = 0;
                AmmoBin bin = p_slot.m_bin >= 0 && p_slot.m_bin < m_config.m_ammoBins.Length ? m_config.m_ammoBins[p_slot.m_bin] : null;
                if (bin != null && bin.m_shots != 0) bin.m_shots--;
                else
                {
                    p_slot.m_binIndex++;
                    if (p_slot.m_binCount == p_slot.m_binIndex) p_slot.m_binIndex--;
                    else p_slot.m_bin++;
                }

                if (p_slot.m_ammo == 0)
                {
                    p_slot.m_state = WeaponSlot.Empty;
                    MissionSpeech.PlayCockpitSound(10, 0);
                    rounds = 0;
                }
            }

            AmsInterceptGbl();
            return rounds;
        }

        // AmsSpawn (0x1000ceef): 1 to 5 rounds off the pool (within the volley; none for the local player with unlimited
        // ammunition, which counts 2), then AmsIntercept. Out of rounds the AMS empties and says so.
        public int AmsSpawn(WeaponSlot p_slot, WeaponDef p_def)
        {
            if (m_firingObj == null) return 0;
            AmmoPool pool = p_slot.m_pool >= 0 && p_slot.m_pool < m_config.m_poolCount ? m_config.m_pools[p_slot.m_pool] : null;
            if (pool == null || pool.m_shots == 0) return 0;

            int rounds = 2;
            Player local = SimMain.PlayerOf(SimMain.g_localPlayerId);
            Host.World.CountShotFired(IsLocal, local != null && m_player.m_team == local.m_team);
            if (pool.m_shots != -1 && (!SimMain.g_difficulty.m_unlimitedAmmo || !IsPilot))
            {
                rounds = SimRandom.RandomIntBelow(5) + 1;
                if (p_slot.m_volley - rounds < 0) rounds = p_slot.m_volley;
                // Mercenaries (0x1000f815): the pool loses the rounds (not below 0), its cursor one shot.
#if UNITY_EDITOR || MEKENGINE_MERCS
                if (MercsWeapons) DrawFromPoolMercs(pool, rounds, true);
                else DrawFromPool(pool, rounds);
#else
                DrawFromPool(pool, rounds);
#endif
                if (pool.m_shots == 0)
                {
                    p_slot.m_state = WeaponSlot.Empty;
                    rounds = 0;
                    MissionSpeech.PlayCockpitSound(10, 0);
                }
            }

            AmsIntercept();
            return rounds;
        }

        // IsWithinRadius (Matrox, inline): the squares, exactly (whole units).
        public static bool IsWithinRadius(Vector3Int p_d, long p_radius) =>
            (long)p_d.x * p_d.x + (long)p_d.y * p_d.y + (long)p_d.z * p_d.z <= p_radius * p_radius;

        // Not in the original, MoonBunny Edition's (GameplayOptions.MoonBunnyActive): the SLDF Field Damper
        // (MechConfig.DamperRemoved). A PPC's hit on a mech without one (1 in 40
        // with one) drops its lock and keeps it off: 3 seconds, a Clan PPC's on an Inner Sphere mech 5, an Inner Sphere
        // PPC's on a Clan mech 1. A MekNet match: the host's alone, reported (NetMatchDriver.LockJam).
        public int m_lockJamUntil;
        public bool? m_innerSphere;

        public bool LockJammed => Clock.g_currentClock < m_lockJamUntil;


        public void JamLock(int p_ticks)
        {
            m_lockJamUntil = Math.Max(m_lockJamUntil, Clock.g_currentClock + p_ticks);
            m_flags &= ~(0x40 | 0x80 | 0x8000);
        }

        // UpdateWeaponLock: with a guided weapon selected and the target (not a nav) beyond its short range and within
        // its long one, within 16 degrees of the aim, the lock comes on after 0x16a ticks (m_stateTime counts it).
        // Titanium (0x1006da56): an AI player with a lock chance keeps a lock it has; a NARC'd mech target locks at once.
        public void UpdateWeaponLock()
        {
            // A MekNet match: another machine's mech's lock is as it reports (NetMatchDriver.OnLock).
            if (Host.Net.FiresByEvent(this)) return;
            if (LockJammed)
            {
                m_flags &= ~(0x40 | 0x80 | 0x8000);
                return;
            }
            if (TitaniumWeapons && m_player.m_aiMode == 2 && m_aiLockChance != 0 && (m_flags & 0x80) != 0) return;
            m_flags &= ~0x80;
            WeaponDef def = m_selectedWeapon >= 0 && m_config != null ? WeaponDef.Of(m_config.m_weapons[m_selectedWeapon].m_type) : default;
            int target = m_targetInfo.m_target;
            if (m_selectedWeapon == -1 || target == 0 || (target & 0x1000) != 0 || (target & 0x100) != 0 || def.m_guided == 0)
            {
                m_flags &= 0x7fff;
                m_flags &= 0xffbf;
                return;
            }

            // GBL's lock (MW2.flat 0x54160) has the NARC lock too (target mech +0x112 bit 1).
            if ((TitaniumWeapons || GblWeapons) && (target & 0x200) != 0 && SimMain.MechOf(target & 0xff) is Mech narced && narced.m_narced)
            {
                m_stateTime = 0;
                m_flags |= 0x40 | 0x80 | 0x8000;
                return;
            }

            Vector3Int d = ObjectPosition - m_targetInfo.m_position;
            bool inRange;
            int yaw, pitch;
            if (IsWithinRadius(d, def.m_shortRange) || !IsWithinRadius(d, def.m_longRange))
            {
                inRange = false;
                yaw = pitch = 0x110000;
                m_flags &= 0x7fff;
            }
            else
            {
                inRange = true;
                int facing = (m_heading % 0x1680000 + 0x1680000) % 0x1680000;
                int twist = m_torsoTwist.m_value % 0x1680000;
                int bearing = m_targetInfo.m_heading - facing;
                if (bearing > 0xb40000) bearing -= 0x1680000;
                else if (bearing < -0xb40000) bearing += 0x1680000;
                yaw = bearing - twist;
                if (yaw > 0xb40000) yaw -= 0x1680000;
                else if (yaw < -0xb40000) yaw += 0x1680000;
                pitch = (m_targetInfo.m_pitch + m_torsoPitch.m_value) % 0x1680000;
            }

            if (inRange && System.Math.Abs(yaw) < 0x100000 && System.Math.Abs(pitch) < 0x100000)
            {
                m_flags |= 0x8000;
                if ((m_flags & 0x40) == 0)
                {
                    m_flags |= 0x40;
                    m_stateTime = 0x16a;
                }
                else if (m_stateTime <= 0)
                {
                    m_stateTime = 0;
                    m_flags |= 0x80;
                }
                else m_stateTime -= Clock.g_deltaTime;
            }
            else
            {
                m_flags &= 0x7fff;
                if ((m_flags & 0x40) != 0)
                {
                    m_stateTime += Clock.g_deltaTime;
                    if (m_stateTime >= 0x16a) m_flags &= ~0x40;
                }
            }
        }

        // ---- Aiming -----------------------------------------------------------------------------------------------

        // GetAimRange: Player::m_aimRange's value, units.
        public int AimRange => m_aimRange != null ? m_aimRange.m_value : 50000;

        // m_throttle's value.
        public int Throttle => m_throttle != null ? m_throttle.m_value : 0x400;

        // UpdateAimDistance: the aim ray cast; a mech or game thing it hits sets the distance the weapons converge at (at
        // least 2000), and for the local player is the aimed shape (TargetAtReticle's). Returns the shape hit.
        public Shape UpdateAimDistance()
        {
            m_aimDistance.UpdateRamp();
            CollisionRay ray = BuildAimRay();
            if (ShapeCollision.TestSegmentCollision(ref ray, out Shape found, m_player.m_index) && found != null)
            {
                Shape hit = found;
                if ((hit.Kind & 0x100) != 0 || (hit.Kind & 0x200) != 0)
                {
                    long length = ray.GetRayLength();
                    m_aimDistance.m_value = length > 2000L << 16 ? (int)(length >> 16) : 2000;
                    if (IsPilot) m_aimedShape = hit;
                    if (IsLocal) SimMain.g_aimedShape = hit;
                }
            }

            m_aimRange.m_target = m_aimDistance.m_value;
            m_aimRange.UpdateRamp();
            return found;
        }

        // BuildAimRay: from the eye object (raised by the local mech's cockpit height, g_eyeHeightOffset), 150000 long,
        // along the eye's heading and roll with the torso's full pitch (GetEyeAimDirection).
        public CollisionRay BuildAimRay()
        {
            FixVec at = Host.Anim.GetObjPosition(m_eyeObj);
            // g_eyeHeightOffset: the local mech's, as written; in a match the mech's own.
            Mech local = Host.Net.NetRules ? this : SimMain.LocalMech;
            if (local != null) at.Y += (long)local.m_cockpitHeight << 16;
            return CollisionRay.BuildRayFromDirection(at, GetEyeAimDirection(), 150000L << 16);
        }

        // GetEyeAimDirection: the eye object's world heading (its node's, SimNodes) with the torso's full pitch, 16.16
        // (Quaternion.Euler(pitch, heading, roll) * forward; the roll doesn't turn the forward axis).
        public FixVec GetEyeAimDirection()
        {
            int yaw = Host.Anim.GetObjWorldYaw(m_eyeObj);
            int cosPitch = Fixed.Cos(TorsoPitch);
            return new FixVec(Fixed.Mul16(Fixed.Sin(yaw), cosPitch), -Fixed.Sin(TorsoPitch), Fixed.Mul16(Fixed.Cos(yaw), cosPitch));
        }

        // GetMechAimDirection: from the firing hardpoint to the aim ray's end at the aim range (16.16 units).
        public FixVec GetMechAimDirection()
        {
            CollisionRay ray = BuildAimRay();
            ray.SetRayLength((long)AimRange << 16);
            return ray.End - FiringPosition;
        }

        // GetFiringPosition (16.16 units).
        public FixVec FiringPosition => m_firingObj != null ? Host.Anim.GetObjPosition(m_firingObj) : FixVec.FromUnits(ObjectPosition);

    }
}
