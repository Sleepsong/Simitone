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
    }
}
