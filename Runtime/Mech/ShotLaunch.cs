using UnityEngine;

namespace Demech2
{
    // The mech's side of the shots (shots.c's slots): a launched shot taken from the free slots and sent along the
    // mech's aim from its hardpoint (SpawnShot's launch, Titanium's), the anti-missile system's watch over the missiles
    // in flight and its intercepts.
    public sealed partial class Mech
    {
        // A shot of p_def's type launched from the current hardpoint: a free slot of its type, its age, life, effect,
        // damage, heat, target and shooter, its velocity along the aim (an AI's aim distance taken first), its gravity; GBL
        // puts the weapon's flags on it and restarts the flamer's animations; a missile leaves spread.
        void LaunchShot(WeaponSlot p_slot, WeaponDef p_def)
        {
            int index = Shots.FindFree(p_def.m_shotType);
            if (index < 0) return;
            Shot shot = Shots.g_shots[index];
            if (!IsPilot) UpdateAimDistance();
            shot.m_age = p_slot.m_time;
            shot.m_lifetime = p_def.m_lifetime - p_slot.m_time;
            shot.m_impact = p_def.m_impact;
            shot.m_damage = p_def.m_damageFx;
            shot.m_heat = p_def.m_shotHeatFx;
            shot.m_flags = 1;
            shot.m_unk0x3c = 0;
            shot.m_target = p_slot.m_target;
            shot.m_targetKind = p_slot.m_targetKind;
            shot.m_shooter = m_player.m_index;
            shot.m_weapon = p_slot.m_type;
            if (GblWeapons)
            {
                // GBL (MW2.flat 0x53820): the weapon's flags on the shot (NARC, the torpedo's steering) and the flamer's
                // eight animated textures restarted.
                shot.m_defFlags = p_def.m_flags;
                if ((p_def.m_flags & WeaponDef.FlagFlameAnim) != 0)
                {
                    for (int k = 0; k < 8; k++)
                    {
                        Host.Fx.SetAnimFrame(p_def.m_animSlot + k, 0);
                        Host.Fx.SetAnimMode(p_def.m_animSlot + k, 1);
                    }
                }
            }
            FixVec dir = GetMechAimDirection().NormalizeGuarded();
            shot.m_velocity = new FixVec(dir.X * p_def.m_speed, dir.Y * p_def.m_speed, dir.Z * p_def.m_speed);
            shot.m_steering = new FixVec(0, -(p_def.m_gravity * Debris.g_gravity), 0);
            // GBL: missiles are 3, 4, 9 (torpedo), 10 (Arrow IV) and 12 (NARC).
            bool missile = GblWeapons ? Shots.IsMissile(shot.m_type) : shot.m_type == 3 || shot.m_type == 4;
            shot.m_position = PlaceLaunchedShot(shot.m_object, Host.Fx.ShotLaunchRadius(shot), dir, missile);
            shot.m_object.Show(true);
            if (IsLocal) Shots.g_lastLocalMissile = missile ? index : -1;
        }

        // Titanium's launch (0x1006c804): a free slot of the type (the edition's own shape), the def's flags on the shot,
        // the flamer's animations restarted (the edition's own), a missile type (3, 4, 9, 10, 12) launched spread.
        void LaunchShotTitanium(WeaponSlot p_slot, WeaponDef p_def)
        {
            int index = Shots.FindFree(p_def.m_shotType, WeaponDef.Local(p_slot.m_type).m_shotType);
            if (index < 0) return;

            Shot shot = Shots.g_shots[index];
            if (!IsPilot) UpdateAimDistance();
            shot.m_defFlags = p_def.m_flags;
            shot.m_age = p_slot.m_time;
            shot.m_lifetime = p_def.m_lifetime - p_slot.m_time;
            shot.m_impact = p_def.m_impact;
            shot.m_damage = p_def.m_damageFx;
            shot.m_heat = p_def.m_shotHeatFx;
            shot.m_flags = 1;
            shot.m_unk0x3c = 0;
            shot.m_target = p_slot.m_target;
            shot.m_targetKind = p_slot.m_targetKind;
            shot.m_shooter = m_player.m_index;
            shot.m_weapon = p_slot.m_type;
            // The flamer's animations: the edition's own (WeaponDef.Local).
            WeaponDef anim = WeaponDef.Local(p_slot.m_type);
            if ((anim.m_flags & WeaponDef.FlagFlameAnim) != 0)
            {
                for (int k = 0; k < 8; k++)
                {
                    Host.Fx.SetAnimFrame(anim.m_animSlot + k, 0);
                    Host.Fx.SetAnimMode(anim.m_animSlot + k, 1);
                }
            }

            FixVec dir = GetMechAimDirection().NormalizeGuarded();
            shot.m_velocity = new FixVec(dir.X * p_def.m_speed, dir.Y * p_def.m_speed, dir.Z * p_def.m_speed);
            shot.m_steering = new FixVec(0, -(p_def.m_gravity * Debris.g_gravity), 0);
            bool missile = Shots.IsMissile(shot.m_type);
            shot.m_position = PlaceLaunchedShot(shot.m_object, Host.Fx.ShotLaunchRadius(shot), dir, missile, (p_def.m_flags & WeaponDef.FlagAtMuzzle) != 0);
    #if UNITY_EDITOR || MEKENGINE_MERCS
            if (WeaponDef.NativeMercs)
            {
                // Mercenaries (0x10062775): a bullet (type 5) leaves a tracer from the shot's place; a volley's first shot
                // carries a light (FUN_1007d03e, range 10000).
                if (shot.m_type == 5) Host.Fx.SpawnTracer(shot.m_position.ToFloat(), shot.m_velocity.ToFloat());
                shot.m_volleyLight = m_firstShotOfVolley;
            }
    #endif

            shot.m_object.Show(true);
            if (IsLocal) Shots.g_lastLocalMissile = missile ? index : -1;
        }

