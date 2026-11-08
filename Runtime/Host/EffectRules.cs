namespace Demech2
{
    // The effects' rules a host may change (Host.Effects): the table, the crowding, a spawned effect's additions, the
    // damage while it lasts and the nuke's. Each default is demech2's.
    public class EffectRules
    {
        // The effect types in use, how many, and the entry whose sound and palette flash the running game plays.
        public virtual Effects.EffectInfo[] Info => Effects.BaseInfo;
        public virtual int TypeCount => 0x20;
        public virtual Effects.EffectInfo LocalInfo(int p_type) => Effects.Info[p_type];

        // SpawnEffectEx: an active effect of the type near p_at (16.16 units) crowds the new one out (three at most).
        public virtual bool Crowds(Effects.Effect p_other, Effects.EffectInfo p_info, FixVec p_at) =>
            p_other.m_type < 0x17 && (p_other.m_position - p_at).ApproxLength < 500L << 16;

        // A slot's host data reset (ResetEffectSlot); a spawned effect's additions before it's placed (p_at may move).
        public virtual void ResetSlot(Effects.Effect p_effect) { }
        public virtual void Spawned(Effects.Effect p_effect, Effects.EffectInfo p_info, Shape p_hit, ref FixVec p_at) { }

        // A fixed damage radius for an effect type (units) instead of its object's.
        public virtual bool FixedRadius(int p_type, out float p_radius)
        {
            p_radius = 0;
            return false;
        }

        // UpdateEffects' damage while the effect lasts when the host's (true); UpdateNuke's damage when the host's (true;
        // p_at and p_radius 16.16 units).
        public virtual bool UpdateDamage(Effects.Effect p_effect, Effects.EffectInfo p_info) => false;
        public virtual bool UpdateNuke(FixVec p_at, long p_radius) => false;
    }
}
