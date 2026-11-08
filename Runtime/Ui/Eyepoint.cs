using UnityEngine;

namespace Demech2
{
    // The camera the eyepoint moves (the game's cockpit camera): its field of view, the object it follows, its pose.
    public interface IEyepointHost
    {
        // The camera's vertical field of view (degrees), set when it differs.
        void SetVerticalFov(float p_degrees);

        // The view mode's camera target: the eye anchor (the cockpit and satellite views), the orbit eye (the track
        // view) or none (the camera placed by hand).
        void TargetView(int p_mode);

        // The local mech's pilot, found if it isn't yet; and the cockpit view's pose: the eye turned by the torso's twist
        // and pitch, the pilot's pan and tilt (degrees) and half the eye's roll, shaken (UpdateCameraShake).
        void PoseCockpitEye(float p_pan, float p_tilt);

        // The eyepoint read back from the camera, where the cockpit view placed it (units, degrees).
        void ReadEye(out Vector3 p_units, out float p_heading, out float p_pitch, out float p_roll);

        // GetPlayerEyeView's eye object: p_mech's, raised by the local mech's cockpit height (units), and its heading
        // (degrees).
        void GetPlayerEyeView(Mech p_mech, out Vector3 p_units, out float p_heading);

        // The view placed after the update (mode p_mode): the camera at the eyepoint outside the cockpit, the cockpit's
        // shape drawn in it, the head shown outside it.
        void ViewUpdated(int p_mode);

        // The real clock (ticks), the ticks and seconds of the rendered frame.
        float RenderTicks { get; }
        float FrameDelta { get; }
        float FrameSeconds { get; }
    }

    // The eyepoint (demech2 MW2/src/eyepoint.c: UpdateEyepoint, UpdateCockpitView, GetCockpitEyeView,
    // GetPlayerEyeView, the view modes; view.c's track, drop, free-eye and ordinance views): the cockpit view faces the
    // mech's heading turned by its torso's twist and pitch, then by the pilot's pan and tilt: ramps (0.2 seconds)
    // toward the head yaw and pitch channels, which the glance buttons override; rolled by half the eye's roll. The field
    // of view is the eyepoint's (m_fovX 1: 90 degrees across the original's 4:3 screen), so the cockpit's panels sit
    // where the original draws them over it. A shake (CameraShake) moves and turns the eye for a moment. The cockpit
    // view zooms by the zoom channel (g_normalFov, 1 to 4). The other views (SetViewMode): the ordinance view follows
    // the local player's tracked missile, zoomed (g_zoomFov 2), back to the view it came from once it's gone; the track
    // view circles the tracked player's mech (COCKPIT_VIEW, and forced once the local mech is lost); the drop view
    // (ejecting) rises from the eye looking down, spinning; the free eye (a debug key) flies. The game's camera
    // (IEyepointHost) shows it.
    public sealed class Eyepoint
    {
        const float PilotRampSeconds = 0.2f;
        const float TicksPerSecond = 181f; // 0xb5
        const float DropAcceleration = 0.2368f; // g_dropAcceleration (units per tick squared)
        // The drop view's spin, 300 / 65536 a frame of 18 ticks in the original's timing, per tick of the real clock.
        const float DropSpinPerTick = 300f / 65536f / 18f;

        // eyepoint.h's view modes.
        public const int c_viewCockpit = 0, c_viewTrack = 1, c_viewFreeEye = 2, c_viewOrdinance = 3, c_viewDrop = 4, c_viewSatellite = 6;

        // g_inCockpitView: the view is the cockpit's; g_cockpitEyeSteady: and the eye is placed from it this frame.
        public static bool g_inCockpitView, g_cockpitEyeSteady;

        // g_requestedViewMode, g_viewMode (the mode updated last, -1 to restart one), g_lostViewMode,
        // g_ordinanceReturnMode; g_trackedPlayer and g_localPlayer (the camera's player); g_spectating.
        static int g_requestedViewMode, g_viewMode = -1, g_lostViewMode = -1, g_ordinanceReturnMode = -1;
        public static int g_trackedPlayer;
        static int g_cameraPlayer;
        public static bool g_spectating;

