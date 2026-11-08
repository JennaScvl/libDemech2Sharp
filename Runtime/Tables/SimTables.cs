using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Demech2
{
    // Not the original's: the sim's constant tables (weapons, maneuvers, AI scripts, sound limits, …) live as JSON in
    // StreamingAssets/Tables/<name>.json, read once on first use. A missing or unreadable table is fatal: the sim can't run
    // without it. Fixed-point values are stored as their raw integers so they load bit-identical.
    public static partial class SimTables
    {
        public static string PathOf(string p_name) => Path.Combine(Application.streamingAssetsPath, "Tables", p_name + ".json");

        // SHA-256 (hex) over every table, ordered by file name, each fed as its name then its exact bytes; a networked match refuses
        // joiners whose hash differs from the host's. "" when the folder is missing.
        public static string Hash => hash ??= ComputeHash();
        static string hash;

        static string ComputeHash()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Tables");
            if (!Directory.Exists(dir)) return "";
            string[] files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, (a, b) => string.CompareOrdinal(Path.GetFileName(a), Path.GetFileName(b)));
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string file in files)
            {
                sha.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(file)));
                sha.AppendData(File.ReadAllBytes(file));
            }
            StringBuilder hex = new StringBuilder(64);
            foreach (byte b in sha.GetHashAndReset()) hex.Append(b.ToString("x2"));
            return hex.ToString();
        }

        public static T Load<T>(string p_name) where T : class
        {
            string path = PathOf(p_name);
            T table = null;
            try { table = JsonUtility.FromJson<T>(File.ReadAllText(path)); }
            catch (Exception e) { throw new InvalidDataException($"SimTables: {path}: {e.Message}", e); }
            if (table == null) throw new InvalidDataException($"SimTables: {path}: empty");
            return table;
        }
    }
}
