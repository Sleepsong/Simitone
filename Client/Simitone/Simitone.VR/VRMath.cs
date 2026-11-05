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

        /// <summary>
        /// A view: where it is, which way it faces (-Z forward), and its field of view as tangents of the angles to its
        /// left, right, bottom and top edges (left and bottom negative).
        /// </summary>
        public struct ViewFrustum
        {
            public Vector3 Position;
            public Quaternion Orientation;
            public float Left, Right, Down, Up;

            public Matrix View => Matrix.Invert(Matrix.CreateFromQuaternion(Orientation) * Matrix.CreateTranslation(Position));
            public Matrix Projection(float near, float far)
                => Matrix.CreatePerspectiveOffCenter(Left * near, Right * near, Down * near, Up * near, near, far);
        }

        public static ViewFrustum Frustum(Vector3 position, Quaternion orientation, XrFovf fov) => new ViewFrustum
        {
            Position = position,
            Orientation = orientation,
            Left = (float)Math.Tan(fov.AngleLeft),
            Right = (float)Math.Tan(fov.AngleRight),
            Down = (float)Math.Tan(fov.AngleDown),
            Up = (float)Math.Tan(fov.AngleUp)
        };

        /// <summary>
        /// One view that sees everything either of two views sees (for drawing both eyes in one pass, culled once):
        /// facing between them, with a field of view covering both, and set back far enough that both eyes' viewpoints
        /// are inside it. Each view's frustum is a cone from its viewpoint, so containing the viewpoint and the edge
        /// directions contains the whole of it.
        /// </summary>
        public static ViewFrustum Enclose(in ViewFrustum a, in ViewFrustum b)
        {
            var orientation = Quaternion.Normalize(Quaternion.Slerp(a.Orientation, b.Orientation, 0.5f));
            var centre = (a.Position + b.Position) / 2;
            var toLocal = Quaternion.Inverse(orientation);
            var result = new ViewFrustum { Orientation = orientation, Left = float.MaxValue, Down = float.MaxValue, Right = float.MinValue, Up = float.MinValue };
            void Corners(in ViewFrustum f, ref ViewFrustum r)
            {
                foreach (var x in new[] { f.Left, f.Right })
                    foreach (var y in new[] { f.Down, f.Up })
                    {
                        var dir = Vector3.Transform(Vector3.Transform(new Vector3(x, y, -1), f.Orientation), toLocal);
                        var depth = Math.Max(-dir.Z, 1e-3f); // (eyes face within 90 degrees of each other)
                        r.Left = Math.Min(r.Left, dir.X / depth);
                        r.Right = Math.Max(r.Right, dir.X / depth);
                        r.Down = Math.Min(r.Down, dir.Y / depth);
                        r.Up = Math.Max(r.Up, dir.Y / depth);
                    }
            }
            Corners(a, ref result);
            Corners(b, ref result);
            // how far back the viewpoint must go for both eyes' viewpoints to be inside: for an eye offset o (local),
            // the depth to it from a viewpoint d behind the centre is d - o.z, and it must see o.x and o.y within the
            // field of view
            float back = 0;
            foreach (var eye in new[] { a.Position, b.Position })
            {
                var o = Vector3.Transform(eye - centre, toLocal);
                float need = 0;
                if (o.X > 0) need = Math.Max(need, o.X / result.Right); else if (o.X < 0) need = Math.Max(need, o.X / result.Left);
                if (o.Y > 0) need = Math.Max(need, o.Y / result.Up); else if (o.Y < 0) need = Math.Max(need, o.Y / result.Down);
                back = Math.Max(back, need + o.Z);
            }
            back *= 1.01f;
            result.Position = centre + Vector3.Transform(new Vector3(0, 0, back), orientation);
            return result;
        }

        /// <summary>
        /// The clip-space transform from one view-projection to another (row vectors, as XNA): for any point,
        /// clip_to = clip_from * delta. Lets a shader that drew a point for one view draw it for another.
        /// </summary>
        public static Matrix ClipDelta(Matrix viewProjectionFrom, Matrix viewProjectionTo)
            => Matrix.Invert(viewProjectionFrom) * viewProjectionTo;

        /// <summary>
        /// A smaller view inside <paramref name="eye"/> (same viewpoint): a square of half-size
        /// <paramref name="halfTangent"/> centred on <paramref name="centre"/> (tangents), slid back inside the eye's
        /// field of view if it would cross an edge. Used for the sharp centre of foveated rendering.
        /// </summary>
        public static ViewFrustum Inset(in ViewFrustum eye, Vector2 centre, float halfTangent)
        {
            float Place(float c, float lo, float hi)
            {
                if (hi - lo <= 2 * halfTangent) return (lo + hi) / 2;
                return MathHelper.Clamp(c, lo + halfTangent, hi - halfTangent);
            }
            var x = Place(centre.X, eye.Left, eye.Right);
            var y = Place(centre.Y, eye.Down, eye.Up);
            return new ViewFrustum
            {
                Position = eye.Position,
                Orientation = eye.Orientation,
                Left = Math.Max(eye.Left, x - halfTangent),
                Right = Math.Min(eye.Right, x + halfTangent),
                Down = Math.Max(eye.Down, y - halfTangent),
                Up = Math.Min(eye.Up, y + halfTangent)
            };
        }

        /// <summary>
        /// Where an inset (see <see cref="Inset"/>) lands in its eye's image, in pixels of a width x height image stored
        /// top row first (as MonoGame's render targets are).
        /// </summary>
        public static Rectangle InsetRect(in ViewFrustum eye, in ViewFrustum inset, int width, int height)
        {
            var x0 = (inset.Left - eye.Left) / (eye.Right - eye.Left) * width;
            var x1 = (inset.Right - eye.Left) / (eye.Right - eye.Left) * width;
            var y0 = (eye.Up - inset.Up) / (eye.Up - eye.Down) * height;
            var y1 = (eye.Up - inset.Down) / (eye.Up - eye.Down) * height;
            return new Rectangle((int)Math.Round(x0), (int)Math.Round(y0), (int)Math.Round(x1 - x0), (int)Math.Round(y1 - y0));
        }

        /// <summary>
        /// The sharp square of foveated rendering, snapped to whole pixels of its eye's width x height image (top row
        /// first): a <paramref name="size"/> pixel rectangle centred as near <paramref name="centre"/> (tangents) as fits,
        /// and the view that draws exactly those pixels, so its image lines up with the eye's pixel for pixel.
        /// </summary>
        public static (Rectangle rect, ViewFrustum view) InsetPixels(in ViewFrustum eye, Vector2 centre, Point size, int width, int height)
        {
            var w = Math.Min(size.X, width);
            var h = Math.Min(size.Y, height);
            var cx = (centre.X - eye.Left) / (eye.Right - eye.Left) * width;
            var cy = (eye.Up - centre.Y) / (eye.Up - eye.Down) * height;
            var x = Math.Max(0, Math.Min(width - w, (int)Math.Round(cx - w / 2f)));
            var y = Math.Max(0, Math.Min(height - h, (int)Math.Round(cy - h / 2f)));
            float left = eye.Left, right = eye.Right, up = eye.Up, down = eye.Down;
            float tx(int px) => left + px / (float)width * (right - left);
            float ty(int py) => up - py / (float)height * (up - down);
            var view = new ViewFrustum
            {
                Position = eye.Position,
                Orientation = eye.Orientation,
                Left = tx(x), Right = tx(x + w), Up = ty(y), Down = ty(y + h)
            };
            return (new Rectangle(x, y, w, h), view);
        }

        /// <summary>A direction's tangents in a view (x / -z, y / -z); null if it points behind.</summary>
        public static Vector2? Tangents(in ViewFrustum view, Vector3 worldDirection)
        {
            var d = Vector3.Transform(worldDirection, Quaternion.Inverse(view.Orientation));
            if (d.Z > -1e-3f) return null;
            return new Vector2(d.X / -d.Z, d.Y / -d.Z);
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
