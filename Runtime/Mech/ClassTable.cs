using UnityEngine;

namespace Demech2
{
    // The gamepieces that aren't mechs (demech2 MW2/src/artillery.c: FirstArtillery, UpdateArtillery,
    // LateUpdateArtillery; door.c: FirstDoor, UpdateDoor, LateUpdateDoor), in 1.1's fixed point. Artillery (player type 3)
    // turns its torso and fires its weapons as its AI aims them; it doesn't move. A door (type 7) slides toward the nav
    // of its star's current objective when that is a go-to objective (0x100), its position eased by its speed, throttle
    // and turn rate ramps (x, y and z).
    public sealed partial class Mech
    {
        // FirstArtillery: the torso's ramps (0.8 seconds), its ten weapon slots, its state, standing where it is, the
        // steering cleared, the AI (InitializeAI); it has no speed.
        public void FirstArtillery()
        {
            m_torsoTwist = new Ramp(0, 0.8);
            m_torsoPitch = new Ramp(0, 0.8);
            m_selectedWeapon = 0;
            m_heat = 0;
            m_lastSelectedWeapon = 0;
            if (m_config != null) m_config.m_weaponCount = 10;
            m_collisionTicks = 0;
            m_flags = 0x2000;
            PowerState = 0;
            m_stateTime = 0;
            m_deltaHeat = 0;
            m_autopilot = 0;
            m_velocityY = 0;
            UpdateHeadingTrig();
            m_host.PoseMech(true);
            m_steering["mech.autopilot"] = 0;
            m_steering["drive.throttle"] = 0;
            m_steering["nav.next"] = 0;
            InitializeAI();
            m_topSpeed = 0;
        }

        // UpdateArtillery: the torso's ramps; the pitch object turned by the full pitch, the torso by the twist.
        public void UpdateArtillery()
        {
            m_deltaHeat = 0;
            m_torsoTwist.UpdateRamp();
            m_torsoPitch.UpdateRamp();
            int pitch = m_torsoPitch.m_value, twist = m_torsoTwist.m_value;
            if (m_pitchObj != null)
                Host.Anim.SetObjRotationFixed(m_pitchObj, pitch, 0, 0);
            if (m_torsoObj != null) Host.Anim.SetObjRotationFixed(m_torsoObj, 0, twist, 0);
        }

        // LateUpdateArtillery: the AI; running, the weapons, the user's target dropped once it can't be claimed (an AI
        // piece's always), the lock, no autopilot, no throttle overheating; the torso aimed from the steering (turning
        // freely with a twist limit of a full turn, else held within it). Otherwise everything to rest. Then the heat and
        // the power states: it powers up in 6 to 8 seconds, a shutdown starts over, a destroyed one smokes.
        public void LateUpdateArtillery()
        {
            UpdateAI();
            if (PowerState == 2)
            {
                UpdateWeaponFireState();
                if (m_player.m_aiMode != 2)
                {
                    if (m_targetInfo.m_target != 0 && (m_targetInfo.m_target & 0x1000) == 0 && !Targeting.UpdateTarget(this)) m_targetInfo.m_target = 0;
                }
                else m_targetInfo.m_target = 0;

                UpdateWeaponLock();
                m_autopilot = 0;
                m_steering["mech.autopilot"] = 0;
                if ((m_flags & 4) != 0 && (m_flags & 8) == 0) m_steering["drive.throttle"] = 0;
                int twist = m_steering["torso.yaw"];
                if (m_maxTorsoTwist >= 0x1680000)
                {
                    int delta = twist - m_torsoTwist.m_value;
                    while (delta > 0xb40000) delta -= 0x1680000;
                    while (delta < -0xb40000) delta += 0x1680000;
                    m_torsoTwist.m_target = twist;
                    m_torsoTwist.m_value = twist - delta;
                }
                else if (m_maxTorsoTwist < m_torsoTwist.m_target) m_torsoTwist.m_target = m_maxTorsoTwist;
                else if (-m_maxTorsoTwist > m_torsoTwist.m_target) m_torsoTwist.m_target = -m_maxTorsoTwist;

                m_torsoPitch.m_target = m_steering["torso.pitch"];
            }
            else
            {
                m_torsoTwist.m_target = 0;
                m_torsoPitch.m_target = 0;
                m_steering["drive.throttle"] = 0;
                m_steering["torso.yaw"] = 0;
                m_steering["torso.pitch"] = 0;
            }

            CalculateHeat();
            switch (PowerState)
            {
                case 0:
                    PowerState = 1;
                    m_stateTime = Clock.g_currentClock + SimRandom.RandomIntBelow(0x16a) + 0x43e;
                    break;
                case 1:
                    if (m_stateTime < Clock.g_currentClock) PowerState = 2;
                    break;
                case 3:
                    PowerState = 0;
                    break;
                case 4:
                    if (m_stateTime == 0) m_stateTime = Clock.g_currentClock + 0x712;
                    if (m_stateTime > Clock.g_currentClock) EmitWreckSmoke();
                    break;
            }
        }