        // A launched shot's object at the hardpoint, pointing along p_dir (a missile up to 100 units off the line,
        // p_spread), moved on by twice its radius (not Titanium's flamer, p_atMuzzle: PlaceShotObject's flag 0x40). Returns
        // where it is (16.16 units). p_radius: the shot's (Host.Fx.ShotLaunchRadius).
        FixVec PlaceLaunchedShot(ISceneObject p_obj, float p_radius, FixVec p_dir, bool p_spread, bool p_atMuzzle = false)
        {
            int yaw = Fixed.Heading(p_dir.X, p_dir.Z);
            int pitch = Fixed.Asin(p_dir.Y);
            p_obj.SetRotation(Quaternion.Euler(-pitch / 65536f, yaw / 65536f, 0));
            FixVec at = FiringPosition;
            if (p_spread)
            {
                int offset = SimRandom.RandomIntBelow(200) - 100;
                FixVec side = new FixVec(-p_dir.Z, 0, p_dir.X).NormalizeGuarded();
                at.X += offset * side.X;
                at.Z += offset * side.Z;
                var up = new FixVec(Fixed.Mul16L(p_dir.Y, side.Z), Fixed.Mul16L(p_dir.Z, side.X) + Fixed.Mul16L(p_dir.X, side.Z),
                    -Fixed.Mul16L(p_dir.Y, side.X));
                at += new FixVec(offset * up.X, offset * up.Y, offset * up.Z);
            }

            if (!p_atMuzzle) at += p_dir.Mul(Fixed.FromFloatL(p_radius * 2));
            p_obj.SetPosition(at.ToFloat());
            return at;
        }

        // GBL's AMS (MW2.flat 0x54940 each tick while ready, 0x54bb0 the intercept): Titanium's with the approximate
        // distance.
        void AmsUpdateGbl(WeaponSlot p_slot, WeaponDef p_def)
        {
            if (p_slot.m_state != WeaponSlot.Ready || Shots.g_shots[0] == null) return;
            int side = SimMain.PlayerSide(m_player.m_index);
            foreach (Shot shot in Shots.g_shots)
            {
                if (shot == null || shot.m_flags == 0 || shot.m_object == null || !Shots.IsMissile(shot.m_type)) continue;
                if (SimMain.SameSide(m_player.m_index, shot.m_shooter, side)) continue;
                FixVec d = FixVec.FromUnits(ObjectPosition) - shot.m_position;
                if (d.ApproxLength < 50001L << 16 && FixVec.Dot(shot.m_velocity, d) > 0)
                {
                    p_slot.m_state = WeaponSlot.Firing;
                    p_slot.m_volley = p_def.m_volley;
                    p_slot.m_time = 0;
                    p_slot.m_target = p_slot.m_targetKind = 0;
                    DodgeShot(p_slot);
                }
            }
        }

