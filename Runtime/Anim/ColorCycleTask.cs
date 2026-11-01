using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Demech2
{
    // A timed callback cycling the face colors of a star's shape (demech2 MW2/src/objectanim.c ColorCycleTask). Its data
    // is "<star id>;<color>,<color>,...": up to sixteen palette entries. Each tick face i of the shape takes color
    // (clock / period + i) % count. The original finds its shape loaded and drops the task if not; here the faces may
    // come later (IColorCycleHost.PrepareFaces), so the task waits for them.
    public sealed class ColorCycleTask : TimedCallback
    {
        readonly IColorCycleHost host;

        // ColorCycle (SIZE 0x50)
        Transform m_object;
        int m_shapeId = -1;
        int m_faceCount = -1;
        readonly List<int> m_colors = new List<int>();

        public ColorCycleTask(IColorCycleHost p_host) => host = p_host;

        public override bool Run(int p_event, string p_data, int p_clock, int p_period)
        {
            if (p_period == 0)
            {
                p_period = 1;
            }

            switch (p_event)
            {
                case 0:
                {
                    string idText = p_data;
                    int semicolon = p_data.IndexOf(';');
                    if (semicolon >= 0)
                    {
                        idText = p_data.Substring(0, semicolon);
                        foreach (string token in p_data.Substring(semicolon + 1).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (m_colors.Count >= 16) break;
                            m_colors.Add((int)Strtoul(token));
                        }
                    }

                    m_shapeId = Atoi(idText);
                    m_object = host.FindObject(m_shapeId);
                    return m_object != null;
                }
                case 1:
                {
                    if (m_object == null || m_colors.Count == 0) return false;
                    if (m_faceCount == -1)
                    {
                        m_faceCount = host.PrepareFaces(m_shapeId, m_object, m_colors);
                        if (m_faceCount == -1) return true;
                    }

                    int offset = p_clock / p_period % m_colors.Count;
                    for (int i = 0; i < m_faceCount; i++)
                    {
                        host.SetFaceColor(i, m_colors[(offset + i) % m_colors.Count]);
                    }

                    return true;
                }
                default:
                    return true;
            }
        }

        // strtoul(token, NULL, 0): decimal, 0x hex or 0 octal; 0 if none.
        static uint Strtoul(string p_text)
        {
            string text = p_text.Trim();
            try
            {
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Convert.ToUInt32(text.Substring(2), 16);
                if (text.Length > 1 && text[0] == '0') return Convert.ToUInt32(text, 8);
                int end = 0;
                while (end < text.Length && char.IsDigit(text[end])) end++;
                return end == 0 ? 0 : uint.Parse(text.Substring(0, end), CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return 0;
            }
        }

        // atoi: leading digits after an optional sign; 0 if none.
        static int Atoi(string p_text)
        {
            string text = p_text.Trim();
            int end = text.Length > 0 && (text[0] == '-' || text[0] == '+') ? 1 : 0;
            int start = end;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            return end == start ? 0 : int.Parse(text.Substring(0, end), CultureInfo.InvariantCulture);
        }
    }
}
