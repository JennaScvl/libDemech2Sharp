using System.Collections.Generic;
using UnityEngine;

namespace Demech2
{
    // Explosions, fires, flashes and debris (shots.c: SpawnEffectEx, UpdateEffects, ScatterDebris, StartNuke, UpdateNuke;
    // world.c's xplode records; fadepal.c EmitWreckSmoke), the floats as the lockstep's fixed point
    // (Fixed) where they are the sim's (the effects' places, timing, crowding and damage; their shapes, lights and sounds the
    // game's, Host.Fx). MW2 has no particles: each of the 0x100 effect slots is a shape (an xplode record's) shown where the
    // effect goes off for its type's duration, its animated faces playing once (SetAnimMode 2). Its type (g_effectInfo)
    // also gives it a sound, a palette flash and the effect camera, and whether it damages what is near it. Debris (type
    // 0xb) slots are thrown as Debris pieces.
    //
    // An effect that damages hurts the mechs (with splash damage), the game things and the chunks near it while it lasts,
    // and fires heat the mechs near them (HeatMechsNearFires). The table, the crowding, a placed effect's additions, the
    // damage and the nuke are the host's to change (Host.Effects).
    public static class Effects
    {
        public const int c_impactMech = 0x100, c_impactThing = 0x200, c_impactGround = 0x400;
        public const int c_slotCount = 0x100;

        // EffectInfo (SIZE 0x1c)
        public readonly struct EffectInfo
        {
            public readonly int m_duration;    // ticks
            public readonly int m_flash;       // a palette flash when the camera follows, or -1
            public readonly int m_sound;       // a sound effect, or <= 0
            public readonly int m_soundChance; // the percent chance of the sound, or -1 for always
            public readonly bool m_camera;     // the effect camera may follow it
            public readonly bool m_needsObject; // only a slot with a scene object can play it
            public readonly bool m_damages;    // it deals splash damage while it lasts
            public readonly object m_ext;      // the host's data for this type, or null
            readonly bool m_prjSound;

            public EffectInfo(int p_duration, int p_flash, int p_sound, int p_soundChance, int p_camera, int p_needsObject, int p_damages)
            {
                m_duration = p_duration;
                m_flash = p_flash;
                m_sound = p_sound;
                m_soundChance = p_soundChance;
                m_camera = p_camera != 0;
                m_needsObject = p_needsObject != 0;
                m_damages = p_damages != 0;
                m_ext = null;
                m_prjSound = false;
            }

            // A copy of p_info with the host's data, its sound the loaded PRJ's id when p_soundIsPrjId.
            public EffectInfo(EffectInfo p_info, bool p_soundIsPrjId, object p_ext)
            {
                this = p_info;
                m_ext = p_ext;
                m_prjSound = p_soundIsPrjId;
            }

            // The sound as the loaded PRJ's id.
            public int SoundId => m_prjSound ? m_sound : Host.Anim.MapSoundId(m_sound);
        }

        // The base game's effect types (Tables/BlastKinds.json), read on first use. Held in a nested class so the state
        // walk (which walks Effects' own statics) never sees it.
        static class BlastKinds
        {
            internal static readonly EffectInfo[] All = Load();

            static EffectInfo[] Load()
            {
                BlastKindRow[] rows = SimTables.BlastKinds;
                var info = new EffectInfo[rows.Length];
                for (int i = 0; i < rows.Length; i++)
                {
                    BlastKindRow r = rows[i];
                    info[i] = new EffectInfo(r.lifeTicks, r.paletteFlash, r.soundId, r.soundOdds, r.followCam, r.wantsTarget, r.hurts);
                }
                return info;
            }
        }

        public static EffectInfo[] BaseInfo => BlastKinds.All;

        // The effect types in use (the host's table; the base game's by default).
        public static EffectInfo[] Info => Host.Effects.Info;


        // The entry for an effect type whose sound and palette flash the running game plays (Info's by default).
        static EffectInfo LocalInfo(int p_type) => Host.Effects.LocalInfo(p_type);

        // Effect (SIZE 0x28)
        public sealed class Effect
        {
            public ISceneObject m_object;
            public FixVec m_position;   // 16.16 units
            public int m_timeLeft;      // ticks
            public bool m_active;
            public int m_animation = -1;
            public bool m_hasCamera;
            public int m_type = -1;     // an index into g_effectInfo
            public int m_owner = -1;    // the player it damages on behalf of, or -2
            public object m_ext;        // the host's data for this slot, or null (Host.Effects.ResetSlot clears it)
            public int m_billboard;     // the game's: 0 none, 1 turned to the eye about the vertical, 2 turned to it fully
        }

