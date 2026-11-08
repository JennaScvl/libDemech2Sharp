using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // A player's mech (demech2 MW2/src/mechclass.c: FirstMech, UpdateMech, LateUpdateMech, UpdateLocalMech; ramp.c;
    // config.c LoadMgdFile's values), every player's: its controls (its steering: the local player's input map sinks and
    // game keys, the others' the AI's: Ai.cs) set targets, which ramps ease toward, and each tick the mech's velocity eases
    // toward its speed along its heading on the ground, or flies under gravity, the air's drag and its jump jets' thrust;
    // it moves against the other mechs, buildings and scenery (MechCollision.cs), follows the ground (TerrainHeight), turns
    // at its turn rate, twists and tilts its torso and aims its arms (the pitch object) at where its weapons converge. Its
    // legs' animation follows the throttle (GPAnim.UpdateMotion through its MotionPlayer). It starts powered down and powers
    // up in 6 to 8 seconds; it heats up, fires its weapons (Weapons.cs), locks on, takes damage and is destroyed
    // (MechDamage.cs), and targets; the local player's may run on the autopilot and engage MASC. Artillery and doors (player
    // types 3 and 7) have their own updates (ClassTable.cs). Positions are MW2 units in the world's frame, speeds 16.16 units
    // a clock tick, angles 16.16 degrees. Demech2's Mech and the moving half of its Player in one: the game's side (its
    // objects' poses, parts and sounds' places) answers through m_host (IMechHost).
    public sealed partial class Mech
    {
        // The game's side of this mech: its objects, parts and animation (MechPilot).
        public IMechHost m_host;

        // SetViewMode's modes (the track and drop views).
        public const int c_viewTrack = 1, c_viewDrop = 4;

        public const int c_gravity = 0x794;     // g_gravity at 1 g
        public Player m_player;
        public TerrainHeight m_terrain;
        public Transform m_torsoObj;
        public Transform m_pitchObj;     // the arms
        public Transform m_eyeObj;   // Player::m_eyeObj
        public Transform m_firingObj;    // Player::m_firingObj
        public readonly Transform[] m_objects = new Transform[8]; // the hardpoints

        public IPlayerSteering m_steering;

        // Player::m_obj: the gamepiece record's object, the root of the mech's parts (the model's when it wasn't built).
        public Transform m_obj;

        // Player::m_type (c_playerType...).
        public int m_type;

        public int DebugTopSpeed => m_topSpeed; // TEMP: inert-mech diagnosis
        public int m_topSpeed;      // 16.16 units a tick per throttle unit
        public int m_height;        // the object's origin above the feet
        public int m_maxTorsoTwist;
        public int m_gravity = c_gravity;

        // m_cockpitHeight (the eyepoint is raised by the local mech's: g_eyeHeightOffset) and m_radius (splash damage
        // reaches it so much further), units.
        public int m_cockpitHeight;
        public int m_radius;

        public Ramp m_torsoTwist, m_speed, m_turnRate, m_torsoPitch, m_throttle;

        // Player::m_aimRange (eases toward m_aimDistance's) and m_aimDistance (the distance the weapons converge at).
        public Ramp m_aimRange, m_aimDistance;

        public long m_positionX, m_positionY, m_positionZ;    // units
        public int m_velocityX, m_velocityY, m_velocityZ;     // 16.16 units a tick
        public int m_newVelocityX, m_newVelocityY, m_newVelocityZ; // m_newVelocity: this tick's, before the collisions

        // m_collisionTicks: the ticks the mech has been colliding (0: moving freely).
        public int m_collisionTicks;

        // Player::m_collidedWith: the player its mech ran into this tick, or -1.
        public int m_collidedWith = -1;
        public int m_heading;              // 16.16 degrees
        public int m_headingSin, m_headingCos = 0x10000;
        public bool m_onGround;
        public int m_recenterLastHeading;

        // Mech::m_powerState: 0 off (about to start), 1 starting up, 2 running, 3 shut down, 4 destroyed, 5 ejecting,
        // 7 self-destructing; m_stateTime, when the state moves on (and the lock timer while running).
        // In a live networked match dead is sticky. While the player is out (flags 6, KillMech's) only a wreck
        // (4) or an ejection (5) is taken, anything else becomes 4, whatever writes it (a rewind's restore, a stale host
        // state, the AI, a power request); ReloadPlayerMech clears the flags first.
        public int PowerState
        {
            get => m_powerState;
            set => m_powerState = Host.Net.Active && (PlayerFlags & 6) != 0 && value != 4 && value != 5 ? 4 : value;
        }

        public int m_powerState;
        public int m_stateTime;

        // g_powerRequest: 1 power up, -1 shut down (the startup and shutdown keys): the mech's own (a pilot's command),
        // as are the other requests below.
        public int m_powerRequest;

        // g_jettisonAmmoRequested (JETTISON_AMMO), g_toggleMascRequested (TOGGLE_MASC), PUNCH_IN_AUTO_HDG, the
        // shutdown override's key (Cockpit.OverrideShutdown's), g_feetToTorso (FEET_TO_TORSO).
        public bool m_jettisonAmmoRequested, m_toggleMascRequested, m_punchInRequested, m_overrideShutdownRequested, m_feetToTorso;

        // g_autoEject as a networked match has it: the pilot's own (SimMain.g_autoEject elsewhere).
        public bool m_autoEject;

        // g_ejectStarted, g_localMechDestroyed: UpdateLocalMech's, the mech's own.
        public bool m_ejectStarted, m_localMechDestroyed;

        // Woken from shutdown or rest by EnterAIState; cleared once it finishes powering up.
        public bool m_wokeFromShutdown;

        // Mech::m_flags: 1 a trigger is held, 4 overheating, 8 shutdown overridden, 0x10 MASC, 0x20 a leg lost, 0x40
        // locking, 0x80 locked, 0x100 knocked over, 0x200 done, 0x1000 overheat warned, 0x2000 the torso pitch follows,
        // 0x4000 moving a weapon between groups, 0x8000 the target in the lock cone.
        public int m_flags;

        // Player::m_flags: 2 and 4 destroyed, 0x10 restarting, 0x2000 powered up (the gpspec's flags with them).
        public int PlayerFlags
        {
            get => m_player.m_flags;
            set => m_player.m_flags = value;
        }

        // m_heat (16.16 percent), m_deltaHeat (heat added this tick), m_cooling (per tick).
        public int m_heat;
        public int m_deltaHeat;
        public int m_cooling;

        public int m_autopilot;
        public int m_selectedWeapon;
        public int m_lastSelectedWeapon;

        // The host's data for this mech, or null.
        public object m_ext;

        // m_mobility: 16.16, scales the throttle; each leg hit takes 0.1.
        public int m_mobility = 0x10000;

        // The configuration (LoadMechConfig): weapons, sections, cooling, jump jets; null without one.
        public MechConfig m_config;

        // m_jumpFuel: ticks of jets left, -1 or -2 without jump jets; m_jumpJets, m_jumpThrust.
        public int m_jumpFuel;
        public int m_jumpJets;
        public int m_jumpThrust;

        // m_speed's value (16.16 units a tick, negative backing up), the velocity's length (16.16), m_height and the
        // ground under the mech (Player::m_groundHeight), units.
        public int SpeedValue => m_speed != null ? m_speed.m_value : 0;
        public int VelocityLength => ApproxLen.ApproximateVectorLength(m_velocityX, m_velocityY, m_velocityZ);
        public Vector3Int Velocity => new Vector3Int(m_velocityX, m_velocityY, m_velocityZ);
        public int Height => m_height;
        public int m_groundHeight;
        public Vector3Int Position => new Vector3Int((int)m_positionX, (int)(m_positionY - m_height), (int)m_positionZ);
        public int ObjectY => (int)m_positionY; // Player::m_position's y: the mech object's, m_height above its feet

        // Player::m_position: the object, units.
        public Vector3Int ObjectPosition => new Vector3Int((int)m_positionX, (int)m_positionY, (int)m_positionZ);

        // The torso's twist and pitch, 16.16 degrees.
        public int TorsoTwist => m_torsoTwist != null ? m_torsoTwist.m_value : 0;
        public int TorsoPitch => m_torsoPitch != null ? m_torsoPitch.m_value : 0;
        public int HeadingFixed => m_heading;

        public bool IsLocal => m_player.m_index == SimMain.g_localPlayerId;

        // The mech a human flies (the local player's in single player, every match slot's in a networked match). The
        // sim's gameplay branches test this, the same on every machine; IsLocal is only this machine's view, sounds and
        // records.
        public bool IsPilot => SimMain.IsPilot(m_player.m_index);

        // g_reticleTargeting and g_aimedShape (Targeting, UpdateAimDistance): the pilot's own (Combat.AimedShape is the
        // local one's, for the cockpit).
        public bool m_reticleTargeting;
        public Shape m_aimedShape;

        // g_feetToTorso, the mech's own.
        public bool FeetToTorsoActive => m_feetToTorso;

        // The torso's twist limit (16.16), for the lockstep input's clamp.
        public int MaxTorsoTwist => m_maxTorsoTwist;

        // The torso turns freely when the host says so (no twist limit), the controls spinning it (InputMap.FreeTorsoPan)
        // and the torso taking the shortest way to the steering's angle.
        public bool FreeTorsoTwist => Host.Mechs.FreeTorsoTwist(this);

        // An angle difference (16.16 degrees) within half a turn.
        public static int WrapTwist(int p_delta)
        {
            while (p_delta > 0xb40000) p_delta -= 0x1680000;
            while (p_delta < -0xb40000) p_delta += 0x1680000;
            return p_delta;
        }

        // A networked player gone mid-match: the self-destruct the key starts (LateUpdateMech), forced whatever the
        // power state, but a wreck's.
        public void ForceSelfDestruct()
        {
            if (PowerState == 4 || (PlayerFlags & 6) != 0) return;
            m_stateTime = Clock.g_currentClock + 0x16a;
            PowerState = 7;
        }
        public bool Running => PowerState == 2;

        // A mech shut down mid-jump slides on its jets' speed (the original moves a mech that isn't running or dead
        // without collisions); with the host's ShutdownStopsJets it slows as if the jets' key were let up, and collides
        // while shut down or powering up.
        public static bool ShutdownStopsJets => Host.Mechs.ShutdownStopsJets;

        // Player::m_killer, m_inspectedBy.
        public int m_killer = -1;
        public int m_inspectedBy;

        // Player::m_targetInfo.
        public readonly PlayerTargetInfo m_targetInfo = new PlayerTargetInfo();

        // FirstMech: the ramps, the weapon, heat and power state, the velocities, the steering, the pose, the AI
        // (InitializeAI); the top speed scaled down by the planet's gravity (g_gravityScale).
        public void FirstMech()
        {
            m_torsoTwist = new Ramp(0, IsPilot ? 0.2 : 0.6);
            m_speed = new Ramp(0, 0.2);
            m_turnRate = new Ramp(0, 0.3);
            m_torsoPitch = new Ramp(0, 0.2);
            m_throttle = new Ramp(0x400, 0.2);
            m_selectedWeapon = 0;
            m_lastSelectedWeapon = 0;
            m_heat = m_deltaHeat = 0;
            m_collisionTicks = 0;
            m_flags |= 0x2000;
            PowerState = 0;
            m_stateTime = 0;
            m_autopilot = 0;
            m_velocityX = m_velocityY = m_velocityZ = 0;
            m_newVelocityX = m_newVelocityY = m_newVelocityZ = 0;
            m_mobility = 0x10000;
            m_powerRequest = 0;
            m_jettisonAmmoRequested = m_toggleMascRequested = m_punchInRequested = m_overrideShutdownRequested = m_feetToTorso = false;
            m_mascEngaged = false;
            UpdateHeadingTrig();
            m_host.PoseMech(true);
            m_steering["mech.autopilot"] = 0;
            m_steering["drive.throttle"] = 0;
            m_steering["nav.next"] = 0;
            InitializeAI();
            m_topSpeed = Fixed.Div16(m_topSpeed, Fixed.Div16(m_gravity, c_gravity));
        }

        // The offset from the mech to the eye (dx, dy, dz as LateUpdateMech passes PlaySoundAt), units.
        public Vector3 EyeOffset() => Host.Audio.EyeUnits - new Vector3(m_positionX, m_positionY - m_height, m_positionZ);

        public void UpdateHeadingTrig()
        {
            m_headingSin = Fixed.Sin(m_heading);
            m_headingCos = Fixed.Cos(m_heading);
        }

        // ---- UpdateMech -------------------------------------------------------------------------------------------

        // The player type's update (PlayerType::m_updateFn): artillery's and doors' own, every other type's UpdateMech.
        public void UpdatePlayer()
        {
            switch (m_type)
            {
                case 3:
                    UpdateArtillery();
                    return;
                case 7:
                    UpdateDoor();
                    return;
            }

            UpdateMech();
        }

        // UpdateMech: the ramps; the velocity toward the speed along the heading on the ground (unless the jets fire),
        // else under gravity and the air's drag on the jump jets' horizontal speed; the jets' thrust (lifting, or pushing
        // sideways, forwards or backwards while one of those fires); the move against the mechs, buildings and scenery
        // (MoveMechWithCollisions: twice, bouncing harder when the second move hits too; the collision's sound, shake and
        // damage, or a slope it climbs); the ground (landing, falling damage, the slope's pull on the speed), the heading,
        // then the pose; for the local player the aim distance (UpdateAimDistance), and the arms aimed where the weapons
        // converge.
        public void UpdateMech()
        {
            if ((PowerState == 4 && m_onGround) || (m_flags & 0x100) != 0) return;
            bool resim = g_resimulating;
            // A networked client: the host's reported heat joins the step's.
            if (!resim)
            {
                m_deltaHeat = m_pendingNetHeat;
                m_pendingNetHeat = 0;
            }
            bool isLocal = IsLocal;
            int dt = Clock.g_deltaTime;
            int heightAbove = 0;
            Shape hitShape = null;
            Mech hitPlayer = null;

            if ((PlayerFlags & 1) == 0)
            {
                if (!m_host.TakeLiveTorso())
                {
                    m_torsoTwist.UpdateRamp();
                    m_torsoPitch.UpdateRamp();
                }

                m_turnRate.UpdateRamp();
                m_throttle.UpdateRamp();
                m_speed.UpdateRamp();

                int vx = m_velocityX, vy = m_velocityY, vz = m_velocityZ;
                int accelX = 0, accelY = -m_gravity, accelZ = 0;
                int dragX = 0, dragZ = 0;
                // ShutdownStopsJets: a mech that isn't running drifts as if the jets' key were let up.
                bool jets = m_steering["jets.on"] != 0 && !(ShutdownStopsJets && PowerState != 2);
                if (m_onGround && (!jets || m_jumpFuel <= 0))
                {
                    int s = m_speed.m_value;
                    if (s < 0x20 && s > -0x20) s = 0;
                    int targetX = Fixed.Mul16(s, m_headingSin), targetZ = Fixed.Mul16(s, m_headingCos);
                    if (dt < 0x2d)
                    {
                        accelX = (targetX - m_velocityX) / 0x2d;
                        accelZ = (targetZ - m_velocityZ) / 0x2d;
                    }
                    else if (dt > 0)
                    {
                        accelX = (targetX - m_velocityX) / dt;
                        accelZ = (targetZ - m_velocityZ) / dt;
                    }
                }
                else
                {
                    int length = ApproxLen.ApproximateVectorLength(vx, 0, vz);
                    int top = Fixed.MulDiv64(0x1400, m_topSpeed, 2);
                    if (length > 0 && top > 0 && m_jumpThrust > 0)
                    {
                        int top2 = Fixed.Mul16(top, top);
                        int a = m_jumpThrust - Fixed.Div16(m_jumpThrust, top) / 3;
                        int c = top2 - top * 3 / 4;
                        a = Fixed.Div16(a, c);
                        int b = m_jumpThrust - Fixed.Mul16(a, top2);
                        b = Fixed.Div16(b, top);
                        int airDrag = -Fixed.Mul16(b + Fixed.Mul16(a, length), length);
                        dragX = Fixed.MulDiv64(airDrag, vx, length);
                        dragZ = Fixed.MulDiv64(airDrag, vz, length);
                    }
                }

                int dx = 0, dy = 0, dz = 0;
                if (jets && m_jumpFuel > 0 && PowerState == 2)
                {
                    vx = m_velocityX;
                    vz = m_velocityZ;
                    int drag = 0;
                    int liftDrag = Host.Mechs.LiftDrag;
                    if (vy > 0) drag = Fixed.Mul16((m_gravity - m_jumpThrust) * liftDrag, vy) / Host.World.JumpJetDrag;
                    accelY += m_jumpThrust + drag;
                    if (m_steering["turn"] != 0)
                    {
                        accelY = vy <= 0 ? 0 : -m_gravity;
                    }
                    else if (m_steering["jets.left"] != 0)
                    {
                        accelY = vy <= 0 ? 0 : -m_gravity;
                        accelX += -Fixed.Mul16(m_headingCos, m_jumpThrust) + dragX;
                        accelZ += dragZ + Fixed.Mul16(m_headingSin, m_jumpThrust);
                    }
                    else if (m_steering["jets.right"] != 0)
                    {
                        accelY = vy <= 0 ? 0 : -m_gravity;
                        accelX += dragX + Fixed.Mul16(m_headingCos, m_jumpThrust);
                        accelZ += -Fixed.Mul16(m_headingSin, m_jumpThrust) + dragZ;
                    }
                    else if (m_steering["jets.fore"] != 0)
                    {
                        accelY = vy <= 0 ? 0 : -m_gravity;
                        accelX += dragX + Fixed.Mul16(m_headingSin, m_jumpThrust);
                        accelZ += dragZ + Fixed.Mul16(m_headingCos, m_jumpThrust);
                    }
                    else if (m_steering["jets.aft"] != 0)
                    {
                        accelY = vy <= 0 ? 0 : -m_gravity;
                        accelX += -Fixed.Mul16(m_headingSin, m_jumpThrust) + dragX;
                        accelZ += -Fixed.Mul16(m_headingCos, m_jumpThrust) + dragZ;
                    }
                }
                else if (vy == 0 && m_onGround) accelY = 0;

                IntegrateMidpoint(ref dx, ref vx, accelX, dt);
                IntegrateMidpoint(ref dy, ref vy, accelY, dt);
                IntegrateMidpoint(ref dz, ref vz, accelZ, dt);
                if (vx < 0x1000 && vx > -0x1000) vx = 0;
                if (vz < 0x1000 && vz > -0x1000) vz = 0;

                m_newVelocityX = vx;
                m_newVelocityY = vy;
                m_newVelocityZ = vz;
                int x = (int)m_positionX, y = (int)m_positionY, z = (int)m_positionZ;
                m_collidedWith = -1;

                if (MoveMechWithCollisions(ref hitShape, ref hitPlayer, dx, dy, dz, ref x, ref y, ref z) != 0)
                {
                    dx = x - (int)m_positionX;
                    dy = y - (int)m_positionY;
                    dz = z - (int)m_positionZ;
                    vx = m_velocityX;
                    vy = m_velocityY;
                    vz = m_velocityZ;
                    int saved = m_collisionTicks;
                    Shape hitShape2 = null;
                    int x2 = 0, y2 = 0, z2 = 0;
                    if (MoveMechWithCollisions(ref hitShape2, ref hitPlayer, dx, dy, dz, ref x2, ref y2, ref z2) != 0)
                    {
                        if (hitShape2 != hitShape)
                        {
                            x = (int)m_positionX;
                            y = (int)m_positionY;
                            z = (int)m_positionZ;
                        }

                        vx = m_velocityX = vx * 2;
                        vy = m_velocityY = vy * 2;
                        vz = m_velocityZ = vz * 2;
                    }

                    m_collisionTicks = saved;
                    if (PowerState == 4)
                    {
                        if (!resim) KnockMechOver();
                        m_onGround = true;
                        return;
                    }

                    m_groundHeight = GetTerrainHeight(x, y, z, out m_groundNormal);
                    heightAbove = y - m_height - m_groundHeight;
                    if (g_segmentNormal.y > 0xb505 && heightAbove < 1000 && heightAbove > -10000)
                    {
                        m_collisionTicks = 0;
                        heightAbove = -1;
                    }
                    else
                    {
                        if (isLocal && !resim && Eyepoint.g_inCockpitView && !g_collisionSoundPlayed)
                        {
                            g_collisionSoundPlayed = true;
                            int impact = ApproxLen.ApproximateVectorLength(m_newVelocityX, m_newVelocityY, m_newVelocityZ);
                            if (impact > 200000)
                            {
                                int sound;
                                if (hitPlayer != null) sound = 0xf0;
                                else if (hitShape != null && (hitShape.Kind & 0x200) != 0) sound = 200;
                                else sound = 0xe5;
                                int volume = impact;
                                if (volume > 1500000) volume = 1500000;
                                volume = Fixed.MulDiv64(200, volume, 1500000);
                                CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(sound), volume, 0x40, 5, 0x32);
                                CameraShake.PlayPlayerHitFeedback(g_segmentNormal, Host.Fx.RenderTicks);
                            }
                        }

                        if (resim) { }
                        else if (hitPlayer != null) DamageMechsInCollision(hitPlayer);
                        else DamageMechHittingShape(hitShape);
                    }
                }
                else
                {
                    if (isLocal) g_collisionSoundPlayed = false;
                    m_groundHeight = GetTerrainHeight(x, y, z, out m_groundNormal);
                    heightAbove = y - m_height - m_groundHeight;
                }

                m_onGround = false;
                MotionPlayer motion = m_host.Motion;
                if (heightAbove > m_height)
                {
                    if (motion != null)
                    {
                        motion.m_animFlags &= ~1u;
                        motion.m_animFlags |= 4;
                    }

                    Host.Mechs.Airborne(this);
                }
                else
                {
                    if (motion != null) motion.m_animFlags &= ~4u;
                    Host.Mechs.Grounded(this, resim);
                    if (heightAbove <= 0)
                    {
                        m_onGround = true;
                        if (heightAbove < 0)
                        {
                            if (vy < -0x56276 && !resim)
                            {
                                if (PowerState == 4)
                                {
                                    KnockMechOver();
                                    return;
                                }

                                PlayMechLanding(vy);
                            }

                            if (vy < -0x102762 && SimMain.g_difficulty.m_collisionDamage && !resim)
                            {
                                int damage = Fixed.Div16(vy + 0x102762, -0x204ec4) * 50;
                                // As written, the local player's; in a match the mech's own (the same everywhere).
                                int faller = Host.Net.NetRules ? m_player.m_index : SimMain.g_localPlayerId;
                                ApplyDamage(faller, damage, 8);
                                ApplyDamage(faller, damage, 7);
                            }

                            m_velocityY = vy = 0;
                            y = m_groundHeight + m_height;
                        }
                    }
                }

                if ((m_groundNormal.x != 0 || m_groundNormal.z != 0) && m_onGround)
                {
                    int slope = Fixed.Mul16(m_headingCos, m_groundNormal.z) + Fixed.Mul16(m_headingSin, m_groundNormal.x);
                    int slide = Host.World.SlideSlope;
                    if (slope > slide || slope < -slide) m_speed.m_value += Fixed.Mul16(Fixed.Mul16(m_gravity, slope), dt);
                }

                SeparateFromMechs(dt, ref x, y, ref z);
                m_positionX = x;
                y = Host.Mechs.PinnedHeight(this, y);
                m_positionY = y;
                m_positionZ = z;
                m_heading += m_turnRate.m_value * dt / 0xb5;
                m_heading %= 0x1680000;
                m_velocityX = vx;
                m_velocityY = vy;
                m_velocityZ = vz;
                m_host.PoseMech(false);
                if (IsPilot) UpdateAimDistance();
                m_host.AimPitchObject();
                // UpdateMech sets the torso after the pitch object: one object for both (Tarantula's THNGs) keeps the twist.
                if (m_pitchObj == m_torsoObj) m_host.PoseTorso();
            }

            UpdateHeadingTrig();
        }

        // g_collisionSoundPlayed: the local mech's collision was heard (until it moves freely again).
        public static bool g_collisionSoundPlayed;

        // The ground under the point (GetTerrainHeight, whole units) and its normal (g_groundNormal, 16.16; zero where
        // the ground has none).
        public int GetTerrainHeight(int p_x, int p_y, int p_z, out Vector3Int p_normal)
        {
            if (m_terrain == null)
            {
                p_normal = Vector3Int.zero;
                return 0;
            }

            long ground = m_terrain.GetTerrainHeight(FixVec.FromUnits(p_x, p_y, p_z));
            FixVec normal = m_terrain.GroundNormal;
            p_normal = new Vector3Int((int)normal.X, (int)normal.Y, (int)normal.Z);
            return (int)(ground >> 16);
        }

        public Vector3Int m_groundNormal;

        // ---- LateUpdateMech ----------------------------------------------------------------------------------------

        // The player type's late update (PlayerType::m_lateUpdateFn): artillery's and doors' own, every other type's
        // LateUpdateMech.
        public void LateUpdatePlayer()
        {
            switch (m_type)
            {
                case 3:
                    LateUpdateArtillery();
                    return;
                case 7:
                    LateUpdateDoor();
                    return;
            }

            LateUpdateMech();
        }

        // LateUpdateMech: the weapons, the AI (UpdateAI), then while running the target keys, the target and the lock,
        // the autopilot's toggle and steering (RunAutopilot, for the user's mech), the user's turn (the legs' input unless
        // the autopilot steers) and the throttle's target; the animation; the jump jets (granted by a cheat; their fuel
        // burns while they fire, and refills at a quarter of that rate; firing they heat the mech, show their flames and
        // turn it twice as fast); else the turn rate (90 degrees a second, less with speed), feet to torso, the speed
        // (half and backwards when reversing; a stride eases it, and engaged MASC adds half and may fail), the torso, the
        // throttle's heat; powered down, everything to rest; the heat; the power state.
        public void LateUpdateMech()
        {
            if ((m_flags & 0x200) != 0) return;
            bool isLocal = IsLocal;
            bool active = (PlayerFlags & 1) == 0;
            MotionPlayer motion = m_host.Motion;
            bool jumping = false;

            if (PowerState == 2)
            {
                UpdateWeaponFireState();
                Host.Weapons.AfterFireState(this);
            }

            UpdateAI();

            if (PowerState == 2)
            {
                if (m_steering.Pressed("mech.selfdestruct"))
                {
                    m_stateTime = Clock.g_currentClock + 0x16a;
                    PowerState = 7;
                    if (isLocal) MissionSpeech.PlayCockpitSound(4, -1);
                    m_steering["mech.selfdestruct"] = 0;
                }

                int previousTarget = m_targetInfo.m_target;
                bool selecting = RunTargetKeys();
                if ((m_targetInfo.m_target & 0x1000) == 0) Targeting.UpdateTarget(this);

                // Not the original's: the local player's explicit selection of a new target chirps (MECHUDON). A target
                // lost or destroyed sets 0x1000 and doesn't.
                if (selecting && isLocal && m_targetInfo.m_target != previousTarget && (m_targetInfo.m_target & 0x1000) == 0)
                    CockpitSound.PlaySoundEffect(Host.Sound.FindSoundByName("MECHUDON"), 100, 0x40, 5, 0x50);
                UpdateWeaponLock();

                if ((m_steering["drive.steer"] != 0 || m_feetToTorso) && m_autopilot != 0) m_steering["mech.autopilot"] = 1;
                if (m_steering.Pressed("mech.autopilot"))
                {
                    if (m_autopilot == 0)
                    {
                        m_autopilot = 1;
                        if (isLocal) MissionSpeech.PlayCockpitSound(0x1d, 1);
                    }
                    else
                    {
                        m_autopilot = 0;
                        if (isLocal) MissionSpeech.PlayCockpitSound(0x1d, 2);
                    }
                }

                m_steering["mech.autopilot"] = 0;
                if (m_player.m_aiMode == 0 && m_autopilot == 0) m_steering["turn"] = m_steering["drive.steer"];
                if (m_player.m_aiMode == 0) RunAutopilot();

                m_throttle.m_target = Fixed.Mul16(m_steering["drive.throttle"], m_mobility) + 0x400;
                if (m_player.m_aiMode != 2 && m_throttle.m_target == 0x400 && (m_steering["turn"] != 0 || m_feetToTorso))
                {
                    m_throttle.m_target = 0x480;
                }
            }

            if (motion != null)
            {
                motion.m_throttle = m_throttle.m_value;
                motion.m_reverse = m_steering["reverse"] != 0;
                GPAnim.UpdateMotion(motion);
            }

            if (m_jumpFuel == -2 && m_steering.Pressed("grant_jump_jets"))
            {
                m_jumpFuel = 0x712;
                m_jumpJets = 3;
                m_jumpThrust = m_gravity * 3;
            }

            if (m_jumpFuel >= 0)
            {
                if (m_steering["jets.on"] != 0 && PowerState == 2)
                {
                    jumping = true;
                    if (!g_infiniteJumpFuel || !IsPilot || Host.Net.NetRules) m_jumpFuel -= Clock.g_deltaTime;
                    if (m_jumpFuel <= 0) m_jumpFuel = 0;
                }
                else
                {
                    // g_deltaTime / 4 a frame. Not in the original: the remainder carries, so a 6-tick step refills 1.5
                    // on average (the original's 1/4 per tick) rather than truncating to 1.
                    if (m_jumpFuel < 0x712)
                    {
                        m_jumpFuelRefillTicks += Clock.g_deltaTime;
                        m_jumpFuel += m_jumpFuelRefillTicks / 4;
                        m_jumpFuelRefillTicks %= 4;
                    }
                    else m_jumpFuel = 0x712;
                }
            }

            if (jumping)
            {
                int turn = 0;
                if (m_jumpFuel > 0)
                {
                    FireJumpJetEffects();
                    m_deltaHeat += m_jumpJets * Clock.g_deltaTime * 0x180;
                    if (motion != null) motion.m_animFlags &= ~5u;
                    turn = m_steering["turn"] / 1024 * 90;
                }

                m_turnRate.m_target = turn * 2;
            }
            else if ((motion == null || (motion.m_animFlags & 4) == 0) && active)
            {
                int kmh = m_speed.m_value / 10000;
                if (kmh != 0 || (IsPilot && m_mobility > 0))
                {
                    if (kmh > 0x50) kmh = 0x50;

                    int rate = m_steering["turn"] / 1024 * 90;
                    m_turnRate.m_target = (int)((long)rate * Fixed.Cos(kmh << 16) / 65536);
                }
                else m_turnRate.m_target = 0;
            }
            else if (m_autopilot == 0) m_turnRate.m_target = 0;

            if (IsPilot && m_feetToTorso) FeetToTorso();

            if (active)
            {
                int t = m_throttle.m_value - 0x400;
                if (t < 0x20) t = 0;
                m_speed.m_target = m_topSpeed * t;
                if (m_speed.m_target != 0 && motion != null && motion.m_motionState == 2) m_speed.m_target = -(m_speed.m_target >> 1);
                if (!jumping && motion != null && (motion.m_animFlags & 2) != 0)
                {
                    if (m_mascEngaged && IsPilot && Clock.g_currentClock - m_lastMascRoll > 0xb5)
                    {
                        m_lastMascRoll = Clock.g_currentClock;
                        if (SimRandom.RandomIntBelow(0x3c) == 12 && !SimMain.g_difficulty.m_invulnerable && PowerState == 2)
                        {
                            if (IsLocal)
                            {
                                TimedOverlays.ShowInGameMessage("MASC malfunction.", 1, 0x16a, 0x32);
                                CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xc9), 100, 0x40, 5, 0x50);
                            }
                            m_heat += Host.Mechs.MascHeat(m_heat);
                            m_mascEngaged = false;
                        }
                    }

                    m_speed.m_duration = 0x5a;
                    if (m_mascEngaged && IsPilot) m_speed.m_target += m_speed.m_target >> 1;
                }

                // g_feetToTorso (the local player's) holds every mech's twist, as the original's test does (any pilot's).
                if (!SimMain.AnyFeetToTorso) m_torsoTwist.m_target = m_steering["torso.yaw"];
                if (IsLocal) Host.Input.FreeTorsoPan = FreeTorsoTwist;
                if (FreeTorsoTwist) m_torsoTwist.m_value = m_torsoTwist.m_target - WrapTwist(m_torsoTwist.m_target - m_torsoTwist.m_value);
                else if (m_torsoTwist.m_target > m_maxTorsoTwist)
                {
                    m_torsoTwist.m_target = m_maxTorsoTwist;
                    m_steering["torso.yaw"] = m_maxTorsoTwist;
                    m_steering["torso.yaw.hold"] = 1;
                }
                else if (m_torsoTwist.m_target < -m_maxTorsoTwist)
                {
                    m_torsoTwist.m_target = -m_maxTorsoTwist;
                    m_steering["torso.yaw"] = -m_maxTorsoTwist;
                    m_steering["torso.yaw.hold"] = 1;
                }

                m_torsoPitch.m_target = m_steering["torso.pitch"];
                m_deltaHeat += (int)((long)(m_throttle.m_value - 0x400) * m_cooling / 0x2800);
            }

            if (PowerState != 2)
            {
                if (motion != null) GPAnim.StopMotion(motion);
                m_throttle.m_target = 0x400;
                m_speed.m_target = m_torsoTwist.m_target = m_turnRate.m_target = m_torsoPitch.m_target = 0;
                m_steering["drive.throttle"] = 0;
                m_steering["drive.throttle.hold"] = 1;
                foreach (string sink in new[]
                         {
                             "jets.on", "jets.left", "jets.right", "jets.fore",
                             "jets.aft", "arms.fire", "arms.next", "drive.steer.dec", "drive.steer.inc",
                             "reverse", "nav.next", "mech.autopilot",
                         })
                {
                    m_steering[sink] = 0;
                }

                if (IsPilot) ReleaseWeaponTriggers();
            }

            CalculateHeat();

            switch (PowerState)
            {
                case 7:
                    if (m_stateTime < Clock.g_currentClock) EjectPlayer(false);
                    break;
                case 0:
                    m_stateTime = Clock.g_currentClock + SimRandom.RandomIntBelow(0x16a) + 0x43e;
                    PowerState = 1;
                    PlayerFlags &= ~0x2000;
                    if (SimMain.PlayerSide(m_player.m_index) == 1)
                    {
                        Player local = SimMain.PlayerOf(SimMain.g_localPlayerId);
                        if (local != null && (local.m_flags & 0x2000) != 0)
                        {
                            EffectSound.PlaySoundAt(EyeOffset(), Host.Anim.MapSoundId(0x104), Eyepoint.g_inCockpitView);
                        }
                    }
                    else EffectSound.PlaySoundAt(EyeOffset(), Host.Anim.MapSoundId(0xf7), !Eyepoint.g_inCockpitView);

                    break;
                case 1:
                    // Not in the original: a client's regenerated mech powers up on the host's word (AwaitHostPowerUp).
                    if (m_stateTime < Clock.g_currentClock && !m_awaitHostPowerUp)
                    {
                        PowerState = 2;
                        PlayerFlags |= 0x2000;
                        SimMain.g_mechPoweredUp = true;

                        // Not in the original: an enemy the AI wakes from shutdown has its 0x10 cleared before it
                        // starts up, so case 2's "Enemy power-up detected" never plays for it. Say it here instead.
                        if (m_wokeFromShutdown)
                        {
                            m_wokeFromShutdown = false;
                            Player local = SimMain.PlayerOf(SimMain.g_localPlayerId);
                            if (SimMain.PlayerSide(m_player.m_index) == 1 && local != null && (local.m_flags & 0x2000) != 0)
                            {
                                MissionSpeech.PlayCockpitSound(0x1a, -1);
                            }
                        }
                    }

                    break;
                case 2:
                    if ((PlayerFlags & 0x10) != 0)
                    {
                        PlayerFlags &= ~0x10;
                        PlayerFlags |= 0x2000;
                        Player local = SimMain.PlayerOf(SimMain.g_localPlayerId);
                        if (SimMain.PlayerSide(m_player.m_index) == 1 && local != null && (local.m_flags & 0x2000) != 0)
                        {
                            MissionSpeech.PlayCockpitSound(0x1a, -1);
                        }
                    }

                    break;
                case 3:
                    PlayerFlags |= 0x10;
                    Host.Mechs.ShutDownTick(this);
                    break;
                case 4:
                    PlayerFlags &= ~0x2000;
                    if (m_stateTime == 0)
                    {
                        EmitWreckSmoke();
                        m_stateTime = Clock.g_currentClock + 0x712;
                    }

                    if (m_stateTime > Clock.g_currentClock) EmitWreckSmoke();
                    else m_flags |= 0x200;
                    break;
            }

            // The torso's servos, while it turns more than 2 degrees from its target, heard in the cockpit.
            if (isLocal && (m_torsoTwist.m_value + 0x20000 < m_torsoTwist.m_target || m_torsoTwist.m_value - 0x20000 > m_torsoTwist.m_target) &&
                Eyepoint.g_inCockpitView)
            {
                EffectSound.PlaySoundAt(EyeOffset(), Host.Anim.MapSoundId(0x13c), Eyepoint.g_inCockpitView);
            }

            if (!IsPilot) PlayerFlags &= ~1;
        }

        // The target keys of a running mech (LateUpdateMech): each cycles a kind of target once and is cleared. The
        // reset keys clear the "previous" key, as the original does. Returns whether any was pressed.
        public bool RunTargetKeys()
        {
            IPlayerSteering s = m_steering;
            bool pressed = false;
            if (s.Pressed("nav.next"))
            {
                pressed = true;
                Targeting.CycleNavTarget(this, 1, false);
                s["nav.next"] = 0;
            }

            if (s.Pressed("nav.prev"))
            {
                pressed = true;
                Targeting.CycleNavTarget(this, -1, false);
                s["nav.prev"] = 0;
            }

            if (s.Pressed("nav.clear"))
            {
                pressed = true;
                Targeting.CycleNavTarget(this, 0, false);
                s["nav.clear"] = 0;
            }

            if (s.Pressed("lock.next"))
            {
                pressed = true;
                Targeting.CycleTarget(this, 1, 8);
                s["lock.next"] = 0;
            }

            if (s.Pressed("lock.prev"))
            {
                pressed = true;
                Targeting.CycleTarget(this, -1, 8);
                s["lock.prev"] = 0;
            }

            if (s.Pressed("lock.clear"))
            {
                pressed = true;
                Targeting.CycleTarget(this, 0, 8);
                s["lock.prev"] = 0;
                s["lock.clear"] = 0;
            }

            if (s.Pressed("lock.nearest"))
            {
                pressed = true;
                Targeting.TargetNearestEnemy(this);
                s["lock.nearest"] = 0;
            }

            if (s.Pressed("lock.friend"))
            {
                pressed = true;
                Targeting.CycleFriendlyTarget(this, 1);
                s["lock.friend"] = 0;
            }

            if (s.Pressed("lock.lastshot")) s["lock.lastshot"] = 0;

            if (s.Pressed("next_objective"))
            {
                pressed = true;
                Targeting.CycleTarget(this, 1, 0x10008);
                s["next_objective"] = 0;
            }

            if (s.Pressed("thing.next"))
            {
                pressed = true;
                Targeting.CycleGameThingTarget(this, 1);
                s["thing.next"] = 0;
            }

            if (s.Pressed("thing.prev"))
            {
                pressed = true;
                Targeting.CycleGameThingTarget(this, -1);
                s["thing.prev"] = 0;
            }

            if (s.Pressed("thing.clear"))
            {
                pressed = true;
                Targeting.CycleGameThingTarget(this, 0);
                s["thing.prev"] = 0;
                s["thing.clear"] = 0;
            }

            if (s.Pressed("piece.next"))
            {
                pressed = true;
                Targeting.CycleGamePieceTarget(this, 1);
                s["piece.next"] = 0;
            }

            if (s.Pressed("piece.prev"))
            {
                pressed = true;
                Targeting.CycleGamePieceTarget(this, -1);
                s["piece.prev"] = 0;
            }

            if (s.Pressed("piece.clear"))
            {
                pressed = true;
                Targeting.CycleGamePieceTarget(this, 0);
                s["piece.prev"] = 0;
                s["piece.clear"] = 0;
            }

            if (s.Pressed("lock.reticle"))
            {
                pressed = true;
                Targeting.TargetAtReticle(this);
                s["lock.reticle"] = 0;
            }

            return pressed;
        }

        // g_feetToTorso: the legs turn toward the torso at 55 degrees a second, the twist taking back what they turned,
        // until it's within 4 degrees; then both stop and the torso's input recenters.
        public void FeetToTorso()
        {
            int h = Wrap(m_heading), last = Wrap(m_recenterLastHeading);
            if (h >> 16 > 90 && last >> 16 < -90) h = -(0x1680000 - h);
            else if (last >> 16 > 90 && h >> 16 < -90) h += 0x1680000;
            int delta = h - last;
            if (m_torsoTwist.m_value > -0x40000 && m_torsoTwist.m_value < 0x40000)
            {
                m_turnRate.m_value = m_turnRate.m_target = 0;
                m_torsoTwist.m_value = m_torsoTwist.m_target = 0;
                m_steering["torso.yaw.center"] = 1;
                m_steering["torso.yaw"] = 0;
                m_feetToTorso = false;
            }
            else
            {
                m_turnRate.m_target = m_torsoTwist.m_value > 0 ? 0x370000 : -0x370000;
                if (delta != 0) m_torsoTwist.m_value = m_torsoTwist.m_target = m_torsoTwist.m_value - delta;
            }

            m_recenterLastHeading = m_heading;
        }

        public static int Wrap(int p_angle)
        {
            if (p_angle >> 16 > 180) return -(0x1680000 - p_angle);
            if (p_angle >> 16 < -180) return p_angle + 0x1680000;
            return p_angle;
        }

        // ---- UpdateLocalMech --------------------------------------------------------------------------------------

        // UpdateLocalMech: an ejecting mech (5) waits, then the mission is lost; a destroyed one (4) flashes ZAPPED once
        // and the mission is lost; a running one cycles its weapons and groups, jettisons ammunition and toggles its MASC
        // (when equipped, flag 0x10). Then, unless destroyed or ejecting, a power request shuts the mech down ("Shutting
        // down" and its sound), or powers a shut-down one up ("Powering up...") unless it overheats without the override.
        // The ejecting mech's camera is the drop view, the destroyed one's the track view. Artillery and doors have none.
        // Not the original's: run for every pilot (SimLoop), its requests its own; the view, sounds and messages only for
        // the local one.
        public void UpdateLocalMech()
        {
            if (m_type == 3 || m_type == 7) return;
            bool local = IsLocal;
            switch (PowerState)
            {
                case 5:
                    if (!m_ejectStarted)
                    {
                        m_stateTime = Clock.g_currentClock + 0x389;
                        m_ejectStarted = true;
                        if (local) SimMain.g_ejectStarted = true;
                    }
                    else if (m_stateTime > Clock.g_currentClock)
                    {
                        if (local) Eyepoint.SetViewMode(c_viewDrop);
                    }
                    else if (local) Debris.g_localMechLost = true;

                    return;
                case 4:
                    if (!m_localMechDestroyed)
                    {
                        m_localMechDestroyed = true;
                        if (local)
                        {
                            SimMain.g_localMechDestroyed = true;
                            ScreenFlash.FlashZappedPalette();
                            Debris.g_localMechLost = true;
                            Eyepoint.SetViewMode(c_viewTrack);
                        }
                    }

                    return;
                case 2:
                    if (m_steering.Pressed("arms.next"))
                    {
                        if (local) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xfd), 0x32, 0x40, 5, 0x32);
                        g_manualWeaponCycle = true;
                        SelectNextWeapon();
                        g_manualWeaponCycle = false;
                        m_steering["arms.next"] = 0;
                    }

                    if (m_steering.Pressed("arms.group.next") && !Host.Weapons.KeepsGroupCycle(this))
                    {
                        CycleLocalWeaponGroup();
                        m_steering["arms.group.next"] = 0;
                    }

                    if (m_jettisonAmmoRequested)
                    {
                        m_jettisonAmmoRequested = false;
                        JettisonAmmo();
                    }

                    if (m_toggleMascRequested)
                    {
                        m_toggleMascRequested = false;
                        if ((m_flags & 0x10) != 0)
                        {
                            m_mascEngaged = !m_mascEngaged;
                            if (local)
                            {
                                CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(m_mascEngaged ? 0xcb : 0xca), 100, 0x40, 5, 0x50);
                                MissionSpeech.PlayCockpitSound(0x1f, m_mascEngaged ? 1 : 0);
                            }
                        }
                        else if (local) TimedOverlays.ShowInGameMessage("Not equipped with MASC.", 1, 0x16a, 0x32);
                    }

                    // Not the original's place: PUNCH_IN_AUTO_HDG (the cockpit's, running).
                    if (m_punchInRequested)
                    {
                        m_punchInRequested = false;
                        PunchInAutoHeading();
                    }

                    break;
            }

            // Not the original's place: the shutdown override's key (the cockpit's), while overheating.
            if (m_overrideShutdownRequested)
            {
                m_overrideShutdownRequested = false;
                if (PowerState != 3 && (m_flags & 4) != 0 && (m_flags & 8) == 0)
                {
                    if (local)
                    {
                        CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xcd), 100, 0x40, 5, 0x32);
                        MissionSpeech.PlayCockpitSound(2, -1);
                    }

                    m_flags |= 8;
                }
            }

            switch (m_powerRequest)
            {
                case 1:
                    // Not during Mech Soccer's start countdown (SoccerMatch).
                    if (((m_flags & 4) == 0 || (m_flags & 8) != 0) && PowerState == 3 && !Host.World.PowerLocked)
                    {
                        if (local) TimedOverlays.ShowInGameMessage("Powering up...", 1, 0x16a, 0x32);
                        PowerState = 0;
                        PlayerFlags &= ~0x10;
                        m_powerRequest = 0;
                    }

                    break;
                case -1:
                    if (PowerState != 3)
                    {
                        if (local)
                        {
                            MissionSpeech.PlayCockpitSound(0xd, -1);
                            EffectSound.PlaySoundAt(EyeOffset(), Host.Anim.MapSoundId(0xf2), Eyepoint.g_inCockpitView);
                        }

                        PowerState = 3;
                        m_powerRequest = 0;
                    }

                    break;
            }
        }

        // Mech Soccer (not in the original; SoccerMatch): its start's hold, powered down at once and quietly (a wreck or an
        // ejection left be); and Begin's power-up, the startup key's request (UpdateLocalMech).
        public void SoccerPowerDown()
        {
            if (PowerState == 4 || PowerState == 5 || (PlayerFlags & 6) != 0) return;
            PowerState = 3;
            m_powerRequest = 0;
        }

        public void SoccerPowerUp()
        {
            if (PowerState == 3) m_powerRequest = 1;
        }

        // g_autoEject as it applies to this mech: a match pilot's own, else the game's.
        public bool AutoEjects => Host.Net.NetRules ? IsPilot && m_autoEject : SimMain.g_autoEject;

        // g_mascEngaged (the local mech's), for the cockpit.
        public static bool MascEngaged => SimMain.LocalMech != null && SimMain.LocalMech.m_mascEngaged;

        // g_mascEngaged (the mech's MASC), g_lastMascRoll (the clock at its last malfunction roll), g_infiniteJumpFuel
        // (the "mightymouse" cheat).
        public bool m_mascEngaged;
        public static bool g_infiniteJumpFuel;
        public int m_lastMascRoll;

        // FireJumpJetEffects: the jets' flames at hardpoints 6 and 7 (launch effect 0x19); the local player hears the
        // fuel run low once (it passes 0x1c4 ticks), else the jets' roar from the last of them.
        // Not in the original: the jump fuel refill's carried ticks (below 4).
        public int m_jumpFuelRefillTicks;

        public void FireJumpJetEffects()
        {
            Transform jet = null;
            for (int i = 6; i <= 7; i++)
            {
                if (m_objects[i] == null) continue;
                jet = m_objects[i];
                m_firingObj = jet;
                Effects.SpawnLaunchEffect(0x19, m_firingObj);
            }

            if (jet == null) return;
            if (IsLocal && m_jumpFuel < 0x1c4 && m_jumpFuel + Clock.g_deltaTime > 0x1c4) CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0xde), 100, 0x40, 5, 0x50);
            // As the original, each step; at most 8 play at once (SoundLimits).
            else EffectSound.PlaySoundAt(Host.Audio.EyeUnits - Host.Anim.GetObjPosition(jet).ToFloat(), Host.Anim.MapSoundId(0xdf), Eyepoint.g_inCockpitView);
        }
    }
}