        void AmsInterceptGbl()
        {
            int side = SimMain.PlayerSide(m_player.m_index);
            foreach (Shot shot in Shots.g_shots)
            {
                if (shot == null || shot.m_flags == 0 || shot.m_object == null || !Shots.IsMissile(shot.m_type)) continue;
                if (SimMain.SameSide(m_player.m_index, shot.m_shooter, side)) continue;
                FixVec d = FixVec.FromUnits(ObjectPosition) - shot.m_position;
                long distance = d.ApproxLength;
                if (distance >= 50001L << 16) continue;
                RollAmsDistance(shot, d, distance);
                if (shot.m_amsDistance > 0 && distance <= (long)shot.m_amsDistance << 16) shot.m_flags |= Shots.c_proximityFuse;
            }
        }

        // Titanium's AMS watch (0x1000cd45), each tick while it's ready: an enemy missile (3, 4, 9, 10, 12; its shooter on
        // another side than this mech, as the local player sees sides) within 50000 and closing starts a volley.
        void AmsUpdate(WeaponSlot p_slot, WeaponDef p_def)
        {
            if (p_slot.m_state != WeaponSlot.Ready || Shots.g_shots[0] == null) return;
            int side = SimMain.PlayerSide(m_player.m_index);
            foreach (Shot shot in Shots.g_shots)
            {
                if (shot.m_flags == 0 || shot.m_object == null || !Shots.IsMissile(shot.m_type)) continue;
                if (SimMain.SameSide(m_player.m_index, shot.m_shooter, side)) continue;
                FixVec d = FixVec.FromUnits(ObjectPosition) - shot.m_position;
                // Mercenaries (0x1000f651): the approximate distance below 50001.
    #if UNITY_EDITOR || MEKENGINE_MERCS
                bool within = MercsWeapons ? d.ApproxLength < 50001L << 16 : d.Length <= 50000L << 16;
    #else
                bool within = d.Length <= 50000L << 16;
    #endif
                if (within && FixVec.Dot(shot.m_velocity, d) > 0)
                {
                    p_slot.m_state = WeaponSlot.Firing;
                    p_slot.m_volley = p_def.m_volley;
                    p_slot.m_time = 0;
                    p_slot.m_target = p_slot.m_targetKind = 0;
                    DodgeShot(p_slot);
                }
            }
        }

        // Titanium's intercept (0x1000d13b): each enemy missile within 50000 rolls once, while its intercept distance is
        // below 0: 76% a distance of 2500 plus a random multiple of 100 short of it, else 0 (and never again); within that
        // distance its proximity fuse goes off.
        void AmsIntercept()
        {
            int side = SimMain.PlayerSide(m_player.m_index);
            foreach (Shot shot in Shots.g_shots)
            {
                if (shot.m_flags == 0 || shot.m_object == null || !Shots.IsMissile(shot.m_type)) continue;
                if (SimMain.SameSide(m_player.m_index, shot.m_shooter, side)) continue;
                FixVec d = FixVec.FromUnits(ObjectPosition) - shot.m_position;
                // Mercenaries (0x1000fa08): the approximate distance below 50001.
    #if UNITY_EDITOR || MEKENGINE_MERCS
                long distance = MercsWeapons ? d.ApproxLength : d.Length;
                if (MercsWeapons ? distance >= 50001L << 16 : distance > 50000L << 16) continue;
    #else
                long distance = d.Length;
                if (distance > 50000L << 16) continue;
    #endif
                RollAmsDistance(shot, d, distance);
                if (shot.m_amsDistance > 0 && distance <= (long)shot.m_amsDistance << 16) shot.m_flags |= Shots.c_proximityFuse;
            }
        }

        // The intercept distance's roll (16.16 units p_distance away along p_d), while the shot has none (below 0): 76% a
        // distance of 2500 plus a random multiple of 100 short of it, else 0 (and never again).
        static void RollAmsDistance(Shot p_shot, FixVec p_d, long p_distance)
        {
            if (p_shot.m_amsDistance >= 0) return;
            long beyond = p_distance - (2500L << 16);
            if (beyond <= 0) p_shot.m_amsDistance = 0;
            else if (FixVec.Dot(p_shot.m_velocity, p_d) > 0)
            {
                if (SimRandom.RandomIntBelow(100) < 0x4c)
                {
                    p_shot.m_amsDistance = beyond >= 100L << 16
                        ? SimRandom.RandomIntBelow((int)(beyond / (100L << 16))) * 100 + 2500
                        : 2500;
                }
                else p_shot.m_amsDistance = 0;
            }
        }

        // The chassis' tech base (Inner Sphere or Clan), once.
        public bool InnerSphere => m_innerSphere ??= Host.Variant.IsInnerSphere(m_player?.m_chassis);
    }
}