        public static readonly Effect[] g_effects = new Effect[c_slotCount];
        public static int g_nextEffectRecord;

        // g_launchEffectPlayer's firing object (SpawnLaunchEffect places the effect there).
        static Transform g_launchEffectFiringObj;

        static int g_nukeTimeLeft;
        static long g_nukeMaxRadius, g_nukeRadius; // 16.16 units
        static FixVec g_nukePosition;

        // FirstShots' effect half: every slot empty.
        public static void FirstEffects()
        {
            for (int i = 0; i < c_slotCount; i++)
            {
                g_effects[i] = new Effect();
                ResetEffectSlot(i);
            }

            g_nextEffectRecord = 0;
            Host.Fx.ResetEffectLooks();
            g_nukeTimeLeft = 0;
            Debris.ZeroChunx();
        }

        public static void ResetEffectSlot(int p_index)
        {
            Effect effect = g_effects[p_index];
            effect.m_position = FixVec.Zero;
            effect.m_timeLeft = 0;
            effect.m_active = false;
            effect.m_hasCamera = false;
            effect.m_owner = -1;
            Host.Effects.ResetSlot(effect);
        }

        // A networked match's effect with no free slot of its type takes a slot no xplode record filled, made its type
        // for good, shown as the world's own shape of it or of its nearest (the game's ClaimEffectLook; none: unseen,
        // still burning and damaging). The damage reaches the host's fixed radius (Host.Effects.FixedRadius), so every
        // machine damages alike.
        static int Claim(int p_type)
        {
            for (int i = g_nextEffectRecord; i < c_slotCount; i++)
            {
                Effect effect = g_effects[i];
                if (effect.m_type != -1 || effect.m_object != null) continue;
                effect.m_type = p_type;
                effect.m_active = false;
                Host.Fx.ClaimEffectLook(i, p_type);
                return i;
            }

            return -1;
        }

        // An effect's damage radius (16.16 units): the host's fixed one, else its object's; false without either.
        public static bool DamageRadius(Effect p_effect, out long p_radius)
        {
            if (Host.Effects.FixedRadius(p_effect.m_type, out float radius))
            {
                p_radius = Fixed.FromFloatL(radius);
                return true;
            }

            p_radius = p_effect.m_object != null ? Fixed.FromFloatL(p_effect.m_object.Radius) : 0;
            return p_effect.m_object != null;
        }

        // ---- Spawning -----------------------------------------------------------------------------------------------------

        // p_at, p_camera: 16.16 units.
        public static void SpawnEffect(int p_owner, int p_type, FixVec p_at, FixVec p_camera, Shape p_hit = null) =>
            SpawnEffectEx(p_owner, p_type, p_at, p_camera, Vector3.zero, p_hit);

        // Float units.
        public static void SpawnEffect(int p_owner, int p_type, Vector3 p_at, Vector3 p_camera, Shape p_hit = null) =>
            SpawnEffect(p_owner, p_type, FixVec.FromFloat(p_at), FixVec.FromFloat(p_camera), p_hit);

        public static void SpawnRotatedEffect(int p_type, Vector3 p_angles, FixVec p_at) => SpawnEffectEx(-2, p_type, p_at, p_at, p_angles);

        // An effect at a player's firing object (the jump jets' flames, muzzle flashes): types 0x17 and up.
        public static void SpawnLaunchEffect(int p_type, Transform p_firingObj)
        {
            g_launchEffectFiringObj = p_firingObj;
            SpawnEffectEx(-2, p_type, FixVec.Zero, FixVec.Zero, Vector3.zero);
        }

        // Spawns an effect. The low byte of p_type is the effect; the high byte says what a shot hit (c_impactMech etc.),
        // which picks a variant of it, and 0x4000 keeps its sound quiet. The effect takes a free slot of its type (none if
        // three of them already burn within 500 units), and the effect camera may follow it from p_camera. p_angles:
        // degrees. Types 0x22 and 0x23 (when the host's table has them) vary as the missiles' 3 does.
        // The game's look of the shot DetonateShot is setting off (Host.Fx.ImpactFxFor; null otherwise).
        public static object g_impactFx;

        // An explosion (a missile's impact, something destroyed), for the game's sound. Its chained effects too.
        public static bool g_explosionSound;

        public static void SpawnExplosion(int p_owner, int p_type, FixVec p_at, FixVec p_camera, Shape p_hit = null)
        {
            bool was = g_explosionSound;
            g_explosionSound = true;
            try { SpawnEffect(p_owner, p_type, p_at, p_camera, p_hit); }
            finally { g_explosionSound = was; }
        }