        // The ordinance view (ORDINANCE_VIEW, once TrackLastShot found a missile).
        public static bool OrdinanceView => g_requestedViewMode == c_viewOrdinance;

        // g_normalFov and g_zoomFov: the cockpit view's and the other views' m_fovX; the eyepoint's.
        static float g_normalFov = 1f, g_zoomFov = 1f;
        float m_fovX = 1f;
        public static float g_fovX = 1f;

        // g_eyepoint: the eye's place (units, the world's root frame) and turn (degrees), for the views placed by hand.
        public Vector3 m_eyeUnits;
        public float m_eyeHeading, m_eyePitch, m_eyeRoll;

        // The track view: g_trackDistance and its limits, g_trackHeight and its limits, g_trackTurn, the eased offsets
        // and angles.
        static float g_trackDistance, g_trackMinDistance, g_trackMaxDistance, g_trackHeight, g_trackMinHeight, g_trackMaxHeight;
        static float g_trackTurn = 180f;
        readonly Ramp m_trackOffsetX, m_trackOffsetY, m_trackOffsetZ;
        readonly Ramp m_trackHeading, m_trackPitch;

        // The drop view: g_dropSpeed, g_dropStartClock. The free eye: g_freeEyeSpeed. g_ordinanceSavedView.
        float m_dropSpeed;
        float m_dropStartClock;
        readonly Ramp m_freeEyeSpeed;
        (Vector3, float, float, float) m_savedEye;

        // g_pilotPan, g_pilotTilt (degrees); the glances let go.
        public float m_pan, m_tilt;
        public bool m_glanceReleased = true;

        readonly IEyepointHost m_host;

        // ScalarRamp / WrappedRamp (ramp.c): the value moves toward the target over the duration, by the real clock's
        // ticks; a wrapped one the short way around its period.
        sealed class Ramp
        {
            public float Value, Target;
            float time;
            readonly float duration, period;
            readonly IEyepointHost host;

            public Ramp(IEyepointHost p_host, float p_seconds, float p_period = 0)
            {
                host = p_host;
                duration = p_seconds * TicksPerSecond;
                period = p_period;
            }

            public void Start(float p_value)
            {
                Value = p_value;
                time = host.RenderTicks;
            }

            // SetWrappedRampTarget: within half a period of zero.
            public void SetWrappedTarget(float p_target)
            {
                p_target %= period;
                if (p_target > period / 2) p_target -= period;
                else if (p_target < -period / 2) p_target += period;
                Target = p_target;
            }

            public float Update()
            {
                float delta = Target - Value;
                if (period > 0)
                {
                    delta %= period;
                    if (delta > period / 2) delta -= period;
                    else if (delta < -period / 2) delta += period;
                }

                float step = (host.RenderTicks - time) * delta / duration;
                if (delta == 0 || (delta < 0 && step <= delta) || (delta > 0 && step >= delta)) Value = Target;
                else Value += step;
                if (period > 0) Value %= period;
                time = host.RenderTicks;
                return Value;
            }
        }

        public Eyepoint(IEyepointHost p_host)
        {
            m_host = p_host;
            m_trackOffsetX = new Ramp(p_host, 0.5f);
            m_trackOffsetY = new Ramp(p_host, 0.7f);
            m_trackOffsetZ = new Ramp(p_host, 0.5f);
            m_trackHeading = new Ramp(p_host, 0.2f, 360f);
            m_trackPitch = new Ramp(p_host, 0.2f, 360f);
            m_freeEyeSpeed = new Ramp(p_host, 1f);
        }

        // The vertical field of view (degrees) of horizontal half-angle tangent 1 / p_fovX over a 4:3 screen.
        public static float VerticalFov(float p_fovX) => 2f * Mathf.Atan(0.75f / p_fovX) * Mathf.Rad2Deg;

