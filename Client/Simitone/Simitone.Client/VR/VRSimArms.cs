using System;
using System.Collections.Generic;
using System.Reflection;
using FSO.LotView.Components;
using FSO.Vitaboy;
using Microsoft.Xna.Framework;

namespace Simitone.Client.VR
{
    /// <summary>
    /// Your Sim's arms follow your hands while you steer them from their head: each arm is bent (two-bone IK, shoulder
    /// and elbow) so the wrist reaches the controller, and the hand turns to point the way the controller does. It's
    /// blended in and out by <see cref="VRCameraController.ArmWeight"/>, so during an action the Sim's own animation
    /// plays as usual.
    ///
    /// The pose is applied to the skeleton's matrices after the game has animated them for this frame (they're
    /// rebuilt every frame by <see cref="Avatar.ReloadSkeleton"/>), and only for drawing: the bones' world positions are
    /// moved too, so whatever the Sim holds in their hand stays in it. The game's own skinning matrices are a
    /// protected field of FreeSO's Avatar (a submodule), so they're reached by reflection.
    /// </summary>
    public class VRSimArms
    {
        private static readonly FieldInfo SkelBonesField =
            typeof(Avatar).GetField("SkelBones", BindingFlags.NonPublic | BindingFlags.Instance);

        private class Arm
        {
            public int Shoulder, Elbow, Hand, Finger = -1;
            public int[] UpperSet, LowerSet, HandSet; // the bones each joint moves (itself and everything below it)
            public float SideSign;                    // which side of the body (model X) the arm is on
        }

        private class Rig
        {
            public Skeleton Skeleton;
            public Arm Left, Right;
        }

        private Rig Current;
        private Matrix[] LastArray;
        private Matrix[] Animated = new Matrix[0];
        private Matrix[] AnimatedBones = new Matrix[0];
        private bool LoggedMissing;

        /// <summary>Moves <paramref name="avatar"/>'s arms toward the controllers (call once per frame, before drawing).</summary>
        public void Apply(AvatarComponent avatar, VRCameraController camera)
        {
            var model = avatar?.Avatar;
            var skeleton = model?.Skeleton;
            if (skeleton?.Bones == null || SkelBonesField == null) return;
            if (!(SkelBonesField.GetValue(model) is Matrix[] bones) || bones.Length != skeleton.Bones.Length) return;

            // The game makes a new array each time it animates the skeleton. If it hasn't this frame (paused), put
            // back the pose it animated, so the arms don't bend again on top of last frame's bend.
            var weight = camera.ArmWeight;
            var fresh = !ReferenceEquals(bones, LastArray);
            if (fresh)
            {
                if (weight <= 0) return;
                LastArray = bones;
                if (Animated.Length != bones.Length)
                {
                    Animated = new Matrix[bones.Length];
                    AnimatedBones = new Matrix[bones.Length];
                }
                Array.Copy(bones, Animated, bones.Length);
                for (int i = 0; i < bones.Length; i++) AnimatedBones[i] = skeleton.Bones[i].AbsoluteMatrix;
            }
            else
            {
                Array.Copy(Animated, bones, bones.Length);
                for (int i = 0; i < bones.Length; i++)
                {
                    skeleton.Bones[i].AbsoluteMatrix = AnimatedBones[i];
                    skeleton.Bones[i].AbsolutePosition = AnimatedBones[i].Translation;
                }
                if (weight <= 0) return;
            }

            // world -> the skeleton's own space, as AvatarComponent draws it
            var world = Matrix.CreateRotationY((float)(Math.PI - avatar.RadianDirection)) * avatar.World;
            if (avatar.Scale != 1f) world = Matrix.CreateScale(avatar.Scale) * world;
            var toModel = Matrix.Invert(world);
            Solve(skeleton, bones, weight,
                camera.LeftHandValid ? Vector3.Transform(camera.LeftWrist, toModel) : (Vector3?)null,
                Vector3.TransformNormal(camera.LeftPoint, toModel),
                camera.RightHandValid ? Vector3.Transform(camera.RightWrist, toModel) : (Vector3?)null,
                Vector3.TransformNormal(camera.RightPoint, toModel));
        }

