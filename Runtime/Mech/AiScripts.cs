using System;
using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // The AI's scripts (demech2 MW2/src/ai.c: LoadAIScripts, g_aiScripts, the script tables g_mechScripts and the other
    // player types'): AIT resources 1 to 9, each listing per AI state the rules that apply in it:
    //     u16 stateCount; stateCount x (u16 state, u16 ruleCount); the rules of every state in that order, 12 bytes each
    //     (AiRule: s16 message, u16 target, s16 arg, u8 transition, s16 newState, s16 newTarget, u8 unused).
    // A player's script slot takes the script a table names for its player type, by objective (the slot's script
    // number) and whether it leads its star.
    public static class AiScripts
    {
        // An AIT resource, read as the original reads it: words from its start, rules at byte offsets.
        public sealed class Script
        {
            readonly byte[] data;
            readonly Dictionary<int, Mech.AiRule> rules = new Dictionary<int, Mech.AiRule>();

            public Script(byte[] p_data)
            {
                data = p_data;
            }

            // The u16 at word p_index (0 past the end).
            public int Word(int p_index)
            {
                int at = p_index * 2;
                return at >= 0 && at + 2 <= data.Length ? BitConverter.ToUInt16(data, at) : 0;
            }

            // The rule at byte p_at, or null past the end.
            public Mech.AiRule RuleAt(int p_at)
            {
                if (p_at < 0 || p_at + 12 > data.Length) return null;
                if (rules.TryGetValue(p_at, out Mech.AiRule rule)) return rule;
                rule = new Mech.AiRule
                {
                    m_message = BitConverter.ToInt16(data, p_at),
                    m_target = BitConverter.ToUInt16(data, p_at + 2),
                    m_arg = BitConverter.ToInt16(data, p_at + 4),
                    m_transition = data[p_at + 6],
                    m_newState = BitConverter.ToInt16(data, p_at + 7),
                    m_newTarget = BitConverter.ToInt16(data, p_at + 9),
                };
                rules[p_at] = rule;
                return rule;
            }
        }

        // g_aiScripts: [0] none, [1] to [9] AIT resources 1 to 9.
        static readonly Script[] s_scripts = new Script[10];

        // The script tables in their memory order (StreamingAssets/Tables/ScriptPicks.json: mechs, artillery, wanderers,
        // trucks, tanks, 16 entries each; helicopters, 15), so an entry past a table reads the next one's as the original
        // does. Each entry: the script for a leader, then a follower. Past the last, zeros.
        static int[] s_picks;

        static int[] Picks
        {
            get
            {
                if (s_picks != null) return s_picks;
                ScriptPick[] picks = SimTables.ScriptPicks;
                var flat = new int[picks.Length * 2];
                for (int i = 0; i < picks.Length; i++)
                {
                    flat[i * 2] = picks[i].leader;
                    flat[i * 2 + 1] = picks[i].follower;
                }

                return s_picks = flat;
            }
        }

        const int MechTable = 0, ArtilleryTable = 16, WandererTable = 32, TruckTable = 48, TankTable = 64, HelicopterTable = 80;

        static int TableEntry(int p_entry, int p_follower)
        {
            if (p_entry < 0) return 0;
            int table = p_entry / 16, index = p_entry % 16;
            if (table > 5 || (table == 5 && index >= 15)) return 0;
            return Picks[index * 2 + p_follower];
        }

        // The script number for player type p_type's script p_script, as a leader (p_follower 0) or a follower.
        public static int ScriptFor(int p_type, int p_script, int p_follower)
        {
            int table;
            switch (p_type)
            {
                case 1:
                    table = MechTable;
                    break;
                case 3:
                    table = ArtilleryTable;
                    break;
                case 6:
                    table = WandererTable;
                    break;
                case 2:
                    table = TruckTable;
                    break;
                case 4:
                    table = TankTable;
                    break;
                case 5:
                case 8:
                    table = HelicopterTable;
                    break;
                default:
                    return 0;
            }

            return TableEntry(table + p_script, p_follower);
        }

        // g_aiScripts[p_index], or null.
        public static Script Get(int p_index) => p_index > 0 && p_index < s_scripts.Length ? s_scripts[p_index] : null;

        // LoadAIScripts: the nine AIT resources (p_read: AIT resource 1 to 9's bytes, null when missing).
        public static void LoadAIScripts(Func<int, byte[]> p_read)
        {
            s_scripts[0] = null;
            for (int i = 0; i < 9; i++)
            {
                byte[] data = p_read(i + 1);
                s_scripts[i + 1] = data != null ? new Script(data) : null;
                if (data == null) Debug.LogWarning($"AiScripts: no AIT {i + 1}");
            }
        }
    }
}