        // FirstEyepoint: the ramps, the zoom and the track view start over, the local player followed, the cockpit view.
        public void FirstEyepoint()
        {
            m_trackOffsetX.Start(0);
            m_trackOffsetY.Start(0);
            m_trackOffsetZ.Start(0);
            m_freeEyeSpeed.Start(0);
            m_trackHeading.Start(0);
            m_trackPitch.Start(0);
            InputMap.Set("cam.zoom", 1 << 16);
            g_normalFov = g_zoomFov = m_fovX = 1f;
            g_fovX = 1f;
            g_trackDistance = 0;
            g_trackTurn = 180f;
            g_viewMode = g_lostViewMode = g_ordinanceReturnMode = -1;
            g_trackedPlayer = g_cameraPlayer = SimMain.g_localPlayerId;
            g_spectating = false;
            g_requestedViewMode = c_viewCockpit;
        }

        // Not the original's: the pilot's look, glances and eye angles cleared (a networked regenerate).
        public void ResetLook()
        {
            m_pan = m_tilt = 0;
            m_glanceReleased = true;
            m_eyeHeading = m_eyePitch = m_eyeRoll = 0;
        }

        // SetViewMode: once the local mech is lost, the track view (not in a movie).
        public static void SetViewMode(int p_mode)
        {
            if (Debris.g_localMechLost && !Host.Input.Movie)
            {
                if (g_lostViewMode == -1) g_lostViewMode = c_viewTrack;
                p_mode = g_lostViewMode;
            }

            g_requestedViewMode = p_mode;
        }

        public static int GetViewMode() => g_requestedViewMode;

        // CycleTrackedPlayer: the camera on the next (p_next) or previous player, or back on the local one (p_home),
        // skipping those gone (flags 0x4800).
        public static void CycleTrackedPlayer(bool p_next, bool p_home)
        {
            int count = SimMain.PlayerCount;
            if (count == 0) return;
            for (int tries = 0; tries <= count; tries++)
            {
                int step = -1;
                if (p_home)
                {
                    g_trackedPlayer = SimMain.g_localPlayerId;
                    step = 0;
                }
                else if (p_next) step = 1;

                g_trackedPlayer += step;
                if (g_trackedPlayer < 0) g_trackedPlayer = count - 1;
                else if (g_trackedPlayer >= count) g_trackedPlayer = 0;
                g_cameraPlayer = g_trackedPlayer;
                Player player = SimMain.PlayerOf(g_trackedPlayer);
                if (player == null || (player.m_flags & 0x4800) == 0) break;
            }

            g_viewMode = -1;
        }

        // The player the camera follows (g_localPlayer).
        public static int CameraPlayer => g_cameraPlayer;

        // UpdateCockpitView's glances: left and right pan 70 degrees, up and down tilt 50 and 40; once let go, the pilot's
        // sinks go back to the middle.
        void Glance()
        {
            bool left = InputMap.Get("head.glance.left") != 0, right = InputMap.Get("head.glance.right") != 0;
            bool up = InputMap.Get("head.glance.up") != 0, down = InputMap.Get("head.glance.down") != 0;
            if (left) InputMap.Set("head.yaw", -70 << 16);
            else if (right) InputMap.Set("head.yaw", 70 << 16);
            else if (up) InputMap.Set("head.pitch", -50 << 16);
            else if (down) InputMap.Set("head.pitch", 40 << 16);
            else if (!m_glanceReleased)
            {
                InputMap.Set("head.yaw", 0);
                InputMap.Set("head.pitch", 0);
            }

            m_glanceReleased = !left && !right && !up && !down;
        }

        // UpdateScalarRamp of g_pilotPan / g_pilotTilt over a frame of p_seconds.
        static float Ease(float p_value, float p_target, float p_seconds)
        {
            float step = p_seconds / PilotRampSeconds;
            return step >= 1f ? p_target : p_value + (p_target - p_value) * step;
        }

