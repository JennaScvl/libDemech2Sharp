using UnityEngine;

namespace Demech2
{
    // A scene object the shots, effects and debris move (object.c's SceneObject): the game's (a shape instance, or a
    // model's part). Positions are world units, angles degrees; its shape's bounds (ComputeShapeBounds) are the center
    // of its vertices' box (object space) and the farthest vertex from it.
    public interface ISceneObject
    {
        // Its GameObject, or null for an instance that has none.
        Transform Transform { get; }

        Vector3 Center { get; }
        float Radius { get; }
        bool Shown { get; }
        Vector3 Position { get; }

        // SetObjPosition, SetObjRotation, ShowObjTree / HideObjTree, DetachObj.
        void SetPosition(Vector3 p_units);
        void SetRotation(Quaternion p_rotation);
        void Show(bool p_shown);
        void Detach();

        // GetShapeBounds: the shape's center in world units; returns its radius.
        float GetBounds(out Vector3 p_center);

        // Its node's world pose (16.16 units, a 2.30 rotation's rows), when it has a node; the pose written to the node
        // (the sim's collision follows), false without one.
        bool GetNodePose(out FixVec p_position, out long[] p_rotation);
        bool SetNodePose(FixVec p_position, long[] p_rotation, Quaternion p_turn);

        // Its collision shape, or null.
        Shape Shape { get; }
    }
}