        public static void SpawnExplosion(int p_owner, int p_type, Vector3 p_at, Vector3 p_camera, Shape p_hit = null) =>
            SpawnExplosion(p_owner, p_type, FixVec.FromFloat(p_at), FixVec.FromFloat(p_camera), p_hit);

        public static void SpawnEffectEx(int p_owner, int p_type, FixVec p_at, FixVec p_camera, Vector3 p_angles,
            Shape p_hit = null)
        {
            if (g_effects[0] == null) FirstEffects();
            object impactFx = g_impactFx; // the first effect's only, not a chained one's
            g_impactFx = null;
            bool quiet = false, chain = false;
            int chainType = -1;
            if (p_type < 0) return;

            int flags = p_type & 0xff00;
            p_type &= 0xff;
            if (p_type >= Host.Effects.TypeCount) return;
            if ((flags & 0x4000) != 0) quiet = true;

            switch (p_type)
            {
                case 5:
                    if ((flags & c_impactMech) != 0)
                    {
                        p_at.X = p_camera.X;
                        p_at.Z = p_camera.Z;
                        p_type = 8;
                    }
                    else if ((flags & c_impactGround) != 0) p_type = 9;
                    else if ((flags & 0x2000) != 0) p_type = 0x12;
                    break;
                case 3:
                case 0x22:
                case 0x23:
                    if ((flags & c_impactGround) != 0) p_type = 0xc;
                    else if ((flags & 0x2000) != 0) p_type = 0x11;
                    else if ((flags & c_impactThing) != 0) p_type = 0x13;
                    chain = true;
                    chainType = 0x10b;
                    break;
                case 4:
                    if ((flags & c_impactGround) != 0) p_type = 0xc;
                    else if ((flags & 0x2000) != 0) p_type = 0x11;
                    else if ((flags & c_impactThing) != 0) p_type = 0x14;
                    chain = true;
                    chainType = 0x10b;
                    break;
                case 0:
                    if ((flags & 0x2000) != 0) p_type = 0xe;
                    else if ((flags & c_impactGround) != 0) p_type = 9;
                    break;
                case 1:
                    if ((flags & 0x2000) != 0) p_type = 0xf;
                    else if ((flags & c_impactGround) != 0) p_type = 9;
                    break;
                case 2:
                    if ((flags & 0x2000) != 0) p_type = 0x10;
                    else if ((flags & c_impactGround) != 0) p_type = 9;
                    break;
                case 6:
                    if ((flags & c_impactGround) != 0) p_type = 0x15;
                    break;
                case 0xb:
                {
                    int pieces;
                    if ((flags & 0x1000) != 0) pieces = 0x10;
                    else if ((flags & c_impactMech) != 0) pieces = 4;
                    else if ((flags & c_impactThing) != 0) pieces = 4;
                    else pieces = 8;
                    ScatterDebris(p_at, pieces);
                    return;
                }
            }

            EffectInfo info = Info[p_type];
            Vector3 d = Host.Fx.Eyepoint - p_at.ToFloat(); // the sound's and the effect camera's (render side)
            if (info.m_needsObject || info.m_camera)
            {
                int count = 0, slot = -1;
                for (int i = 0; i < c_slotCount; i++)
                {
                    Effect other = g_effects[i];
                    if (other.m_type != p_type) continue;
                    if (other.m_active)
                    {
                        bool crowds = Host.Effects.Crowds(other, info, p_at);
                        if (crowds)
                        {
                            count++;
                            // The detail toggle is each machine's own: not in lockstep (an effect damages). Pending the
                            // user's decision on lockstep's option overrides.
                            if (Host.Fx.LowDetail || count > 2) return;
                        }
                    }
                    else if (slot < 0 && (!info.m_needsObject || other.m_object != null || Host.Net.NetRules))
                    {
                        slot = i;
                    }
                }

                if (slot < 0 && Host.Net.NetRules) slot = Claim(p_type);
                if (slot < 0) return;

                Effect effect = g_effects[slot];
                effect.m_active = true;
                effect.m_position = p_at;
                effect.m_timeLeft = info.m_duration + Clock.g_deltaTime;
                effect.m_owner = p_owner;
                Host.Effects.Spawned(effect, info, p_hit, ref p_at);
                if (effect.m_object != null)
                {
                    if (p_type >= 0x17 && g_launchEffectFiringObj != null)
                    {
                        Host.Fx.PlaceAtFiringObj(g_launchEffectFiringObj, effect.m_object);
                        g_launchEffectFiringObj = null;
                    }
                    else
                    {
                        effect.m_object.SetRotation(Quaternion.Euler(p_angles));
                        effect.m_object.SetPosition(p_at.ToFloat());
                    }

                    effect.m_object.Show(true);
                    Host.Fx.BoostEffect(effect.m_object, effect.m_type);
                    if (effect.m_animation != -1)
                    {
                        Host.Fx.SetAnimFrame(effect.m_animation, 0);
                        Host.Fx.SetAnimMode(effect.m_animation, 2);
                    }
                }

                // The game's light of the effect (its type's, or a shot's own impact look, g_impactFx).
                Host.Fx.LightEffect(impactFx, p_type, effect.m_object, p_at, p_camera, info.m_camera, info.m_duration, s_lightKelvin, p_hit);

                if (info.m_camera && Host.Fx.EffectCameraEnabled)
                {
                    // The effect camera: the closest such effect takes it (and with it the palette's flash).
                    if (Host.Fx.TakeEffectCamera(slot, p_camera, d))
                    {
                        effect.m_hasCamera = true;
                        int flash = LocalInfo(p_type).m_flash;
                        if (flash > -1) ScreenFlash.StartPaletteFlash(flash, info.m_duration, 1);
                    }
                }
            }

            if (chain) SpawnEffect(p_owner, chainType, p_at, p_camera);

            EffectInfo sounding = LocalInfo(p_type);
            if (sounding.m_sound > 0 && (sounding.m_soundChance == -1 || SimRandom.Cosmetic.RandomIntBelow(100) < sounding.m_soundChance))
            {
                // Half as loud in the cockpit view (GetViewMode 0), unless quiet.
                bool half = Eyepoint.g_inCockpitView && !quiet;
                EffectSound.PlaySoundAt(d, sounding.SoundId, half, g_explosionSound);
            }
        }

