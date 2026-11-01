namespace Demech2
{
    // Values that move toward a target over time on the 181 Hz clock (demech2 MW2/src/ramp.c, include/ramp.h). Ramp is
    // the integer one; FloatRamp and WrappedRamp are the Matrox edition's floats as the lockstep's 16.16 (Fixed): a
    // ramp reaches its target after a duration in ticks; a wrapped ramp goes the short way around its period (angles).

    // Ramp (StartRamp, UpdateRamp).
    public sealed class Ramp
    {
        public int m_target, m_value, m_duration, m_time;

        public Ramp(int p_value, double p_seconds) : this(p_value, p_value, p_seconds) { }

        // StartRamp.
        public Ramp(int p_target, int p_value, double p_seconds)
        {
            m_target = p_target;
            m_value = p_value;
            m_duration = (int)(p_seconds * 181.0);
            m_time = Clock.g_currentClock;
        }

        public int UpdateRamp()
        {
            int delta = m_target - m_value;
            if (delta != 0)
            {
                int step = (int)((long)(Clock.g_currentClock - m_time) * delta / m_duration);
                if ((delta < 0 && step <= delta) || (delta > 0 && step >= delta)) m_value = m_target;
                else m_value += step;
            }

            m_time = Clock.g_currentClock;
            return m_value;
        }
    }

    // FloatRamp (StartFloatRamp, UpdateFloatRamp): the Matrox edition's ScalarRamp, its values 16.16.
    public struct FloatRamp
    {
        public long m_target, m_value, m_duration; // m_duration: ticks, 16.16
        public int m_time;

        public bool StartFloatRamp(long p_target, long p_value, long p_duration)
        {
            m_target = p_target;
            m_value = p_value;
            m_duration = p_duration;
            m_time = Clock.g_currentClock;
            return m_duration > 0;
        }

        // A value within 1e-07 of the target (here: on it) lands on it.
        public long UpdateFloatRamp()
        {
            long delta = m_target - m_value;
            if (delta != 0)
            {
                long step = Fixed.Div16L(delta * (Clock.g_currentClock - m_time), m_duration);
                if ((delta < 0 && step <= delta) || (delta > 0 && step >= delta)) m_value = m_target;
                else m_value += step;
            }

            m_time = Clock.g_currentClock;
            return m_value;
        }
    }

    // WrappedRamp (StartWrappedRamp, UpdateWrappedRamp), its values 16.16.
    public struct WrappedRamp
    {
        public long m_target, m_value, m_duration, m_period; // m_duration: ticks, 16.16
        public int m_time;

        public bool StartWrappedRamp(long p_target, long p_value, long p_duration, long p_period)
        {
            m_target = p_target;
            m_value = p_value;
            m_duration = p_duration;
            m_time = Clock.g_currentClock;
            m_period = p_period;
            return m_duration > 0;
        }

        // Moves the value the short way around the period.
        public long UpdateWrappedRamp()
        {
            long half = m_period / 2;
            long delta = m_target - m_value;
            delta %= m_period; // fmod
            if (delta > half) delta -= m_period;
            else if (delta < -half) delta += m_period;

            long step = Fixed.Div16L(delta * (Clock.g_currentClock - m_time), m_duration);
            if ((delta < 0 && step <= delta) || (delta > 0 && step >= delta)) m_value = m_target;
            else m_value += step;

            m_value %= m_period;
            m_time = Clock.g_currentClock;
            return m_value;
        }
    }
}