        /// <summary>
        /// The arm pose itself, in the skeleton's own space: bends each arm whose wrist target is given so the wrist
        /// reaches it (as far as the arm reaches) and the hand points along its pointing direction, blended by
        /// <paramref name="weight"/>. Writes <paramref name="bones"/> (the skinning matrices) and the bones' absolute
        /// matrices. False if the skeleton has no arms it recognises. (Also used by tools/vr/ui-check.)
        /// </summary>
        public bool Solve(Skeleton skeleton, Matrix[] bones, float weight, Vector3? leftWrist, Vector3 leftPoint,
            Vector3? rightWrist, Vector3 rightPoint)
        {
            var rig = RigFor(skeleton);
            if (rig == null) return false;
            // which way is out from the body for each shoulder
            var midShoulders = (bones[rig.Left.Shoulder].Translation + bones[rig.Right.Shoulder].Translation) / 2;
            if (leftWrist != null) Reach(bones, skeleton, rig.Left, midShoulders, leftWrist.Value, leftPoint, weight);
            if (rightWrist != null) Reach(bones, skeleton, rig.Right, midShoulders, rightWrist.Value, rightPoint, weight);
            return true;
        }

        private void Reach(Matrix[] bones, Skeleton skeleton, Arm arm, Vector3 midShoulders, Vector3 target, Vector3 pointing, float weight)
        {
            var s = bones[arm.Shoulder].Translation;
            var e = bones[arm.Elbow].Translation;
            var h = bones[arm.Hand].Translation;
            var upper = (e - s).Length();
            var lower = (h - e).Length();
            if (upper < 1e-4f || lower < 1e-4f) return;

            // where the wrist can get to: no further than the arm reaches, no nearer than it folds
            var toTarget = target - s;
            var distance = toTarget.Length();
            if (distance < 1e-4f) return;
            var dir = toTarget / distance;
            distance = MathHelper.Clamp(distance, Math.Abs(upper - lower) + 0.02f * upper, (upper + lower) * 0.999f);
            var wrist = s + dir * distance;

            // the elbow bends down and out, as people's do when they hold something in front of them
            var side = s - midShoulders;
            side.Y = 0;
            side = side.LengthSquared() > 1e-6f ? Vector3.Normalize(side) : new Vector3(arm.SideSign, 0, 0);
            var pole = Perpendicular(-Vector3.UnitY + side * 0.6f, dir);
            if (pole == Vector3.Zero) pole = Perpendicular(side, dir);
            if (pole == Vector3.Zero) pole = Perpendicular(Vector3.UnitZ, dir);
            var along = (distance * distance + upper * upper - lower * lower) / (2 * distance);
            var outward = (float)Math.Sqrt(Math.Max(0, upper * upper - along * along));
            var elbow = s + dir * along + pole * outward;

            // shoulder: swing the upper arm to the new elbow
            var shoulderTurn = Blend(Between(e - s, elbow - s), weight);
            Rotate(bones, skeleton, arm.UpperSet, s, shoulderTurn);
            // elbow: swing the forearm so the wrist reaches
            var e1 = bones[arm.Elbow].Translation;
            var h1 = bones[arm.Hand].Translation;
            var wristGoal = Vector3.Lerp(h1, wrist, weight);
            var elbowTurn = Between(h1 - e1, wristGoal - e1);
            Rotate(bones, skeleton, arm.LowerSet, e1, elbowTurn);
            // hand: point it the way the controller points
            if (arm.Finger >= 0 && pointing.LengthSquared() > 1e-8f)
            {
                var h2 = bones[arm.Hand].Translation;
                var f = bones[arm.Finger].Translation;
                if ((f - h2).LengthSquared() > 1e-8f)
                    Rotate(bones, skeleton, arm.HandSet, h2, Blend(Between(f - h2, Vector3.Normalize(pointing)), weight));
            }
        }

        /// <summary>Turns a set of bones by <paramref name="turn"/> about <paramref name="pivot"/> (skeleton space).</summary>
        private static void Rotate(Matrix[] bones, Skeleton skeleton, int[] set, Vector3 pivot, Quaternion turn)
        {
            if (turn == Quaternion.Identity) return;
            var m = Matrix.CreateTranslation(-pivot) * Matrix.CreateFromQuaternion(turn) * Matrix.CreateTranslation(pivot);
            foreach (var i in set)
            {
                bones[i] = bones[i] * m;
                var bone = skeleton.Bones[i];
                bone.AbsoluteMatrix = bones[i];
                bone.AbsolutePosition = bones[i].Translation;
            }
        }

