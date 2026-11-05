using System;
using Microsoft.Xna.Framework;
using Simitone.VR.OpenXR;

namespace Simitone.VR
{
    /// <summary>
    /// Conversions between OpenXR and XNA math. Both are right handed with +Y up and -Z forward,
    /// so the conversion is a straight copy; only the matrix conventions differ (XNA uses row vectors).
    /// </summary>
    public static class VRMath
    {
        public static Vector3 ToXna(XrVector3f v) => new Vector3(v.X, v.Y, v.Z);
        public static Quaternion ToXna(XrQuaternionf q) => new Quaternion(q.X, q.Y, q.Z, q.W);
        public static XrVector3f ToXr(Vector3 v) => new XrVector3f { X = v.X, Y = v.Y, Z = v.Z };
        public static XrQuaternionf ToXr(Quaternion q) => new XrQuaternionf { X = q.X, Y = q.Y, Z = q.Z, W = q.W };

        public static XrPosef Pose(Vector3 position, Quaternion orientation)
            => new XrPosef { Position = ToXr(position), Orientation = ToXr(orientation) };

        /// <summary>
        /// Off-centre perspective projection for an OpenXR field of view, in XNA/D3D clip space (z 0..1).
        /// MonoGame's GL backend remaps depth and Y for render targets itself.
        /// </summary>
        public static Matrix Projection(XrFovf fov, float near, float far)
        {
            var left = near * (float)Math.Tan(fov.AngleLeft);
            var right = near * (float)Math.Tan(fov.AngleRight);
            var up = near * (float)Math.Tan(fov.AngleUp);
            var down = near * (float)Math.Tan(fov.AngleDown);
            return Matrix.CreatePerspectiveOffCenter(left, right, down, up, near, far);
        }

        /// <summary>Yaw (rotation about +Y) of a direction, 0 = looking down -Z.</summary>
        public static float Yaw(Vector3 forward) => (float)Math.Atan2(-forward.X, -forward.Z);

        /// <summary>Yaw of an orientation's forward vector.</summary>
        public static float Yaw(Quaternion q) => Yaw(Vector3.Transform(-Vector3.UnitZ, q));

        public static float WrapAngle(float a) => MathHelper.WrapAngle(a);

        /// <summary>
        /// A perspective projection with a tiny field of view whose centre ray shows up at <paramref name="pixel"/>
        /// of a <paramref name="width"/> x <paramref name="height"/> viewport (a shift in normalised device
        /// coordinates). Used for the VR placement cursor: whatever pixel the game's mouse sits on, unprojecting it
        /// gives (very nearly) the camera's centre ray.
        /// </summary>
        public static Matrix ShiftedProjection(float fov, float width, float height, Vector2 pixel, float near, float far)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            var projection = Matrix.CreatePerspectiveFieldOfView(fov, width / height, near, far);
            var shift = Matrix.Identity;
            shift.M41 = 2 * pixel.X / width - 1;
            shift.M42 = 1 - 2 * pixel.Y / height;
            return projection * shift;
        }

        /// <summary>
        /// Launch speed for a projectile fired from <paramref name="origin"/> along <paramref name="direction"/> to
        /// pass through <paramref name="target"/> under <paramref name="gravity"/> (all in the same units).
        /// Null when no speed can: the target is above the straight line, or the direction is (nearly) vertical.
        /// </summary>
        public static float? ArcSpeedToHit(Vector3 origin, Vector3 direction, Vector3 target, float gravity)
        {
            direction.Normalize();
            var horizontal = (float)Math.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
            if (horizontal < 0.05f) return null;
            var distance = new Vector2(target.X - origin.X, target.Z - origin.Z).Length();
            var height = target.Y - origin.Y;
            var tan = direction.Y / horizontal;
            var denominator = 2 * horizontal * horizontal * (distance * tan - height);
            if (denominator <= 1e-4f) return null;
            return (float)Math.Sqrt(gravity * distance * distance / denominator);
        }
    }
}