        // UpdateEyepoint: the zoom, then the view mode's camera.
        public void UpdateEyepoint()
        {
            g_normalFov = InputMap.Get("cam.zoom") / 65536f;
            ApplyCameraFov();
            g_inCockpitView = g_cockpitEyeSteady = false;
            int mode = GetViewMode();
            if (mode == c_viewCockpit) g_cameraPlayer = SimMain.g_localPlayerId;
            Mech player = SimMain.MechOf(g_cameraPlayer);
            // Not the original's: once the local mech is lost the death orbit wins over every other view.
            bool deathOrbit = Debris.g_localMechLost && !g_spectating;
            if (deathOrbit) mode = c_viewTrack;
            else if (player == null && mode != c_viewFreeEye) mode = c_viewCockpit;
            m_host.TargetView(mode);

            switch (mode)
            {
                case c_viewOrdinance:
                    UpdateOrdinanceView();
                    break;
                case c_viewTrack:
                {
                    if (player == null) break; // the orbit eye holds where it was
                    float seconds = m_host.FrameDelta / TicksPerSecond;
                    float turn = InputMap.Get("cam.yaw") / 65536f * seconds;
                    if (Debris.g_localMechLost && !g_spectating) turn = m_host.FrameDelta * 0.25f;
                    UpdateTrackView(player, InputMap.Get("cam.range") / 65536f * seconds,
                        InputMap.Get("cam.height") / 65536f * seconds, InputMap.Get("cam.pitch") / 65536f, turn);
                    break;
                }
                case c_viewCockpit:
                case c_viewSatellite:
                    UpdateCockpitView();
                    break;
                case c_viewDrop:
                    UpdateDropView(player);
                    break;
                default:
                {
                    float seconds = m_host.FrameDelta / TicksPerSecond;
                    int eyeTilt = InputMap.Get("cam.pitch");
                    UpdateFreeEyeView(InputMap.Get("cam.height") / 65536f * seconds * 2,
                        InputMap.Get("cam.range") / 65536f * seconds * 2,
                        InputMap.Get("cam.slide") / 65536f * seconds, InputMap.Get("cam.yaw") / 65536f * seconds,
                        eyeTilt > 0 ? 45f * seconds : eyeTilt < 0 ? -45f * seconds : 0);
                    InputMap.Set("cam.pitch.center", 1);
                    break;
                }
            }

            g_viewMode = mode;
            g_eyepointUnits = m_eyeUnits;
            m_host.ViewUpdated(mode);
        }

        // ApplyCameraFov: the cockpit view's field of view or the other views', with a sound when it changes.
        void ApplyCameraFov()
        {
            float fov = GetViewMode() == c_viewCockpit ? g_normalFov : g_zoomFov;
            float vertical = VerticalFov(fov);
            m_host.SetVerticalFov(vertical);
            if (fov == m_fovX) return;
            m_fovX = fov;
            g_fovX = fov;
            CockpitSound.PlaySoundEffect(Host.Anim.MapSoundId(0x147), 100, 0x40, 5, 0x50);
        }

        // UpdateCockpitView: back from another view, the pilot's look and the shake start over; then the glances, and
        // the eye placed unless shaking.
        void UpdateCockpitView()
        {
            if (g_viewMode != c_viewCockpit && g_viewMode != c_viewSatellite)
            {
                m_pan = m_tilt = 0;
                CameraShake.ClearCameraShakeKeys();
                ApplyCameraFov();
            }

            g_inCockpitView = true;

            Glance();
            m_pan = Ease(m_pan, InputMap.Get("head.yaw") / 65536f, m_host.FrameSeconds);
            m_tilt = Ease(m_tilt, InputMap.Get("head.pitch") / 65536f, m_host.FrameSeconds);

            m_host.PoseCockpitEye(m_pan, m_tilt);
            g_cockpitEyeSteady = true;
            m_host.ReadEye(out m_eyeUnits, out m_eyeHeading, out m_eyePitch, out m_eyeRoll);
        }

