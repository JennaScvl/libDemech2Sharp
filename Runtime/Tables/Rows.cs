using System;

namespace Demech2
{
    // The tables' JSON schemas (StreamingAssets/Tables/<name>.json): field names are the files' keys.

    // Armament.json: the base game's weapons (weapondata.c g_weaponDefs).
    [Serializable] public sealed class ArmamentRow
    {
        public int projectile, hitEffect, muzzleFx, muzzleFxAlt, repeats, spawnsShot, homing, burst, burstsPerMag, soundId,
            gravityScale, velocity;
        public float dmg, firingHeat, targetHeat;
        public int minLock, maxLock, cooldown, burstGap, ttl;
        public string label;
    }

    [Serializable] public sealed class ArmamentFile { public ArmamentRow[] weapons; }

    // ArmamentWorth.json: each weapon type's worth (mekfile.c GetMechValue).
    [Serializable] public sealed class ArmamentWorthFile { public int[] worth; }

    // SlotWorth.json: the critical slot kinds GetMechValue weighs.
    [Serializable] public sealed class SlotWorthRow { public int code, weight, startCount; }
    [Serializable] public sealed class SlotWorthFile { public SlotWorthRow[] slots; }

    // BlastKinds.json: the base game's effect types (g_effectInfo).
    [Serializable] public sealed class BlastKindRow { public int lifeTicks, paletteFlash, soundId, soundOdds, followCam, wantsTarget, hurts; }
    [Serializable] public sealed class BlastKindFile { public BlastKindRow[] kinds; }

    // VoicePitches.json: the rates RandomSampleRate draws from.
    [Serializable] public sealed class VoicePitchFile { public int[] hz; }

    // SoundCaps.json: soundlimits.c g_soundTable (id, priority, max count, rate).
    [Serializable] public sealed class SoundCapRow { public short soundId, rank, maxPlaying, pitch; }
    [Serializable] public sealed class SoundCapFile { public SoundCapRow[] caps; }

    // CritCallouts.json: the SNDS ids of the part a "Critical hit : " names.
    [Serializable] public sealed class CritCalloutFile { public int[] lines; }

    // PilotProbeRays.json: sixteen directions around a mech, as (x, z) divisors of a length (maneuvers.c).
    [Serializable] public sealed class ProbeRayFile { public ProbeRay[] rays; }
    [Serializable] public sealed class ProbeRay { public int dx, dz; }

    // PilotManeuvers.json: the maneuver tables (maneuvers.c).
    [Serializable] public sealed class ManeuverFile { public ManeuverStep[] standard, featherweight, plain, orbiting, flanking; }

    [Serializable] public sealed class ManeuverStep
    {
        public int maneuver, followerCount;
        public int[] followers;
    }

    // ScriptPicks.json: the AI script tables in their memory order (ai.c).
    [Serializable] public sealed class ScriptPickFile { public ScriptPick[] picks; }
    [Serializable] public sealed class ScriptPick { public int leader, follower; }

    // LoadingPalette.json: supanim.c g_supAnimPalette (6-bit components).
    [Serializable] public sealed class LoadingPaletteFile { public LoadingColor[] colors; }
    [Serializable] public sealed class LoadingColor { public byte r, g, b; }

    // CreditPages.json: dorcs.c's pages.
    [Serializable] public sealed class CreditPageFile { public CreditPage[] pages; }
    [Serializable] public sealed class CreditPage { public string heading, body; }

    // LanceMenuTexts.json: the command menus' texts and starting orders (lancemenu.c, commandmenu.c).
    [Serializable] public sealed class LanceMenuFile
    {
        public string[] formations, orders, slotStates;
        public int[] startingOrders;
    }

    // CommandKeys.json: the game keys in table order (gamekeys.c).
    [Serializable] public sealed class CommandRow
    {
        public string id;
        public int number;
    }

    [Serializable] public sealed class CommandFile { public CommandRow[] commands; }

    // LegacyMapNames.json: GAMEKEY.MAP's and INPUT.MAP's names for our ids.
    [Serializable] public sealed class MapNameRow
    {
        public string mapName;
        public string id;
    }

    [Serializable] public sealed class MapNameFile
    {
        public MapNameRow[] commandKeys;
        public MapNameRow[] channels;
    }

    // ControlChannels.json: the input channels in table order (inputmap.c).
    [Serializable] public sealed class ChannelRow
    {
        public string id, kind, incKey, decKey, holdKey, centerKey, drives;
        public int rest, low, high, rampShift, scaleShift;
        public bool menuOnly, recenters;
    }

    [Serializable] public sealed class ChannelFile { public ChannelRow[] channels; }

    // Each table, read on first use and kept.
    public static partial class SimTables
    {
        static ArmamentRow[] s_armament;
        public static ArmamentRow[] Armament => s_armament ??= Load<ArmamentFile>("Armament").weapons;

        static int[] s_armamentWorth;
        public static int[] ArmamentWorth => s_armamentWorth ??= Load<ArmamentWorthFile>("ArmamentWorth").worth;

        static SlotWorthRow[] s_slotWorth;
        public static SlotWorthRow[] SlotWorth => s_slotWorth ??= Load<SlotWorthFile>("SlotWorth").slots;

        static BlastKindRow[] s_blastKinds;
        public static BlastKindRow[] BlastKinds => s_blastKinds ??= Load<BlastKindFile>("BlastKinds").kinds;

        static int[] s_voicePitches;
        public static int[] VoicePitches => s_voicePitches ??= Load<VoicePitchFile>("VoicePitches").hz;

        static SoundCapRow[] s_soundCaps;
        public static SoundCapRow[] SoundCaps => s_soundCaps ??= Load<SoundCapFile>("SoundCaps").caps;

        static int[] s_critCallouts;
        public static int[] CritCallouts => s_critCallouts ??= Load<CritCalloutFile>("CritCallouts").lines;

        static ProbeRay[] s_probeRays;
        public static ProbeRay[] PilotProbeRays => s_probeRays ??= Load<ProbeRayFile>("PilotProbeRays").rays;

        static ManeuverFile s_maneuvers;
        public static ManeuverFile PilotManeuvers => s_maneuvers ??= Load<ManeuverFile>("PilotManeuvers");

        static ScriptPick[] s_scriptPicks;
        public static ScriptPick[] ScriptPicks => s_scriptPicks ??= Load<ScriptPickFile>("ScriptPicks").picks;

        static LoadingColor[] s_loadingPalette;
        public static LoadingColor[] LoadingPalette => s_loadingPalette ??= Load<LoadingPaletteFile>("LoadingPalette").colors;

        static CreditPage[] s_creditPages;
        public static CreditPage[] CreditPages => s_creditPages ??= Load<CreditPageFile>("CreditPages").pages;

        static LanceMenuFile s_lanceMenu;
        public static LanceMenuFile LanceMenuTexts => s_lanceMenu ??= Load<LanceMenuFile>("LanceMenuTexts");

        static CommandRow[] s_commandKeys;
        public static CommandRow[] CommandKeys => s_commandKeys ??= Load<CommandFile>("CommandKeys").commands;

        static MapNameFile s_legacyMapNames;
        public static MapNameFile LegacyMapNames => s_legacyMapNames ??= Load<MapNameFile>("LegacyMapNames");

        static ChannelRow[] s_controlChannels;
        public static ChannelRow[] ControlChannels => s_controlChannels ??= Load<ChannelFile>("ControlChannels").channels;
    }
}
