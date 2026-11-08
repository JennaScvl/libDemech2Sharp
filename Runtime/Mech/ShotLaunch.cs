using UnityEngine;

namespace Demech2
{
    // The mech's side of the shots (shots.c's slots): a launched shot taken from the free slots and sent along the
    // mech's aim from its hardpoint (SpawnShot's launch).
    public sealed partial class Mech
    {
        // A shot of p_def's type launched from the current hardpoint: a free slot of its type, its age, life, effect,
        // damage, heat, target and shooter, its velocity along the aim (an AI's aim distance taken first), its gravity
        // (and the host's additions, Host.Weapons.ShotLaunched); a missile leaves spread.
        public void LaunchShot(WeaponSlot p_slot, WeaponDef p_def)
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
            Host.Weapons.ShotLaunched(this, shot, p_def);
            FixVec dir = GetMechAimDirection().NormalizeGuarded();
            shot.m_velocity = new FixVec(dir.X * p_def.m_speed, dir.Y * p_def.m_speed, dir.Z * p_def.m_speed);
            shot.m_steering = new FixVec(0, -(p_def.m_gravity * Debris.g_gravity), 0);
            bool missile = Host.Weapons.LaunchesSpread(shot);
            shot.m_position = PlaceLaunchedShot(shot.m_object, Host.Fx.ShotLaunchRadius(shot), dir, missile);
            shot.m_object.Show(true);
            if (IsLocal) Shots.g_lastLocalMissile = missile ? index : -1;
        }

        // A launched shot's object at the hardpoint, pointing along p_dir (a missile up to 100 units off the line,
        // p_spread), moved on by twice its radius (unless p_atMuzzle). Returns where it is (16.16 units). p_radius: the
        // shot's (Host.Fx.ShotLaunchRadius).
        public FixVec PlaceLaunchedShot(ISceneObject p_obj, float p_radius, FixVec p_dir, bool p_spread, bool p_atMuzzle = false)
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

    }
}