        // Shows up to p_count free debris slots (type 0xb; all when 0) at p_at (16.16 units), thrown.
        public static void ScatterDebris(FixVec p_at, int p_count)
        {
            int shown = 0;
            if (p_count == 0) p_count = c_slotCount;

            for (int i = 0; i < c_slotCount && shown < p_count; i++)
            {
                Effect effect = g_effects[i];
                if (!effect.m_active && effect.m_type == 0xb && effect.m_object != null)
                {
                    ISceneObject obj = effect.m_object;
                    int piece = Debris.AddDebrisPiece(obj, 1, p_at);
                    if (piece != -1)
                    {
                        effect.m_active = true;
                        effect.m_timeLeft = Info[0xb].m_duration;
                        Debris.ThrowDebrisPiece(piece);
                        obj.Show(true);
                        shown++;
                    }
                }
            }
        }

        // ---- Updating -----------------------------------------------------------------------------------------------------

        // Counts down the effects and frees them when their time runs out, giving the camera back if one had it; hides
        // the animated faces of those whose animation has played out.
        public static void UpdateEffects()
        {
            if (g_effects[0] == null) return;
            UpdateNuke();
            if (SimMain.g_difficulty.m_heatTracking) HeatMechsNearFires();

            for (int i = 0; i < c_slotCount; i++)
            {
                Effect effect = g_effects[i];
                if (!effect.m_active) continue;

                EffectInfo info = Info[effect.m_type];
                if (Host.Effects.UpdateDamage(effect, info)) { }
                else if (info.m_damages && DamageRadius(effect, out long radius))
                {
                    if (SimMain.g_difficulty.m_splashDamage) Mech.DamageMechsInRadius(effect.m_owner, effect.m_position, radius, 256);
                    if (SimMain.g_world != null) Shots.DamageThingsInRadius(SimMain.g_world, effect.m_owner, effect.m_position, radius, 256);
                    Debris.DamageChunksInRadius(effect.m_position, radius, 256);
                }

                effect.m_timeLeft -= Clock.g_deltaTime;
                if (effect.m_timeLeft > 0) continue;

                if (effect.m_hasCamera)
                {
                    // The camera's effect lasts while the palette still fades.
                    if (ScreenFlash.GetPaletteFadeSteps() != 0) continue;
                    effect.m_hasCamera = false;
                    Host.Fx.ReleaseEffectCamera();
                }

                effect.m_object?.Show(false);
                if (effect.m_animation > 0)
                {
                    Host.Fx.SetAnimMode(effect.m_animation, 0);
                    Host.Fx.SetAnimFrame(effect.m_animation, 0);
                }

                ResetEffectSlot(i);
            }

            Host.Fx.ShowAnimatedFaces();
        }

