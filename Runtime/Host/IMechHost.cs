using UnityEngine;

namespace Demech2
{
    // What a mech asks of its own scene objects (the game's MechPilot): the pose written to its objects, its parts'
    // shapes and debris (classtable.c, object.c), its animation player, and the regenerate's remembered tree.
    public interface IMechHost
    {
        // SetObjPosition/SetObjRotation of the mech's object, and its torso (p_snap: no render interpolation across
        // this step); the pitch object aimed where the weapons converge; a door's object.
        void PoseMech(bool p_snap);
        void PoseTorso();
        void AimPitchObject();
        void PoseDoor();

        // Not in the original: the local torso's live ramps, carried to this step (UpdateMech uses them instead of
        // its own step when true).
        bool TakeLiveTorso();

        // The legs' animation player (gpanim.c), or null.
        MotionPlayer Motion { get; }

        // The part shape of p_obj (GetObjShape), or null; FindObjByPart, RaisePartDamageLevel, BlowOffPart over the
        // mech's tree; KnockMechOver's whole tree blown off; LoadClassLevel(index, 1)'s hidden sectionless parts.
        Shape PartOf(Transform p_obj);
        Shape RootShape { get; }
        Transform FindObjByPart(int p_partId);
        void RaisePartDamageLevel(int p_level, int p_partId);
        void BlowOffPart(int p_partId);
        void BlowOffMech();
        void HideSectionlessParts();

        // mechreload.c: a start remembered (RememberMechSegments), RestoreMechSegments, and the start p_spawn's mech
        // remembered (else this one's).
        bool HasRememberedStart { get; }
        void RestoreMechSegments();
        void GetRememberedStart(Mech p_spawn, out long p_x, out long p_y, out long p_z, out int p_heading);
    }
}
