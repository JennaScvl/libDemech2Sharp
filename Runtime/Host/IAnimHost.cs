using UnityEngine;

namespace Demech2
{
    // What the animation code (gpanim.c, objectanim.c) asks of the game: the scene objects it moves, the reels it plays
    // and the sounds it names.
    public interface IAnimHost
    {
        // The running install's sound id for a DOS MW2.PRJ SNDS id.
        int MapSoundId(int p_dosId);

        // MoveObj / RotateObj on a scene object, in whole units and 16.16 degrees (object.c).
        void MoveObj(Transform p_obj, int p_x, int p_y, int p_z);
        void RotateObj(Transform p_obj, int p_angleX, int p_angleY, int p_angleZ);

        // SetObjPosition / MoveObj / SetObjRotation / RotateObj in 16.16 units and degrees.
        void SetObjPositionFixed(Transform p_obj, long p_x, long p_y, long p_z);
        void MoveObjFixed(Transform p_obj, long p_x, long p_y, long p_z);
        void SetObjRotationFixed(Transform p_obj, long p_x, long p_y, long p_z);
        void RotateObjFixed(Transform p_obj, long p_x, long p_y, long p_z);

        // GetObjPosition (16.16 units, the object's sim node); its world heading (16.16 degrees); a point p_local
        // (16.16 units) in its frame, in the world's.
        FixVec GetObjPosition(Transform p_obj);
        int GetObjWorldYaw(Transform p_obj);
        FixVec GetObjWorldPoint(Transform p_obj, FixVec p_local);

        // Not in the original: p_obj jumped (no render interpolation across this step).
        void SnapObj(Transform p_obj);

        // g_reels[p_number], g_animBase, g_maxAnimNumber (objectanim.c LoadAnimFile).
        Reel GetReel(int p_number);
        int AnimBase { get; }
        int MaxAnimNumber { get; set; }

        // The distance from the eye to p_obj in units, and GetDistanceVolume (soundfx.c).
        float EyeDistance(Transform p_obj);
        int GetDistanceVolume(int p_distance);
    }

    // A player's sound output: PlaySoundAt(eyepoint offset, sound, g_inCockpitView).
    public interface IMotionSoundHost
    {
        void PlaySoundAt(int p_sound);
    }

    // An ambient sound's samples (soundfx.c): one source of SNDS resources, and the looping sample it plays.
    public interface IAmbientSoundHost
    {
        // FindResourceIdByName(0xb, p_name).
        int FindSound(string p_name);

        // A looping sample of sound p_id on p_obj, silent at p_range units; null if the sound has no data.
        IAmbientSample CreateSample(Transform p_obj, int p_id, int p_range);
    }

    public interface IAmbientSample
    {
        // False once the sample is gone.
        bool IsAlive { get; }

        // The volume and a random rate set, and played if p_start or not playing.
        void Play(bool p_start);

        void Destroy();
    }

    // A color cycle's shape (objectanim.c ColorCycleTask): its object, and its faces' colors.
    public interface IColorCycleHost
    {
        // The star p_id's object, null if none.
        Transform FindObject(int p_id);

        // Readies the shape's faces for their own colors; its face count, or -1 until it can.
        int PrepareFaces(int p_shapeId, Transform p_obj, System.Collections.Generic.List<int> p_colors);

        // SetFaceColor with color word (p_entry << 4), draw mode 0.
        void SetFaceColor(int p_face, int p_entry);
    }

    // The loading screen's page (supanim.c): its backdrop and dropship shapes and palette.
    public interface ISupAnimHost
    {
        bool HasShape { get; }

        // VFX_shape_frame_count of the dropship.
        int ShapeFrameCount();

        // VFX_shape_draw of the backdrop (p_dropship false) or dropship at p_x, p_y.
        void DrawShape(bool p_dropship, int p_frame, int p_x, int p_y);

        // VFX_shape_palette of the backdrop's frame 0.
        void ShapePalette(byte[] p_palette);

        // The page cleared to entry 0.
        void ClearPage();

        // p_palette's 6-bit entries realized and the page drawn through them; null redraws with the last.
        void Realize(byte[] p_palette);

        // The real time in seconds.
        float RealTime { get; }

        // The backdrop sets palette entries 0-15 itself (the game's edition decides).
        bool OwnsFirstColors { get; }
    }
}