        // ---- Wrecks and nukes ---------------------------------------------------------------------------------------------

        // The game's fireball peak temperature for the effect SpawnEffectEx is lighting (0: its type's light's).
        static float s_lightKelvin;

        // A wrecked mech's smoke (fadepal.c EmitWreckSmoke): once as it falls (p_stateTime 0), then now and then a spark or
        // a puff near it. Its reactor's fireball peaks at p_kelvin (the game's light).
        // Its draws are the gameplay stream's: the sparks (type 3) damage what is near them.
        public static void EmitWreckSmoke(int p_killer, Vector3Int p_units, int p_stateTime, float p_kelvin = 0)
        {
            FixVec at = FixVec.FromUnits(p_units);
            if (p_stateTime == 0)
            {
                s_lightKelvin = p_kelvin;
                SpawnExplosion(p_killer, 0xd, at, at);
                s_lightKelvin = 0;
            }
            else if (SimRandom.RandomIntBelow(100) <= 20)
            {
                at.X += (long)SimRandom.RandomNormal() << 15; // RandomNormal() / 2
                at.Z += (long)SimRandom.RandomNormal() << 15;
                if (SimRandom.RandomIntBelow(100) < 0x3c) SpawnEffect(p_killer, 3, at, at);
                else SpawnEffect(p_killer, 0x10b, at, at);
            }
        }

        // Sets off a nuke at p_at: the palette's fade, the effect, and the blast that grows over the next ticks
        // (UpdateNuke).
        public static void StartNuke(Vector3Int p_at)
        {
            int duration = Info[0x16].m_duration * 2;
            g_nukeMaxRadius = 400000L << 16;
            // The host's fixed nuke radius (DamageRadius's).
            if (Host.Effects.FixedRadius(0x16, out float nukeRadius))
                g_nukeMaxRadius = Fixed.FromFloatL(nukeRadius * 100);
            else for (int i = c_slotCount - 1; i >= 0; i--)
            {
                Effect effect = g_effects[i];
                if (effect.m_type == 0x16 && effect.m_object != null)
                {
                    g_nukeMaxRadius = Fixed.FromFloatL(effect.m_object.Radius * 100);
                    break;
                }
            }

            g_nukePosition = FixVec.FromUnits(p_at);
            g_nukeTimeLeft = duration;
            g_nukeRadius = 0;
            ScreenFlash.StartPaletteFade(0x12, duration, 2);
            SpawnExplosion(-2, 0x16, g_nukePosition, g_nukePosition);
        }

        // Grows the nuke's blast, which damages the mechs, the game things and the chunks within it.
        static void UpdateNuke()
        {
            if (g_nukeTimeLeft <= 0) return;
            g_nukeRadius += (long)Clock.g_deltaTime * 200 << 16;
            if (g_nukeRadius > g_nukeMaxRadius) g_nukeRadius = g_nukeMaxRadius;
            if (Host.Effects.UpdateNuke(g_nukePosition, g_nukeRadius)) { }
            else
            {
                Mech.DamageMechsInRadius(-1, g_nukePosition, g_nukeRadius, 0x1000);
                if (SimMain.g_world != null) Shots.DamageThingsInRadius(SimMain.g_world, -1, g_nukePosition, g_nukeRadius, 0x1000);
                Debris.DamageChunksInRadius(g_nukePosition, g_nukeRadius, 0x1000);
            }
            g_nukeTimeLeft -= Clock.g_deltaTime;
        }

        // HeatMechsNearFires: each fire (a shape of kind 0x10) heats the mechs within twice its radius (and theirs),
        // more the nearer they stand. (The shapes' order doesn't matter: the heats only add up.)
        static void HeatMechsNearFires()
        {
            foreach (Shape shape in ShapeCollision.AllShapes)
            {
                if (shape.KindType != 0x10 || !shape.InScene) continue;
                long radius = shape.FxRadius * 2; // 16.16 units
                FixVec center = shape.FxCenter;
                foreach (Mech mech in SimMain.g_mechs)
                {
                    if (mech == null || (mech.PlayerFlags & 2) != 0 || (mech.PlayerFlags & 4) != 0) continue;
                    long reach = ((long)mech.m_radius << 16) + radius;
                    long distance = (FixVec.FromUnits(mech.ObjectPosition) - center).ApproxLength;
                    if (distance < reach)
                    {
                        int heat = (int)((radius - radius * distance / reach) / 65536) >> 3;
                        mech.m_deltaHeat += heat * Clock.g_deltaTime;
                    }
                }
            }
        }
    }
}