        // GetCameraFloor: 500 units over the highest surface under the eye, 200 if that's at or below 0.
        static float GetCameraFloor(Vector3 p_units)
        {
            float height = SimMain.g_terrain != null ? SimMain.g_terrain.GetTerrainHeight(new Vector3(p_units.x, 1e9f, p_units.z)) : 0;
            return height > 0 ? height + 500 : height + 200;
        }

        // UpdateTrackView: the camera p_distance farther out and p_height up, p_turn further around the mech, tilted by
        // p_tilt, always looking at it; its limits from the first mech's size the first time; eased into place, the
        // first frame from wherever the eye was.
        void UpdateTrackView(Mech p_player, float p_distance, float p_height, float p_tilt, float p_turn)
        {
            m_host.GetPlayerEyeView(p_player, out Vector3 at, out float heading);
            if (g_viewMode != c_viewTrack)
            {
                if (!Debris.g_localMechLost) MissionSpeech.PlayCockpitSound(0x11, -1);
                if (g_trackDistance == 0)
                {
                    Mech first = SimMain.MechOf(0) ?? p_player;
                    g_trackDistance = first.m_radius * 3;
                    g_trackMinDistance = g_trackDistance / 2;
                    g_trackMaxDistance = g_trackDistance * 4;
                    g_trackHeight = g_trackDistance / 4;
                    g_trackMaxHeight = g_trackMaxDistance;
                    g_trackMinHeight = -first.Height + 200;
                }

                m_trackOffsetX.Start(m_eyeUnits.x - at.x);
                m_trackOffsetY.Start(m_eyeUnits.y - at.y);
                m_trackOffsetZ.Start(m_eyeUnits.z - at.z);
                m_trackHeading.Start(m_eyeHeading);
                m_trackPitch.Start(m_eyePitch);
                m_eyeRoll = 0;
                if (m_eyeUnits.x == at.x) m_eyeUnits.x += 10;
                g_zoomFov = 1f;
                ApplyCameraFov();
            }

            g_trackDistance = Mathf.Clamp(g_trackDistance + p_distance, g_trackMinDistance, g_trackMaxDistance);
            float height = p_height + g_trackHeight;
            g_trackTurn = (g_trackTurn + p_turn) % 360f;

            // GetBearingAndRange toward the mech.
            Vector3 d = at - m_eyeUnits;
            float bearing = FloatMath.FixedAtan2(d.x, d.z);
            float bearingTilt = FloatMath.FixedAtan2(d.y, new Vector2(d.x, d.z).magnitude);
            m_trackPitch.SetWrappedTarget(-bearingTilt - p_tilt / 2);
            m_eyePitch = m_trackPitch.Update();
            m_trackHeading.SetWrappedTarget(bearing);
            m_eyeHeading = m_trackHeading.Update();

            float angle = (heading - g_trackTurn) % 360f;
            if (angle < -180) angle += 360;
            else if (angle > 180) angle -= 360;
            float radians = angle * Mathf.Deg2Rad;
            m_trackOffsetX.Target = Mathf.Sin(radians) * g_trackDistance;
            m_eyeUnits.x = at.x + m_trackOffsetX.Update();
            m_trackOffsetZ.Target = Mathf.Cos(radians) * g_trackDistance;
            m_eyeUnits.z = at.z + m_trackOffsetZ.Update();

            float offsetY = height;
            float floor = GetCameraFloor(m_eyeUnits);
            if (at.y + offsetY < floor) offsetY = floor - at.y;
            g_trackHeight = Mathf.Clamp(height, g_trackMinHeight, g_trackMaxHeight);
            m_trackOffsetY.Target = offsetY;
            m_eyeUnits.y = at.y + m_trackOffsetY.Update();
        }

