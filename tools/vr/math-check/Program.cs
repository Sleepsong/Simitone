using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.VR;

// The VR buy/build cursor relies on two pieces of maths:
//  1. a camera whose (shifted, tiny-FOV) projection makes the game's mouse pixel unproject onto the aim point, the
//     way FreeSO's WorldState.CameraRayAtScreenPos / WorldPlatform3D picking do it (Viewport.Unproject);
//  2. the arc speed that makes a throw along the controller's direction land on a grabbed object.
static class Program
{
    static int Failures;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: {detail}");
        if (!ok) Failures++;
    }

    // As FreeSO: ray from unprojecting the pixel at depth 0 and 1.
    static Ray RayAt(Viewport vp, Vector2 pixel, Matrix view, Matrix projection)
    {
        var p1 = vp.Unproject(new Vector3(pixel, 0), projection, view, Matrix.Identity);
        var p2 = vp.Unproject(new Vector3(pixel, 1), projection, view, Matrix.Identity);
        return new Ray(p1, Vector3.Normalize(p2 - p1));
    }

    static float DistanceToRay(Ray ray, Vector3 point)
    {
        var t = Vector3.Dot(point - ray.Position, ray.Direction);
        return Vector3.Distance(ray.Position + ray.Direction * t, point);
    }

    static int Main()
    {
        var vp = new Viewport(0, 0, 1280, 720);
        var cursor = new Vector2(640, 360);

        // ---- placing: camera straight above the arc's landing point
        var target = new Vector3(37.3f, 2.1f, 52.9f);
        var view = Matrix.CreateLookAt(target + Vector3.Up * 20f, target, Vector3.Forward);
        foreach (var px in new[] { cursor, new Vector2(300, 200), new Vector2(1279, 719) })
        {
            var proj = VRMath.ShiftedProjection(0.004f, 1280, 720, px, 0.05f, 4000f);
            var ray = RayAt(vp, px, view, proj);
            var miss = DistanceToRay(ray, target);
            Check($"place: cursor pixel {px} passes through the landing point (within 3 mm)", miss < 0.01f, $"miss {miss:0.00000} units");
        }
        {
            // any other mouse position (pointer on the catalogue) stays close to the landing point
            var proj = VRMath.ShiftedProjection(0.004f, 1280, 720, cursor, 0.05f, 4000f);
            var ray = RayAt(vp, new Vector2(640, 715), view, proj);
            var t = (target.Y - ray.Position.Y) / ray.Direction.Y; // where it meets the floor plane
            var drift = Vector3.Distance(ray.Position + ray.Direction * t, target);
            Check("place: mouse at the bottom of the screen lands within 0.1 units (3 cm)", drift < 0.1f, $"drift {drift:0.0000} units");
        }

        // ---- picking: camera down the controller's ray
        {
            var origin = new Vector3(20, 4.5f, 20);
            var dir = Vector3.Normalize(new Vector3(0.6f, -0.35f, -0.7f));
            var pickView = Matrix.CreateLookAt(origin, origin + dir, Vector3.Up);
            var proj = VRMath.ShiftedProjection(0.004f, 1280, 720, cursor, 0.05f, 4000f);
            var ray = RayAt(vp, cursor, pickView, proj);
            var angle = MathHelper.ToDegrees((float)Math.Acos(MathHelper.Clamp(Vector3.Dot(ray.Direction, dir), -1, 1)));
            var offset = DistanceToRay(ray, origin);
            Check("pick: the cursor ray is the controller ray", angle < 0.01f && offset < 0.01f, $"{angle:0.0000} deg, {offset:0.0000} units off");
        }

        // ---- arc speed: simulate the throw like VRCameraController does and see where it lands
        var scale = 3f;
        var gravity = 9.8f * scale;
        foreach (var (origin, dir, grab) in new[]
        {
            (new Vector3(30, 4.2f, 30), new Vector3(0.3f, -0.2f, -1f), new Vector3(33, 0, 21)),
            (new Vector3(30, 4.2f, 30), new Vector3(1f, 0.15f, 0.2f), new Vector3(45, 0, 33)),
            (new Vector3(10, 4.8f, 10), new Vector3(0, -0.8f, 1f), new Vector3(10, 0, 13.5f)),
        })
        {
            var d = Vector3.Normalize(dir);
            // aim the controller at the grabbed object's horizontal direction, keeping its pitch
            var flat = new Vector2(grab.X - origin.X, grab.Z - origin.Z);
            flat.Normalize();
            var horiz = (float)Math.Sqrt(d.X * d.X + d.Z * d.Z);
            d = Vector3.Normalize(new Vector3(flat.X * horiz, d.Y, flat.Y * horiz));
            var speed = VRMath.ArcSpeedToHit(origin, d, grab, gravity);
            if (speed == null) { Check($"arc to {grab}", false, "no solution"); continue; }
            var pos = origin;
            var vel = d * speed.Value;
            for (int i = 0; i < 20000 && pos.Y > grab.Y; i++)
            {
                var next = pos + vel * 0.0005f;
                vel.Y -= gravity * 0.0005f;
                if (next.Y <= grab.Y) { next = pos + (next - pos) * ((pos.Y - grab.Y) / (pos.Y - next.Y)); pos = next; break; }
                pos = next;
            }
            var miss = new Vector2(pos.X - grab.X, pos.Z - grab.Z).Length();
            Check($"arc lands on a grabbed object at {grab}", miss < 0.05f, $"speed {speed.Value / scale:0.00} m/s, lands {miss:0.000} units away");
        }
        {
            var none = VRMath.ArcSpeedToHit(new Vector3(0, 1, 0), new Vector3(0, -0.5f, 1), new Vector3(0, 5, 10), gravity);
            Check("arc: a target above the straight line has no solution", none == null, none?.ToString() ?? "null");
        }

        // ---- movement arc (left trigger): 8 m/s from about waist height, stepped like VRCameraController.TraceArc
        {
            float Reach(float pitchDegrees, out bool landed)
            {
                var pitch = MathHelper.ToRadians(pitchDegrees);
                var pos = new Vector3(0, 1.0f * scale, 0);
                var vel = new Vector3(0, (float)Math.Sin(pitch), -(float)Math.Cos(pitch)) * 8f * scale;
                landed = false;
                for (int i = 0; i < 300 && !landed; i++) // TraceArc: 300 steps of 15 ms
                {
                    var next = pos + vel * 0.015f;
                    vel.Y -= gravity * 0.015f;
                    if (next.Y <= 0) { next = pos + (next - pos) * (pos.Y / (pos.Y - next.Y)); landed = true; }
                    pos = next;
                }
                return new Vector2(pos.X, pos.Z).Length() / scale;
            }
            var at45 = Reach(45, out var landed45);
            Check("movement arc: about 7 m of reach at 45 degrees", landed45 && at45 > 6.5f && at45 < 8f, $"{at45:0.00} m");
            var allLand = true;
            var detail = "";
            for (int pitch = -80; pitch <= 85; pitch += 5)
            {
                var r = Reach(pitch, out var landed);
                allLand &= landed;
                if (pitch % 30 == 0) detail += $"{pitch}deg {r:0.0} m  ";
            }
            Check("movement arc: lands within TraceArc's 300 steps at any pitch", allLand, detail.Trim());
        }

        Console.WriteLine(Failures == 0 ? "all VR cursor maths checks passed" : $"{Failures} failure(s)");
        return Failures == 0 ? 0 : 1;
    }
}
