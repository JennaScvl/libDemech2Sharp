using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // objectanim.c helpers.
    public static class ObjectAnim
    {
        // Scales p_value by mode p_mode: 1 by 1.5, 3 by 0.75, any other mode leaves it.
        public static int ScaleBySpeedLevel(int p_mode, int p_value)
        {
            switch (p_mode)
            {
                case 1:
                    return (p_value >> 1) + p_value;
                case 3:
                    return p_value - (p_value >> 2);
                default:
                    return p_value;
            }
        }
    }

    // The world stream's paths (demech2 MW2/src/resource.c LoadPathTable, MW2/include/path.h): up to 0x40 named paths of
    // up to 0x40 waypoints, from its path records.
    public static class PathTable
    {
        // PathPoint (SIZE 0x1c): a position (units) and three angles (degrees) the record gives as integers.
        public struct Point
        {
            public int X, Y, Z;
            public int Pitch, Heading, Roll;
            public int Duration; // ticks
        }

        // Path (SIZE 0x744)
        public sealed class Path
        {
            public string Name;
            public Point[] Points;
            public int Count => Points.Length;
        }

        // g_paths, g_pathCount
        public static readonly List<Path> g_paths = new List<Path>();

        public static void Reset() => g_paths.Clear();

        // LoadPathTable: the record's name (0x40 bytes at 0x08) and its waypoints (0x1c bytes each from 0x48). False when
        // the table or the path is full.
        // p_data: the stream, p_offset: the record's header, p_size: its size.
        public static bool LoadPathTable(byte[] p_data, int p_offset, int p_size)
        {
            int count = (p_size - 0x48) / 0x1c;
            if (g_paths.Count >= 0x40 || count > 0x40) return false;

            var path = new Path { Name = Libc.CString(p_data, p_offset + 0x08, p_offset + p_size), Points = new Point[Math.Max(count, 0)] };
            for (int i = 0; i < path.Points.Length; i++)
            {
                int at = 0x48 + i * 0x1c;
                path.Points[i] = new Point
                {
                    X = BitConverter.ToInt32(p_data, p_offset + at),
                    Y = BitConverter.ToInt32(p_data, p_offset + at + 4),
                    Z = BitConverter.ToInt32(p_data, p_offset + at + 8),
                    Pitch = BitConverter.ToInt32(p_data, p_offset + at + 0x0c),
                    Heading = BitConverter.ToInt32(p_data, p_offset + at + 0x10),
                    Roll = BitConverter.ToInt32(p_data, p_offset + at + 0x14),
                    Duration = BitConverter.ToInt32(p_data, p_offset + at + 0x18),
                };
            }

            g_paths.Add(path);
            return true;
        }

        // The first of the first p_count paths named p_name.
        public static Path Find(string p_name, int p_count)
        {
            for (int i = 0; i < p_count && i < g_paths.Count; i++)
            {
                if (string.Equals(g_paths[i].Name, p_name, StringComparison.OrdinalIgnoreCase)) return g_paths[i];
            }

            return null;
        }
    }

    // A timed callback turning a star's object (demech2 MW2/src/objectanim.c SpinTask). Its data is
    // "<star id>;<x>,<y>,<z>,<period>": the turns in degrees per period of seconds.
    public sealed class SpinTask : TimedCallback
    {
        readonly Func<Transform> getObject;

        // ObjectSpin (16.16: degrees, ticks)
        long m_rateX, m_rateY, m_rateZ;
        long m_period;
        int m_lastClock;
        bool m_bound;

        // p_getObject: the star's object (GetShapeObject of its shape slot), null once it has none; null when the id
        // names no star.
        public SpinTask(Func<Transform> p_getObject) => getObject = p_getObject;

        public override bool Run(int p_event, string p_data, int p_clock, int p_period)
        {
            switch (p_event)
            {
                case 0:
                {
                    int semicolon = p_data.IndexOf(';');
                    if (semicolon >= 0)
                    {
                        var values = new float[4];
                        Libc.ScanFloats(p_data.Substring(semicolon + 1), values);
                        m_rateX = Fixed.FromFloatL(values[0]);
                        m_rateY = Fixed.FromFloatL(values[1]);
                        m_rateZ = Fixed.FromFloatL(values[2]);
                        m_period = Fixed.FromFloatL(values[3] * 181.0f);
                        if (m_period == 0) m_period = 181L << 16;
                        m_lastClock = p_clock;
                    }

                    m_bound = getObject != null;
                    return m_bound;
                }
                case 1:
                {
                    Transform obj = m_bound ? getObject() : null;
                    if (obj == null) return false;
                    long t = Fixed.Div16L((long)(p_clock - m_lastClock) << 16, m_period);
                    long dx = Fixed.Mul16L(t, m_rateX);
                    long dy = Fixed.Mul16L(m_rateY, t);
                    long dz = Fixed.Mul16L(m_rateZ, t);
                    m_lastClock = p_clock;
                    Host.Anim.RotateObjFixed(obj, dx, dy, dz);
                    return true;
                }
                default:
                    return true;
            }
        }
    }

    // A timed callback moving a star's object around a circle (demech2 MW2/src/objectanim.c OrbitTask). Its data is
    // "<star id>;<radius>,<period>,<enabled>,<unused>": it travels the circle and turns once each period of seconds.
    public sealed class OrbitTask : TimedCallback
    {
        readonly Func<Transform> getObject;

        // ObjectOrbit (16.16: degrees and units a second, degrees)
        long m_turnRate, m_speed, m_angle;
        bool m_enabled;
        int m_lastClock;
        bool m_bound;

        public OrbitTask(Func<Transform> p_getObject) => getObject = p_getObject;

        public override bool Run(int p_event, string p_data, int p_clock, int p_period)
        {
            switch (p_event)
            {
                case 0:
                {
                    m_enabled = false;
                    int semicolon = p_data.IndexOf(';');
                    if (semicolon >= 0)
                    {
                        string[] tokens = p_data.Substring(semicolon + 1).Split(',');
                        float radius = 0, period = 0;
                        int enabled = 0;
                        if (tokens.Length > 0 && Libc.ScanFloat(tokens[0], out radius) &&
                            tokens.Length > 1 && Libc.ScanFloat(tokens[1], out period) && tokens.Length > 2)
                        {
                            enabled = Libc.Atoi(tokens[2]);
                        }

                        m_turnRate = Fixed.FromFloatL((float)(360.0 / period));
                        m_speed = Fixed.FromFloatL((float)(radius * 6.283185307179586 / period));
                        m_enabled = enabled != 0;
                        m_angle = 0;
                    }

                    m_lastClock = p_clock;
                    m_bound = getObject != null;
                    return m_bound;
                }
                case 1:
                {
                    if (!m_bound || !m_enabled) return false;
                    Transform obj = getObject();
                    if (obj == null) return false;

                    long cosine = Fixed.Sin(m_angle + (90L << 16));
                    long sine = Fixed.Sin(m_angle);
                    long t = Fixed.Div16L((long)(p_clock - m_lastClock) << 16, 181L << 16);
                    m_lastClock = p_clock;
                    long step = Fixed.Mul16L(t, m_speed);
                    long x = Fixed.Mul16L(step, cosine);
                    long z = -Fixed.Mul16L(sine, step);
                    Host.Anim.MoveObjFixed(obj, x, 0, z);
                    step = Fixed.Mul16L(m_turnRate, t);
                    Host.Anim.RotateObjFixed(obj, 0, step, 0);
                    m_angle += step;
                    m_angle %= 360L << 16; // FIXED_MOD360
                    return true;
                }
                default:
                    return true;
            }
        }
    }

    // A timed callback moving a star's object along a path (demech2 MW2/src/objectanim.c PathTask). Its data is
    // "<star id>;<mode>,<rotate>,<path name>": mode "loop", "repeat" or else stop at the end, and "rotate" to turn the
    // object along the path. Position and angles ease toward the path's point over 0.3 seconds. Event -1 restarts it.
    public sealed class PathTask : TimedCallback
    {
        readonly Func<Transform> getObject;
        readonly int pathCount;

        // PathFollower
        PathTable.Path m_path;
        int m_mode;      // 0 loop, 1 repeat, 2 stop
        bool m_rotate;
        int m_startClock;
        int m_duration;
        FloatRamp m_x, m_y, m_z;
        WrappedRamp m_pitch, m_heading, m_roll;
        bool m_bound;

        // p_pathCount: the paths loaded when its record ran (g_pathCount), the ones it can find.
        public PathTask(Func<Transform> p_getObject, int p_pathCount)
        {
            getObject = p_getObject;
            pathCount = p_pathCount;
        }

        // 0.3 seconds (181 ticks a second), 16.16 ticks; a turn, 16.16 degrees.
        const long RampTicks = 3558604, Turn = 360L << 16;

        void StartRamps(PathTable.Point p_point)
        {
            m_x.StartFloatRamp((long)p_point.X << 16, (long)p_point.X << 16, RampTicks);
            m_y.StartFloatRamp((long)p_point.Y << 16, (long)p_point.Y << 16, RampTicks);
            m_z.StartFloatRamp((long)p_point.Z << 16, (long)p_point.Z << 16, RampTicks);
            m_pitch.StartWrappedRamp(0, 0, RampTicks, Turn);
            m_heading.StartWrappedRamp(0, 0, RampTicks, Turn);
            m_roll.StartWrappedRamp(0, 0, RampTicks, Turn);
        }

        // The ramps from the path's first point, its whole duration, and the star's shape slot.
        bool Begin()
        {
            StartRamps(m_path.Points[0]);
            int total = 0;
            for (int i = 0; i < m_path.Count; i++) total += m_path.Points[i].Duration;
            m_duration = total;
            m_bound = getObject != null;
            return m_bound;
        }

        public override bool Run(int p_event, string p_data, int p_clock, int p_period)
        {
            switch (p_event)
            {
                case -1:
                    m_startClock = p_clock;
                    if (m_path != null && m_path.Count > 0) return Begin();
                    return true;
                case 0:
                {
                    m_startClock = p_clock;
                    int semicolon = p_data.IndexOf(';');
                    if (semicolon >= 0)
                    {
                        // sscanf("%[^,],%[^,],%[^,]"): up to three comma-separated fields.
                        string[] fields = p_data.Substring(semicolon + 1).Split(',');
                        string mode = fields.Length > 0 ? fields[0] : "";
                        string rotate = fields.Length > 1 ? fields[1] : "";
                        string name = fields.Length > 2 ? fields[2] : "";
                        if (string.Equals(mode, "loop", StringComparison.OrdinalIgnoreCase)) m_mode = 0;
                        else if (string.Equals(mode, "repeat", StringComparison.OrdinalIgnoreCase)) m_mode = 1;
                        else m_mode = 2;
                        m_rotate = string.Equals(rotate, "rotate", StringComparison.OrdinalIgnoreCase);
                        m_path = PathTable.Find(name, pathCount);
                    }

                    if (m_path != null && m_path.Count > 0) return Begin();
                    return true;
                }
                case 1:
                    return Step(p_clock);
                default:
                    return true;
            }
        }

        bool Step(int p_clock)
        {
            if (!m_bound || m_path == null) return false;
            Transform obj = getObject();
            if (obj == null) return false;

            PathTable.Path path = m_path;
            if (path.Count <= 1) return false;

            int elapsed = p_clock - m_startClock;
            int end = 0, start = 0, i;
            for (i = 0; i < path.Count; i++)
            {
                start = end;
                end += path.Points[i].Duration;
                if (end >= elapsed) break;
            }

            if (i >= path.Count - 1)
            {
                if (m_mode == 2)
                {
                    return false;
                }
                else if (m_mode == 1)
                {
                    i = 0;
                    elapsed = 0;
                    m_startClock = p_clock;
                    start = 0;
                    end = path.Points[0].Duration;
                    StartRamps(path.Points[0]);
                    Host.Anim.SnapObj(obj); // not in the original: the wrap is instant, not streaked
                }
                else if (path.Count == i)
                {
                    i = 0;
                    elapsed -= m_duration;
                    m_startClock = p_clock - elapsed;
                    start = 0;
                    end = path.Points[0].Duration;
                    // Not in the original: the wrap is instant, not eased by the ramps or streaked by the render lerp.
                    m_x.StartFloatRamp((long)path.Points[0].X << 16, (long)path.Points[0].X << 16, RampTicks);
                    m_y.StartFloatRamp((long)path.Points[0].Y << 16, (long)path.Points[0].Y << 16, RampTicks);
                    m_z.StartFloatRamp((long)path.Points[0].Z << 16, (long)path.Points[0].Z << 16, RampTicks);
                    Host.Anim.SnapObj(obj);
                }
            }

            PathTable.Point point = path.Points[i];
            PathTable.Point next = path.Count - 1 == i ? path.Points[0] : path.Points[i + 1];
            // Not in the original: a looping path holds at its last point for its duration instead of drifting back
            // to the first (the wrap then snaps).
            PathTable.Point moveTo = m_mode == 0 && path.Count - 1 == i ? point : next;

            // (A zero duration's 0 / 0, the floats' NaN, holds the point here.)
            long t = point.Duration != 0 ? Fixed.Div16L((long)(elapsed - start) << 16, (long)point.Duration << 16) : 0;
            m_x.m_target = (long)(moveTo.X - point.X) * t + ((long)point.X << 16);
            m_y.m_target = (long)(moveTo.Y - point.Y) * t + ((long)point.Y << 16);
            m_z.m_target = (long)(moveTo.Z - point.Z) * t + ((long)point.Z << 16);
            long x = m_x.UpdateFloatRamp(), y = m_y.UpdateFloatRamp(), z = m_z.UpdateFloatRamp();
            Host.Anim.SetObjPositionFixed(obj, x, y, z);

            long angle = (long)point.Heading << 16;
            if (m_rotate) angle += Fixed.Heading(next.X - point.X, next.Z - point.Z);
            Aim(ref m_heading, angle);

            // asin of the height difference over 65536, as written (past 1 the floats' NaN; here 90 degrees).
            angle = (long)point.Pitch << 16;
            if (m_rotate) angle -= Fixed.Asin(next.Y - point.Y);
            Aim(ref m_pitch, angle);

            Aim(ref m_roll, (long)point.Roll << 16);
            long pitch = m_pitch.UpdateWrappedRamp(), heading = m_heading.UpdateWrappedRamp(), roll = m_roll.UpdateWrappedRamp();
            Host.Anim.SetObjRotationFixed(obj, pitch, heading, roll);
            return true;
        }

        // The ramp's target p_angle (16.16 degrees), its value moved within half a turn of it.
        static void Aim(ref WrappedRamp p_ramp, long p_angle)
        {
            long delta = p_angle - p_ramp.m_value;
            while (delta > Fixed.Deg180) delta -= Turn;
            while (delta < -Fixed.Deg180) delta += Turn;
            p_ramp.m_target = p_angle;
            p_ramp.m_value = p_angle - delta;
        }
    }

    // A timed callback looping a sound on a star's object while the eye is in range (demech2 MW2/src/objectanim.c
    // AmbientSoundTask, MW2/src/soundfx.c UpdateAmbientSound, StopAmbientSound). Its data is
    // "<star id>;<range>,<sound name>,<enabled>", the range in metres (x100 units). It takes one of the ambient sample
    // slots (8-15), none if all are playing. The sample itself is the game's (IAmbientSoundHost).
    public sealed class AmbientSoundTask : TimedCallback
    {
        const int Slots = 8; // g_audioEngine's samples 8-15

        static readonly IAmbientSample[] s_slots = new IAmbientSample[Slots];

        readonly Func<Transform> getObject;
        readonly IAmbientSoundHost sounds;

        // AmbientSound
        int m_range;            // units
        int m_slot = -1;
        IAmbientSample m_source; // m_data's sample
        bool m_enabled;
        bool m_skip;
        int m_id;

        // p_getObject: the star's object (GetShapeObject of its shape slot), null once it has none; null when the id
        // names no star. p_sounds: the SNDS the name is found in and the samples played.
        public AmbientSoundTask(Func<Transform> p_getObject, IAmbientSoundHost p_sounds)
        {
            getObject = p_getObject;
            sounds = p_sounds;
        }

        public override bool Run(int p_event, string p_data, int p_clock, int p_period)
        {
            switch (p_event)
            {
                case 0:
                {
                    m_slot = -1;
                    m_source = null;
                    m_skip = true;
                    string name = "";
                    int semicolon = p_data.IndexOf(';');
                    if (semicolon >= 0)
                    {
                        // sscanf("%d,%[^,],%d")
                        string[] fields = p_data.Substring(semicolon + 1).Split(',');
                        m_range = Libc.Atoi(fields[0]) * 100;
                        if (fields.Length > 1) name = fields[1];
                        m_enabled = fields.Length > 2 && Libc.Atoi(fields[2]) != 0;
                    }

                    m_id = sounds.FindSound(name);
                    return getObject != null;
                }
                case 1:
                {
                    if (getObject == null || !m_enabled)
                    {
                        Stop();
                        return false;
                    }

                    Transform obj = getObject();
                    if (obj == null)
                    {
                        Stop();
                        return false;
                    }

                    Update(obj);
                    return true;
                }
                case 2:
                    Stop();
                    return true;
                default:
                    return true;
            }
        }

        // UpdateAmbientSound
        void Update(Transform p_obj)
        {
            bool started = false;
            if (m_skip)
            {
                m_skip = false;
                return;
            }

            float distance = Host.Anim.EyeDistance(p_obj);
            if (m_range < distance)
            {
                Stop();
                return;
            }

            if (m_slot == -1 || m_source == null || !m_source.IsAlive)
            {
                m_slot = -1;
                for (int i = 0; i < Slots; i++)
                {
                    if (s_slots[i] == null || !s_slots[i].IsAlive)
                    {
                        m_slot = i;
                        break;
                    }
                }

                if (m_slot == -1) return;

                m_source = sounds.CreateSample(p_obj, m_id, m_range);
                if (m_source == null)
                {
                    m_slot = -1;
                    return;
                }

                s_slots[m_slot] = m_source;
                started = true;
            }

            if (Host.Anim.GetDistanceVolume((int)distance) <= 0)
            {
                Stop();
                return;
            }

            m_source.Play(started);
        }

        // StopAmbientSound
        void Stop()
        {
            if (m_source == null || !m_source.IsAlive)
            {
                m_slot = -1;
                return;
            }

            m_source.Destroy();
            s_slots[m_slot] = null;
            m_slot = -1;
            m_source = null;
        }
    }
}
