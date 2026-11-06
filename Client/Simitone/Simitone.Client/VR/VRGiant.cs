using System;
using System.Collections.Generic;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.Vitaboy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>
    /// The dollhouse's giant: you, as your selected Sim many times their size, standing on the lot's ground (your real
    /// floor) and leaning in over the house. It's their own avatar drawn a second time (their look, their clothes), from
    /// a skeleton of its own: the game's rest pose standing, the upper body bending at the base of the spine towards
    /// your head, the arms reaching for your hands (<see cref="VRSimArms"/>), the head left out (you look out of it).
    /// The feet stay put while you lean and step after you when you walk further; crouch and the body sinks.
    /// Only for drawing: the real Sim keeps living in the house.
    /// </summary>
    public sealed class VRGiant
    {
        private readonly VRSimArms Arms = new VRSimArms();
        private AvatarComponent Source;
        private Skeleton Skeleton;      // a copy of the Sim's rest skeleton, posed for the giant each frame
        private Matrix[] Rest, Bones;   // rest pose, and this frame's (skinning matrices, model space)
        private int Head = -1, SpineBase = -1;
        private float RestFloor;        // the rest pose's lowest bone (the soles), model units
        private int[] UpperBody;
        private Vector3 Feet;           // world units, on the ground
        private float BodyYaw;
        private bool Placed;
        private float StandingEye = 1.6f; // metres: the tallest your head has been lately
        /// <summary>The giant's world matrix this frame (model space to the lot's world).</summary>
        public Matrix World { get; private set; }
        /// <summary>Drawn this frame.</summary>
        public bool Shown { get; private set; }

        /// <summary>How far you can lean (metres, as you see it) before the feet step after you.</summary>
        private const float LeanReach = 0.3f;

        /// <summary>Works out this frame's pose (before the eyes are drawn): as <paramref name="sim"/>, around your head and hands.</summary>
        public void Update(AvatarComponent sim, VRCameraController camera, VRLotWorld world, float dt)
        {
            Shown = false;
            if (sim?.Avatar?.BaseSkeleton == null || camera == null) { Source = null; Placed = false; return; }
            if (sim != Source || Skeleton == null)
            {
                Source = sim;
                if (!Prepare(sim.Avatar.BaseSkeleton)) { Source = null; return; }
                Placed = false;
            }
            var units = camera.UnitsPerMetre;
            var head = SimitoneVR.HeadPoseTracking();
            if (!head.Valid) return;

            // your standing height, so the giant's eyes are yours (it comes back down slowly after a jump)
            var floorRelative = SimitoneVR.Runtime?.FloorRelative ?? false;
            var eyeHeight = floorRelative ? head.Position.Y : SimitoneVR.Settings.FallbackEyeHeight;
            StandingEye = Math.Max(1.2f, Math.Max(eyeHeight, StandingEye - 0.05f * dt));

            var restHead = Rest[Head].Translation;
            if (restHead.Y - RestFloor < 1e-3f) return;
            // a little under and behind the eyes, where the head bone is
            var size = (StandingEye - 0.1f) * units / (restHead.Y - RestFloor);
            var eye = camera.HeadWorld;
            var targetYaw = camera.HeadWorldYaw;
            if (!Placed) BodyYaw = targetYaw;
            BodyYaw += MathHelper.WrapAngle(targetYaw - BodyYaw) * (1 - (float)Math.Exp(-dt * 3f));
            var bodyForward = new Vector3(-(float)Math.Sin(BodyYaw), 0, -(float)Math.Cos(BodyYaw));
            var headBone = eye - Vector3.Up * 0.1f * units - bodyForward * 0.08f * units;

            // the feet: under you, staying put while you lean; dragged along when you go further
            var under = new Vector2(headBone.X, headBone.Z);
            var feet = new Vector2(Feet.X, Feet.Z);
            if (!Placed) feet = under;
            var away = under - feet;
            var reach = LeanReach * units;
            if (away.Length() > reach) feet = under - Vector2.Normalize(away) * reach;
            var ground = VRCameraController.FloorHeight(world, feet / VRCameraController.TileUnits, 1);
            Feet = new Vector3(feet.X, ground, feet.Y);
            Placed = true;

            Shown = Pose(Feet, BodyYaw, size, headBone,
                camera.LeftHandValid ? camera.LeftWrist : (Vector3?)null, camera.LeftPoint,
                camera.RightHandValid ? camera.RightWrist : (Vector3?)null, camera.RightPoint, camera.LeftRoll, camera.RightRoll);
        }

        /// <summary>
        /// The pose itself (world units): standing with its soles at <paramref name="feet"/>, facing <paramref name="yaw"/>,
        /// <paramref name="size"/> times a Sim's size; the upper body bent at the base of the spine so the head bone goes
        /// to <paramref name="headBone"/> (the whole body sinking if that's lower than it reaches, and bending no further
        /// than 80 degrees); each arm whose wrist target is given reaching for it. Sets <see cref="World"/>. False without
        /// a skeleton. (Also used by tools/vr/ui-check, with <see cref="PrepareFor"/>.)
        /// </summary>
        public bool Pose(Vector3 feet, float yaw, float size, Vector3 headBone, Vector3? leftWrist, Vector3 leftPoint,
            Vector3? rightWrist, Vector3 rightPoint, float leftRoll = 0, float rightRoll = 0)
        {
            if (Skeleton == null) return false;
            Array.Copy(Rest, Bones, Rest.Length);
            for (int i = 0; i < Bones.Length; i++)
            {
                Skeleton.Bones[i].AbsoluteMatrix = Bones[i];
                Skeleton.Bones[i].AbsolutePosition = Bones[i].Translation;
            }
            var restHead = Rest[Head].Translation;
            var stand = feet - Vector3.Up * RestFloor * size; // (the soles on the ground)
            var turn = Matrix.CreateScale(size) * Matrix.CreateRotationY(MathHelper.Pi + yaw);
            var toModel = Matrix.Invert(turn * Matrix.CreateTranslation(stand));
            // Bend at the base of the spine just far enough to put the head over the target (80 degrees at most), and
            // sink the whole body for the rest if the target's lower than that: a lean bends, a crouch sinks.
            var pivot = Bones[SpineBase].Translation;
            var spineLength = (restHead - pivot).Length();
            var target = Vector3.Transform(headBone, toModel);
            var across = new Vector3(target.X - pivot.X, 0, target.Z - pivot.Z);
            var lean = (float)Math.Asin(MathHelper.Clamp(across.Length() / Math.Max(1e-4f, spineLength), 0, (float)Math.Sin(MathHelper.ToRadians(80))));
            var direction = Vector3.Up * (float)Math.Cos(lean)
                + (across.LengthSquared() > 1e-8f ? Vector3.Normalize(across) * (float)Math.Sin(lean) : Vector3.Zero);
            var sink = Math.Max(0, pivot.Y + spineLength * (float)Math.Cos(lean) - target.Y); // (model units)
            World = turn * Matrix.CreateTranslation(stand - Vector3.Up * sink * size);
            toModel = Matrix.Invert(World);
            VRSimArms.Rotate(Bones, Skeleton, UpperBody, pivot, VRSimArms.Between(restHead - pivot, direction));

            // the arms reach for your hands
            var handedness = toModel.Determinant() < 0 ? -1f : 1f;
            Arms.Solve(Skeleton, Bones, 1,
                leftWrist != null ? Vector3.Transform(leftWrist.Value, toModel) : (Vector3?)null, Vector3.TransformNormal(leftPoint, toModel),
                rightWrist != null ? Vector3.Transform(rightWrist.Value, toModel) : (Vector3?)null, Vector3.TransformNormal(rightPoint, toModel),
                leftRoll * handedness, rightRoll * handedness);
            return true;
        }

        /// <summary>Takes this rest skeleton for the giant (without a Sim to draw: for tools/vr/ui-check). False if it can't.</summary>
        public bool PrepareFor(Skeleton rest) => Prepare(rest);

        /// <summary>A bone's position in the world, as last posed.</summary>
        public Vector3 BonePosition(string name)
        {
            var bone = Skeleton?.GetBone(name);
            return bone == null ? Vector3.Zero : Vector3.Transform(Bones[Array.IndexOf(Skeleton.Bones, bone)].Translation, World);
        }

        /// <summary>The rest pose's height from the soles to the head bone (model units).</summary>
        public float RestHeadHeight => Skeleton == null ? 0 : Rest[Head].Translation.Y - RestFloor;

        /// <summary>A copy of the rest skeleton, its rest pose, and which bones are the head and the upper body.</summary>
        private bool Prepare(Skeleton rest)
        {
            Skeleton = rest.Clone();
            if (Skeleton.RootBone == null) return false;
            Skeleton.ComputeBonePositions(Skeleton.RootBone, Matrix.Identity);
            Rest = new Matrix[Skeleton.Bones.Length];
            Bones = new Matrix[Skeleton.Bones.Length];
            var index = new Dictionary<Bone, int>();
            RestFloor = float.MaxValue;
            for (int i = 0; i < Skeleton.Bones.Length; i++)
            {
                Rest[i] = Skeleton.Bones[i].AbsoluteMatrix;
                index[Skeleton.Bones[i]] = i;
                RestFloor = Math.Min(RestFloor, Rest[i].Translation.Y);
            }
            var head = Skeleton.GetBone("HEAD");
            var pelvis = Skeleton.GetBone("PELVIS");
            if (head == null || pelvis == null) return false;
            // the spine's first bone above the pelvis, on the way up to the head
            var spine = head;
            while (spine != null && spine.ParentName != pelvis.Name) spine = Skeleton.GetBone(spine.ParentName);
            if (spine == null) return false;
            Head = index[head];
            SpineBase = index[spine];
            UpperBody = VRSimArms.Below(spine, index);
            return true;
        }

        /// <summary>
        /// Draws the giant (inside the lot's draw: once per stereo pass), with the game's avatar effect as it draws Sims,
        /// for the view set on <paramref name="state"/>.
        /// </summary>
        public void Draw(GraphicsDevice device, WorldState state)
        {
            var model = Source?.Avatar;
            if (!Shown || model == null || VRSimArms.SkelBonesField == null) return;
            var saved = VRSimArms.SkelBonesField.GetValue(model);
            var hideHead = model.HideHead;
            try
            {
                VRSimArms.SkelBonesField.SetValue(model, Bones);
                model.HideHead = true;
                device.DepthStencilState = DepthStencilState.Default;
                device.BlendState = BlendState.AlphaBlend;
                device.RasterizerState = RasterizerState.CullCounterClockwise;
                var effect = WorldContent.AvatarEffect;
                var advanced = WorldConfig.Current.Directional && WorldConfig.Current.AdvancedLighting;
                effect.CurrentTechnique = effect.Techniques[advanced ? 5 : WorldConfig.Current.PassOffset * 2];
                effect.Parameters["View"].SetValue(state.View);
                effect.Parameters["Projection"].SetValue(state.Projection);
                Source.DrawAvatarMesh(device, state, World, Color.White);
            }
            finally
            {
                VRSimArms.SkelBonesField.SetValue(model, saved);
                model.HideHead = hideHead;
                device.RasterizerState = RasterizerState.CullNone;
            }
        }
    }
}