        /// <summary>The smallest rotation taking direction <paramref name="from"/> to direction <paramref name="to"/>.</summary>
        private static Quaternion Between(Vector3 from, Vector3 to)
        {
            if (from.LengthSquared() < 1e-10f || to.LengthSquared() < 1e-10f) return Quaternion.Identity;
            from.Normalize();
            to.Normalize();
            var dot = MathHelper.Clamp(Vector3.Dot(from, to), -1, 1);
            if (dot > 0.99999f) return Quaternion.Identity;
            var axis = Vector3.Cross(from, to);
            if (axis.LengthSquared() < 1e-10f)
            {
                axis = Perpendicular(Vector3.UnitY, from);
                if (axis == Vector3.Zero) axis = Perpendicular(Vector3.UnitX, from);
            }
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), (float)Math.Acos(dot));
        }

        private static Quaternion Blend(Quaternion turn, float weight)
            => weight >= 1 ? turn : Quaternion.Slerp(Quaternion.Identity, turn, weight);

        /// <summary>The part of <paramref name="v"/> at right angles to <paramref name="axis"/> (unit), or zero.</summary>
        private static Vector3 Perpendicular(Vector3 v, Vector3 axis)
        {
            var p = v - axis * Vector3.Dot(v, axis);
            return p.LengthSquared() > 1e-6f ? Vector3.Normalize(p) : Vector3.Zero;
        }

        // ------------------------------------------------------------------ finding the arms

        private Rig RigFor(Skeleton skeleton)
        {
            if (Current != null && Current.Skeleton == skeleton) return Current.Left != null ? Current : null;
            Current = new Rig { Skeleton = skeleton };
            var index = new Dictionary<Bone, int>();
            for (int i = 0; i < skeleton.Bones.Length; i++) index[skeleton.Bones[i]] = i;
            Current.Left = FindArm(skeleton, index, "L_HAND", 1);
            Current.Right = FindArm(skeleton, index, "R_HAND", -1);
            if (Current.Left == null || Current.Right == null)
            {
                Current.Left = Current.Right = null;
                if (!LoggedMissing) SimitoneVR.Log("Sim hands: this skeleton has no arms I recognise; the arms keep their animation");
                LoggedMissing = true;
                return null;
            }
            return Current;
        }

        /// <summary>
        /// The wrist is the hand bone; the elbow and shoulder are the first two bones above it that are some way
        /// apart (a skeleton can have extra bones in the chain, such as a twist bone in the forearm).
        /// </summary>
        private static Arm FindArm(Skeleton skeleton, Dictionary<Bone, int> index, string handName, float side)
        {
            var hand = skeleton.GetBone(handName);
            if (hand == null || !index.ContainsKey(hand)) return null;
            var arm = new Arm { Hand = index[hand], SideSign = side };
            var elbow = Joint(skeleton, hand, hand.AbsolutePosition);
            var shoulder = elbow == null ? null : Joint(skeleton, elbow, elbow.AbsolutePosition);
            if (shoulder == null) return null;
            arm.Elbow = index[elbow];
            arm.Shoulder = index[shoulder];
            // the finger bone the hand points along (the one furthest out)
            Bone finger = null;
            foreach (var child in hand.Children ?? new Bone[0])
                if (finger == null || (child.AbsolutePosition - hand.AbsolutePosition).LengthSquared()
                    > (finger.AbsolutePosition - hand.AbsolutePosition).LengthSquared()) finger = child;
            if (finger != null && (finger.AbsolutePosition - hand.AbsolutePosition).LengthSquared() > 1e-6f) arm.Finger = index[finger];
            arm.UpperSet = Below(shoulder, index);
            arm.LowerSet = Below(elbow, index);
            arm.HandSet = Below(hand, index);
            return arm;
        }

        private static Bone Joint(Skeleton skeleton, Bone from, Vector3 position)
        {
            var bone = Parent(skeleton, from);
            while (bone != null)
            {
                var gap = (bone.AbsolutePosition - position).Length();
                if (gap > 0.15f) return bone; // (world units: a few centimetres)
                bone = Parent(skeleton, bone);
            }
            return null;
        }

        private static Bone Parent(Skeleton skeleton, Bone bone)
            => string.IsNullOrEmpty(bone.ParentName) || bone.ParentName == "NULL" ? null : skeleton.GetBone(bone.ParentName);

        private static int[] Below(Bone top, Dictionary<Bone, int> index)
        {
            var result = new List<int>();
            var stack = new Stack<Bone>();
            stack.Push(top);
            while (stack.Count > 0)
            {
                var bone = stack.Pop();
                if (index.TryGetValue(bone, out var i)) result.Add(i);
                if (bone.Children != null) foreach (var child in bone.Children) stack.Push(child);
            }
            return result.ToArray();
        }
    }
}
