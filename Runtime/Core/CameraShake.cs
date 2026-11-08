using UnityEngine;

namespace Demech2
{
    // The camera's shake (demech2 MW2/src/camerashake.c; fadepal.c PlayPlayerHitFeedback), in the original's
    // floats: up to ten keys, each an offset of the cockpit eye's position (units) and pitch, heading and roll (degrees)
    // reached over its duration, played in turn on top of the eye's own view. A hit on the local mech, a collision or a
    // hard landing pushes the eye away from the blow and back. Not in the original: it runs on the real clock, which
    // the caller passes (p_realClock, ticks), so the shake moves every rendered frame; the game applies the offsets.
    public static class CameraShake
    {
        struct CameraShakeKey
        {
            public Vector3 Position, Angles;
            public int m_duration; // ticks
        }

        static readonly CameraShakeKey[] g_cameraShakeKeys = new CameraShakeKey[10];
        static int g_cameraShakeKeyCount, g_cameraShakeKey;
        static float g_cameraShakeKeyTime, s_lastClock;
        static bool g_cameraShakeActive;
        static RealClockRamp g_cameraShakeX, g_cameraShakeY, g_cameraShakeZ, g_cameraShakePitch, g_cameraShakeHeading, g_cameraShakeRoll;

        // The shake's offsets this frame (zero when still).
        public static Vector3 PositionOffset { get; private set; }
        public static Vector3 AngleOffset { get; private set; }

        public static bool IsCameraShaking => g_cameraShakeActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ClearCameraShakeKeys();

        public static void ClearCameraShakeKeys()
        {
            g_cameraShakeKeyCount = g_cameraShakeKey = 0;
            g_cameraShakeKeyTime = 0;
            g_cameraShakeActive = false;
            PositionOffset = AngleOffset = Vector3.zero;
        }

        public static void AddCameraShakeKey(Vector3 p_position, Vector3 p_angles, double p_seconds)
        {
            if (g_cameraShakeKeyCount >= 10) return;
            g_cameraShakeKeys[g_cameraShakeKeyCount++] = new CameraShakeKey { Position = p_position, Angles = p_angles, m_duration = (int)(p_seconds * 181.0) };
        }

        public static void StartCameraShake(float p_realClock)
        {
            g_cameraShakeKey = 0;
            g_cameraShakeKeyTime = 0;
            s_lastClock = p_realClock;
            if (g_cameraShakeKeyCount <= 0) return;
            g_cameraShakeActive = true;
            StartCameraShakeKey(0, p_realClock);
        }

        // StartCameraShakeKey: the ramps toward a key, from the shake's offset so far.
        static void StartCameraShakeKey(int p_key, float p_realClock)
        {
            if (p_key >= g_cameraShakeKeyCount || p_key >= 10 || p_key < 0) return;
            Vector3 position = p_key == 0 ? PositionOffset : new Vector3(g_cameraShakeX.Value, g_cameraShakeY.Value, g_cameraShakeZ.Value);
            Vector3 angles = p_key == 0 ? AngleOffset : new Vector3(g_cameraShakePitch.Value, g_cameraShakeHeading.Value, g_cameraShakeRoll.Value);
            CameraShakeKey key = g_cameraShakeKeys[p_key];
            double seconds = key.m_duration / 181.0;
            g_cameraShakeX.Start(key.Position.x, position.x, seconds, p_realClock);
            g_cameraShakeY.Start(key.Position.y, position.y, seconds, p_realClock);
            g_cameraShakeZ.Start(key.Position.z, position.z, seconds, p_realClock);
            g_cameraShakePitch.Start(key.Angles.x, angles.x, seconds, p_realClock);
            g_cameraShakeHeading.Start(key.Angles.y, angles.y, seconds, p_realClock);
            g_cameraShakeRoll.Start(key.Angles.z, angles.z, seconds, p_realClock);
        }

        // UpdateCameraShake: the next key once one's time is up; the eye's offset. False when still.
        public static bool UpdateCameraShake(float p_realClock)
        {
            if (g_cameraShakeKey < 0 || g_cameraShakeKey >= g_cameraShakeKeyCount) g_cameraShakeActive = false;
            if (!g_cameraShakeActive)
            {
                PositionOffset = AngleOffset = Vector3.zero;
                return false;
            }

            // Not in the original: the real clock since the last update, so the shake moves every rendered frame.
            g_cameraShakeKeyTime += p_realClock - s_lastClock;
            s_lastClock = p_realClock;
            if (g_cameraShakeKeyTime >= g_cameraShakeKeys[g_cameraShakeKey].m_duration)
            {
                g_cameraShakeKeyTime = 0;
                g_cameraShakeKey++;
                if (g_cameraShakeKey < g_cameraShakeKeyCount) StartCameraShakeKey(g_cameraShakeKey, p_realClock);
            }

            PositionOffset = new Vector3(g_cameraShakeX.Update(p_realClock), g_cameraShakeY.Update(p_realClock), g_cameraShakeZ.Update(p_realClock));
            AngleOffset = new Vector3(g_cameraShakePitch.Update(p_realClock), g_cameraShakeHeading.Update(p_realClock), g_cameraShakeRoll.Update(p_realClock));
            return true;
        }

        // PlayPlayerHitFeedback: unless already shaking, the eye pushed 25 units against p_blow's direction (and pitched
        // by it) over 0.2 seconds, back half of that over 0.5, and home over 0.2.
        public static void PlayPlayerHitFeedback(Vector3 p_blow, float p_realClock)
        {
            if (g_cameraShakeActive) return;
            Vector3 v = NormalizeVectorGuarded(p_blow);
            float pitch = v.x == 0 && v.z == 0 ? v.y * 30 : (v.y - v.z) * 5;
            Vector3 position = v * -25;
            var angles = new Vector3(pitch, 0, 0);
            ClearCameraShakeKeys();
            AddCameraShakeKey(position, angles, 0.2);
            AddCameraShakeKey(-position / 2, -angles / 2, 0.5);
            AddCameraShakeKey(Vector3.zero, Vector3.zero, 0.2);
            StartCameraShake(p_realClock);
        }

        // NormalizeVectorGuarded (floats): over ApproximateVectorLength; a zero vector stays.
        static Vector3 NormalizeVectorGuarded(Vector3 p_v)
        {
            float x = Mathf.Abs(p_v.x), y = Mathf.Abs(p_v.y), z = Mathf.Abs(p_v.z), swap;
            if (x < y)
            {
                swap = x;
                x = y;
                y = swap;
            }

            if (x < z)
            {
                swap = x;
                x = z;
                z = swap;
            }

            float length = (x * 4 + y + z) / 4;
            return length > 1e-07f ? p_v / length : p_v;
        }

        // FloatRamp on the real clock instead of the sim's.
        struct RealClockRamp
        {
            public float Target, Value, Duration, Time;

            public void Start(float p_target, float p_value, double p_seconds, float p_realClock)
            {
                Target = p_target;
                Value = p_value;
                Duration = (float)(p_seconds * 181.0);
                Time = p_realClock;
            }

            public float Update(float p_realClock)
            {
                float delta = Target - Value;
                if (delta > 1e-07f || delta < -1e-07f)
                {
                    float step = (p_realClock - Time) * delta / Duration;
                    if ((delta < 0 && step <= delta) || (delta > 1e-07f && step >= delta)) Value = Target;
                    else Value += step;
                }
                else
                {
                    Value = Target;
                }

                Time = p_realClock;
                return Value;
            }
        }
    }
}