        // FirstDoor: its state, standing where it is, its position ramps (0.3 seconds) on its place, no heading's sine,
        // the AI.
        public void FirstDoor()
        {
            m_selectedWeapon = 0;
            m_heat = 0;
            m_mobility = 0x10000;
            m_lastSelectedWeapon = 0;
            if (m_config != null) m_config.m_weaponCount = 0;
            m_collisionTicks = 0;
            m_flags = 0;
            PowerState = 0;
            m_stateTime = 0;
            m_deltaHeat = 0;
            m_autopilot = 0;
            m_velocityY = 0;
            m_host.PoseDoor();
            m_speed = new Ramp((int)m_positionX, 0.3);
            m_throttle = new Ramp((int)m_positionY, 0.3);
            m_turnRate = new Ramp((int)m_positionZ, 0.3);
            m_headingSin = 0;
            m_headingCos = 0x10000;
            InitializeAI();
        }

        // UpdateDoor: running, along its position ramps, facing its heading.
        public void UpdateDoor()
        {
            if (PowerState != 2) return;
            m_speed.UpdateRamp();
            m_throttle.UpdateRamp();
            m_turnRate.UpdateRamp();
            m_positionX = m_speed.m_value;
            m_positionY = m_throttle.m_value;
            m_positionZ = m_turnRate.m_value;
            m_host.PoseDoor();
            UpdateHeadingTrig();
        }

        // LateUpdateDoor: the AI; running, no target or autopilot, and with its star's objective a go-to of a nav (0x100)
        // its position ramps' targets set: the nav, or within a tick's move of it (IsWithinRadius) the move's direction
        // scaled by it, as the original has it. States 0, 1 and 3 go to running; a destroyed one smokes.
        public void LateUpdateDoor()
        {
            UpdateAI();
            if (PowerState == 2)
            {
                MissionObjective objective = Objectives.CurrentObjectiveOf(SimMain.g_world, m_player.m_team);
                m_targetInfo.m_target = 0;
                m_steering["mech.autopilot"] = 0;
                if (objective != null && objective.m_type == 0x100 && objective.m_targetCount > 0 && (objective.m_targets[0] & 0xff00) == 0x100)
                {
                    int nav = objective.m_targets[0] & 0xff;
                    if (nav < SimMain.g_world.m_navs.Count)
                    {
                        Vector3Int at = SimMain.g_world.m_navs[nav].m_position;
                        int x = (int)m_positionX, y = (int)m_positionY, z = (int)m_positionZ;
                        int dx = at.x - x, dy = at.y - y, dz = at.z - z;
                        int step = Fixed.Div16((long)m_topSpeed * Clock.g_deltaTime, 0x697e98);
                        if (InRadius.IsWithinRadius(dx, dy, dz, step))
                        {
                            NormalizeVectorGuarded(ref dx, ref dy, ref dz);
                            dx = Fixed.Mul16(dx, step);
                            dy = Fixed.Mul16(dy, step);
                            dz = Fixed.Mul16(dz, step);
                        }
                        else
                        {
                            dx = at.x;
                            dy = at.y;
                            dz = at.z;
                        }

                        m_speed.m_target = dx;
                        m_throttle.m_target = dy;
                        m_turnRate.m_target = dz;
                    }
                }
            }

            switch (PowerState)
            {
                case 0:
                    PowerState = 2;
                    m_stateTime = 0;
                    break;
                case 1:
                case 3:
                    PowerState = 2;
                    break;
                case 4:
                    if (m_stateTime == 0) m_stateTime = Clock.g_currentClock + 0x712;
                    if (m_stateTime > Clock.g_currentClock) EmitWreckSmoke();
                    break;
            }
        }

        // NormalizeVectorGuarded: over the approximate length (16.16); a zero vector stays.
        public static void NormalizeVectorGuarded(ref int p_x, ref int p_y, ref int p_z)
        {
            int length = ApproxLen.ApproximateVectorLength(p_x, p_y, p_z);
            if (length <= 0) return;
            p_x = Fixed.Div16(p_x, length);
            p_y = Fixed.Div16(p_y, length);
            p_z = Fixed.Div16(p_z, length);
        }
    }
}