        // UpdateDropView: the first time, looking straight down; then rising from the eye's height (IntegrateMidpoint),
        // over the eye object, turning ever faster.
        void UpdateDropView(Mech p_player)
        {
            m_host.GetPlayerEyeView(p_player, out Vector3 at, out _);
            if (g_viewMode != c_viewDrop)
            {
                m_dropSpeed = 0;
                m_eyePitch = 90f;
                m_dropStartClock = m_host.RenderTicks;
                ApplyCameraFov();
            }

            float dt = m_host.FrameDelta;
            m_eyeUnits.y += (DropAcceleration * dt * 0.5f + m_dropSpeed) * dt;
            m_dropSpeed += DropAcceleration * dt;
            m_eyeUnits.x = at.x;
            m_eyeUnits.z = at.z;
            m_eyeHeading += (m_host.RenderTicks - m_dropStartClock) * DropSpinPerTick * m_host.FrameDelta;
        }

        // UpdateFreeEyeView: p_climb raises the eye, p_speed drives it ahead (eased, nothing below 32), p_strafe moves it
        // sideways, p_turn and p_pitch turn it; it stays over the ground. Once the local mech is lost it starts the track
        // distance back.
        void UpdateFreeEyeView(float p_climb, float p_speed, float p_strafe, float p_turn, float p_pitch)
        {
            float sinHeading = Mathf.Sin(m_eyeHeading * Mathf.Deg2Rad), cosHeading = Mathf.Cos(m_eyeHeading * Mathf.Deg2Rad);
            float cosPitch = Mathf.Cos(m_eyePitch * Mathf.Deg2Rad);
            if (g_viewMode != c_viewFreeEye)
            {
                if (Debris.g_localMechLost)
                {
                    m_eyeUnits.x -= g_trackDistance * sinHeading;
                    m_eyeUnits.z -= g_trackDistance * cosHeading;
                }

                m_eyeRoll = 0;
                m_freeEyeSpeed.Start(0);
                g_zoomFov = g_normalFov;
                ApplyCameraFov();
            }

            m_eyeUnits.x += p_strafe * cosHeading;
            m_eyeUnits.z -= p_strafe * sinHeading;
            m_eyePitch = Mathf.Clamp(m_eyePitch - p_pitch, -90f, 90f);
            m_eyeUnits.y += p_climb * 4;
            m_freeEyeSpeed.Target = p_speed * 16;
            float speed = m_freeEyeSpeed.Update();
            if (speed < 32 && speed > -32) speed = 0;
            m_eyeHeading += p_turn;
            m_eyeUnits.x -= speed * sinHeading * cosPitch;
            m_eyeUnits.z -= speed * cosHeading * cosPitch;
            m_eyeUnits.y = Mathf.Max(m_eyeUnits.y, GetCameraFloor(m_eyeUnits));
        }

        // g_eyepoint's place (units), for the maps that follow the free eye (g_mapFollowsFreeEye).
        public static Vector3 g_eyepointUnits;

        // UpdateOrdinanceView: the first time, zoomed and the view to return to kept; then the eye on the tracked shot
        // (its place and heading, no pitch or roll); once it's gone, back to that view (the free eye where it was).
        void UpdateOrdinanceView()
        {
            if (g_viewMode != c_viewOrdinance)
            {
                g_ordinanceReturnMode = g_viewMode;
                m_savedEye = (m_eyeUnits, m_eyeHeading, m_eyePitch, m_eyeRoll);
                g_zoomFov = 2f;
                ApplyCameraFov();
            }

            if (Shots.GetTrackedShotView())
            {
                m_eyeUnits = Shots.g_trackedPosition;
                m_eyeHeading = Shots.g_trackedHeading;
                m_eyePitch = m_eyeRoll = 0;
                return;
            }

            SetViewMode(g_ordinanceReturnMode);
            if (g_ordinanceReturnMode == c_viewFreeEye) (m_eyeUnits, m_eyeHeading, m_eyePitch, m_eyeRoll) = m_savedEye;
        }
    }
}
