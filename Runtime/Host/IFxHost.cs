using UnityEngine;

namespace Demech2
{
    // The game's look of the shots and effects, its camera, palette and cockpit, as the sim drives them.
    public interface IFxHost
    {
        // The real clock, ticks (the camera's shake runs on it).
        float RenderTicks { get; }

        // SetAnimFrame, SetAnimMode of an animated texture.
        void SetAnimFrame(int p_animation, int p_frame);
        void SetAnimMode(int p_animation, int p_mode);

        // ---- The shots' look ----

        // FirstShots: the shapes' templates, the tracers and the pending booyowthing shapes gone.
        void ResetShotLooks();

        // A new slot of shot type p_type with its shape (p_visualType's when the world has none of p_type); -1 without
        // one.
        int GrowShot(int p_type, int p_visualType);

        // The radius a launch moves p_shot on by (twice it): its object's, or the game's.
        float ShotLaunchRadius(Shot p_shot);

        // The impact look of weapon p_weapon's shot (passed back to LightEffect), or null.
        object ImpactFxFor(int p_weapon);

        // A bullet's tracer from p_at (units) at p_velocity; the tracers' step.
        void SpawnTracer(Vector3 p_at, Vector3 p_velocity);
        void UpdateTracers();

        // ---- The effects' look ----

        // FirstEffects: the templates and pending xplode shapes gone, the effect camera free.
        void ResetEffectLooks();

        // The effect type the world shows for p_type; a slot a networked match claimed given its look (and its
        // animation).
        int EffectVisualType(int p_type);
        void ClaimEffectLook(int p_slot, int p_type);

        // g_eyepoint, world units.
        Vector3 Eyepoint { get; }

        // Not the original's: the pause banner's detail toggle lowered (g_lodQuality), outside lockstep.
        bool LowDetail { get; }

        // PlaceAtFiringObj: p_obj at the firing object's turn and place.
        void PlaceAtFiringObj(Transform p_firingObj, ISceneObject p_obj);

        // Not the original's: an effect shown brightened for its type; its light (its type's, or a shot's impact look
        // p_impactFx); p_followed: the effect camera may follow it.
        void BoostEffect(ISceneObject p_obj, int p_type);
        void LightEffect(object p_impactFx, int p_type, ISceneObject p_obj, FixVec p_at, FixVec p_camera, bool p_followed,
            int p_duration, float p_kelvin, Shape p_hit);

        // g_effectCameraEnabled; the effect camera taken by slot p_slot when it's free or this one is closer (p_d: from
        // it to the eye), its light at p_camera; given back.
        bool EffectCameraEnabled { get; }
        bool TakeEffectCamera(int p_slot, FixVec p_camera, Vector3 p_d);
        void ReleaseEffectCamera();

        // DrawAnimatedPolygon's stopped animations: an effect's animated faces hidden once it has played through.
        void ShowAnimatedFaces();

        // CollideWithBuildings' chunk (kind 0x50): the object it flies as (its piece's, else its shape's own).
        ISceneObject ChunkObject(Shape p_shape);
    }
}
