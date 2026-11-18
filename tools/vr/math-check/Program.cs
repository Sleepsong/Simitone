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

        // ---- single-pass stereo: one view that encloses both eyes, and each eye's clip-space delta from it
        {
            var rnd = new Random(7);
            foreach (var (ipd, cant) in new[] { (0.064f, 0f), (0.072f, 5f), (0.058f, -3f) })
            {
                var head = new Vector3(12, 5, -7);
                var headRot = Quaternion.CreateFromYawPitchRoll(0.7f, -0.2f, 0.05f);
                var w = scale; // world units per metre
                VRMath.ViewFrustum Eye(int side) => new VRMath.ViewFrustum
                {
                    Position = head + Vector3.Transform(new Vector3(side * ipd / 2 * w, 0, 0), headRot),
                    Orientation = headRot * Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.ToRadians(-side * cant)),
                    Left = (float)Math.Tan(MathHelper.ToRadians(side < 0 ? -54 : -48)), Right = (float)Math.Tan(MathHelper.ToRadians(side < 0 ? 48 : 54)),
                    Down = (float)Math.Tan(MathHelper.ToRadians(-55)), Up = (float)Math.Tan(MathHelper.ToRadians(50))
                };
                var left = Eye(-1); var right = Eye(1);
                var both = VRMath.Enclose(left, right);
                const float near = 0.05f * 3, far = 1000;
                var vpBoth = both.View * both.Projection(near * 0.5f, far);
                var frustumBoth = new BoundingFrustum(vpBoth);
                int seen = 0, missed = 0;
                float worstClip = 0;
                foreach (var eye in new[] { left, right })
                {
                    var vpEye = eye.View * eye.Projection(near, far);
                    var frustumEye = new BoundingFrustum(vpEye);
                    var delta = VRMath.ClipDelta(vpBoth, vpEye);
                    for (int i = 0; i < 4000; i++)
                    {
                        // points anywhere near the eye, from a few cm to 30 m away
                        var dir = Vector3.Normalize(new Vector3((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1));
                        var p = eye.Position + dir * (float)(0.06 + Math.Pow(rnd.NextDouble(), 2) * 30) * w;
                        if (frustumEye.Contains(p) == ContainmentType.Disjoint) continue;
                        seen++;
                        if (frustumBoth.Contains(p) == ContainmentType.Disjoint) missed++;
                        var viaDelta = Vector4.Transform(Vector4.Transform(new Vector4(p, 1), vpBoth), delta);
                        var direct = Vector4.Transform(new Vector4(p, 1), vpEye);
                        worstClip = Math.Max(worstClip, (new Vector3(viaDelta.X, viaDelta.Y, viaDelta.Z) / viaDelta.W - new Vector3(direct.X, direct.Y, direct.Z) / direct.W).Length());
                    }
                }
                Check($"stereo (IPD {ipd * 1000:0} mm, cant {cant} deg): the shared view sees everything either eye does", seen > 1000 && missed == 0,
                    $"{seen} points seen, {missed} missed");
                Check($"stereo (IPD {ipd * 1000:0} mm, cant {cant} deg): the clip delta gives each eye's own projection", worstClip < 2e-3f,
                    $"worst NDC difference {worstClip:0.000000}");
            }
        }

        // ---- foveation: the sharp inset follows the gaze, stays inside the eye's view, and maps to the right pixels
        {
            var eye = new VRMath.ViewFrustum { Position = Vector3.Zero, Orientation = Quaternion.Identity, Left = -1.2f, Right = 1.0f, Down = -1.3f, Up = 1.1f };
            var centred = VRMath.Inset(eye, new Vector2(0.1f, -0.05f), 0.35f);
            Check("foveation: an inset sits on the gaze", Math.Abs((centred.Left + centred.Right) / 2 - 0.1f) < 1e-5f
                && Math.Abs((centred.Down + centred.Up) / 2 + 0.05f) < 1e-5f && Math.Abs(centred.Right - centred.Left - 0.7f) < 1e-5f, "0.7 x 0.7 tangents");
            var edge = VRMath.Inset(eye, new Vector2(0.98f, 1.5f), 0.35f);
            Check("foveation: near the edge it slides back inside (same size)", Math.Abs(edge.Right - 1.0f) < 1e-5f && Math.Abs(edge.Up - 1.1f) < 1e-5f
                && Math.Abs(edge.Right - edge.Left - 0.7f) < 1e-5f && Math.Abs(edge.Up - edge.Down - 0.7f) < 1e-5f, $"x {edge.Left:0.00}..{edge.Right:0.00}, y {edge.Down:0.00}..{edge.Up:0.00}");
            var rect = VRMath.InsetRect(eye, centred, 2200, 2400);
            // a point in the inset's view lands at the same pixel through the inset as through the whole eye
            var t = new Vector2(0.3f, 0.2f);
            var fullX = (t.X - eye.Left) / (eye.Right - eye.Left) * 2200; var fullY = (eye.Up - t.Y) / (eye.Up - eye.Down) * 2400;
            var insetX = rect.X + (t.X - centred.Left) / (centred.Right - centred.Left) * rect.Width;
            var insetY = rect.Y + (centred.Up - t.Y) / (centred.Up - centred.Down) * rect.Height;
            Check("foveation: the inset's pixels line up with the whole eye's", Math.Abs(fullX - insetX) < 1f && Math.Abs(fullY - insetY) < 1f,
                $"rect {rect}, off by {fullX - insetX:0.00}, {fullY - insetY:0.00} px");
            var gaze = VRMath.Tangents(eye, Vector3.Normalize(new Vector3(0.2f, -0.1f, -1)));
            Check("foveation: a gaze direction as tangents", gaze != null && Math.Abs(gaze.Value.X - 0.2f) < 1e-5f && Math.Abs(gaze.Value.Y + 0.1f) < 1e-5f, gaze?.ToString() ?? "null");
        }

        // ---- the dollhouse: holding the world with one or two hands (VRCameraController.UpdateDollhouseDrag)
        {
            var rig = new Vector3(40, -100, 25);
            float yaw = 0.4f, unitsPerMetre = 150f;
            // one hand holds a point and moves (up, too): the point stays in the hand
            var hand0 = new Vector3(0.1f, 1.0f, -0.4f);
            var held = VRMath.RoomToWorld(rig, yaw, unitsPerMetre, hand0);
            var hand1 = new Vector3(-0.2f, 1.2f, -0.6f);
            var rig1 = VRMath.RigPlacing(hand1, held, yaw, unitsPerMetre);
            var drift = Vector3.Distance(VRMath.RoomToWorld(rig1, yaw, unitsPerMetre, hand1), held);
            Check("dollhouse: one hand: what it holds stays in it", drift < 1e-3f, $"{drift:0.00000} units off");

            // two hands: turn the pair 30 degrees about the vertical and move them twice as far apart
            var left0 = new Vector3(-0.2f, 1.0f, -0.5f);
            var right0 = new Vector3(0.2f, 1.05f, -0.45f);
            var heldLeft = VRMath.RoomToWorld(rig, yaw, unitsPerMetre, left0);
            var heldRight = VRMath.RoomToWorld(rig, yaw, unitsPerMetre, right0);
            var anchor = VRMath.RoomToWorld(rig, yaw, unitsPerMetre, (left0 + right0) / 2);
            var mid = new Vector3(0.05f, 1.1f, -0.55f);
            var half = VRMath.RotateYaw((right0 - left0) / 2, MathHelper.ToRadians(30)) * 2;
            var left1 = mid - half;
            var right1 = mid + half;
            var (scale2, yaw2) = VRMath.TwoHandHold(right0 - left0, unitsPerMetre, yaw, right1 - left1, 10, 400);
            var rig2 = VRMath.RigPlacing(mid, anchor, yaw2, scale2);
            var leftOff = Vector3.Distance(VRMath.RoomToWorld(rig2, yaw2, scale2, left1), heldLeft);
            var rightOff = Vector3.Distance(VRMath.RoomToWorld(rig2, yaw2, scale2, right1), heldRight);
            Check("dollhouse: two hands: turning them turns the world, and what each holds stays in it", leftOff < 1e-2f && rightOff < 1e-2f,
                $"left {leftOff:0.0000}, right {rightOff:0.0000} units off");
            Check("dollhouse: two hands twice as far apart: the world twice the size", Math.Abs(scale2 - unitsPerMetre / 2) < 1e-3f
                && Math.Abs(MathHelper.WrapAngle(yaw2 - yaw) - MathHelper.ToRadians(-30)) < 1e-4f,
                $"{unitsPerMetre} -> {scale2:0.00} units per metre, turned {MathHelper.ToDegrees(MathHelper.WrapAngle(yaw2 - yaw)):0.0} degrees");
            // (hands brought to 6 cm apart would make it nearly 7 times smaller; under 5 cm apart is ignored, as jitter)
            var close = Vector3.Normalize(right0 - left0) * 0.06f;
            var (clamped, _) = VRMath.TwoHandHold(right0 - left0, unitsPerMetre, yaw, close, 10, 400);
            Check("dollhouse: it can't be made smaller than the limit", clamped == 400, $"{clamped} units per metre (limit 400)");
            // the giant's scale (VRCameraController.GiantScale): a tile 10 cm across, inside the resize limits
            var giant = 3f / 0.10f;
            Check("dollhouse: you start a giant ten times a Sim's size (a tile 10 cm)", Math.Abs(3f / giant - 0.10f) < 1e-6f
                && giant >= 3f / 0.25f && giant <= 3f / 0.0075f, $"{giant} units per metre, a tile {3f / giant * 100:0.0} cm");
        }

        // ---- the dollhouse lid: a plane in the world, as the clip plane of a view (VRMath.ClipPlane), for a view moved
        // with the lid: points of the house above the lifted floor's underside are kept wherever the view puts them.
        // The view is the dollhouse's: a 64-tile lot 1.3 m across (148 units a metre), seen from 0.65 m above and
        // 0.75 m away, with the stereo pass's near plane (half of 5 cm). Clip space can't place the plane much closer
        // than a few thousandths of a unit at that near plane; the lid is clipped 0.02 units under its floor.
        {
            const float unitsPerMetre = 3f * 64 / 1.3f, near = 0.05f * unitsPerMetre * 0.5f;
            var eye = new Vector3(96, 0.65f * unitsPerMetre, 96 + 0.75f * unitsPerMetre);
            var lidView = Matrix.CreateLookAt(eye, new Vector3(96, 0, 96), Vector3.Up);
            var lidProjection = Matrix.CreatePerspectiveOffCenter(-1.0f * near, 0.9f * near, -0.95f * near, 0.85f * near, near, 1000);
            var lid = Matrix.CreateTranslation(new Vector3(12, 7.5f, -4)); // (the lid moved by the hand)
            var viewProjection = lid * lidView * lidProjection;
            const float baseY = 8.85f; // the second floor's underside
            var plane = new Vector4(0, 1, 0, -baseY);
            var clip = VRMath.ClipPlane(plane, viewProjection);
            float worst = 0;
            bool sidesRight = true;
            var random = new Random(3);
            for (int i = 0; i < 400; i++)
            {
                var p = new Vector3((float)random.NextDouble() * 192, (float)random.NextDouble() * 30, (float)random.NextDouble() * 192);
                var c = Vector4.Transform(new Vector4(p, 1), viewProjection);
                var distance = Vector4.Dot(c, clip);
                worst = Math.Max(worst, Math.Abs(distance - (p.Y - baseY)));
                if ((distance >= 0) != (p.Y >= baseY) && Math.Abs(p.Y - baseY) > 0.005f) sidesRight = false;
            }
            Check("dollhouse lid: the clip plane keeps what's above the lifted floor, as the moved view draws it", sidesRight && worst < 0.005f,
                $"largest difference from the height above the floor {worst:0.00000} units");
        }

        // ---- the observer's stair ride: the way through the stairs' own tiles (VRStairPath), round any turns
        {
            System.Collections.Generic.List<Point> T(params (int x, int y)[] tiles)
            {
                var list = new System.Collections.Generic.List<Point>();
                foreach (var (x, y) in tiles) list.Add(new Point(x, y));
                return list;
            }
            string Show(System.Collections.Generic.IReadOnlyList<Vector2> path) => string.Join(" ", System.Linq.Enumerable.Select(path, p => $"({p.X:0.#},{p.Y:0.#})"));
            bool InsideTiles(System.Collections.Generic.IReadOnlyList<Vector2> path, System.Collections.Generic.List<Point> tiles)
            {
                foreach (var p in path)
                {
                    var inside = false;
                    foreach (var t in tiles) if (p.X >= t.X - 1e-3f && p.X <= t.X + 1 + 1e-3f && p.Y >= t.Y - 1e-3f && p.Y <= t.Y + 1 + 1e-3f) inside = true;
                    if (!inside) return false;
                }
                return true;
            }

            var straight = VRStairPath.Tiles(T((3, 2), (3, 3), (3, 4), (3, 5)), new Point(3, 2), new Point(3, 5));
            var straightRound = VRStairPath.Round(straight);
            Check("stair ride: straight stairs go straight, end to end", straight.Count == 4 && straightRound.Count == 4
                && Math.Abs(VRStairPath.Length(straightRound) - 3) < 1e-4f, Show(straightRound));

            var lTiles = T((0, 0), (0, 1), (0, 2), (1, 2), (2, 2));
            var l = VRStairPath.Tiles(lTiles, new Point(0, 0), new Point(2, 2));
            var lRound = VRStairPath.Round(l);
            var lLength = VRStairPath.Length(lRound);
            Check("stair ride: stairs that turn (an L) are followed through their corner, rounded, inside their tiles",
                l.Count == 5 && l[2] == new Vector2(0.5f, 2.5f) && lRound.Count > l.Count && InsideTiles(lRound, lTiles)
                && lLength < 4 && lLength > Vector2.Distance(l[0], l[4]), $"{l.Count} tiles, rounded {lRound.Count} points, {lLength:0.00} tiles (corners {4:0} unrounded)");

            var spiralTiles = T((5, 5), (6, 5), (6, 6), (5, 6));
            var spiral = VRStairPath.Tiles(spiralTiles, new Point(5, 5), new Point(6, 5));
            Check("stair ride: a 2x2 block with its ends side by side goes the long way round all four (a spiral)",
                spiral.Count == 4 && spiral[1] == new Vector2(5.5f, 6.5f) && spiral[2] == new Vector2(6.5f, 6.5f) && spiral[3] == new Vector2(6.5f, 5.5f), Show(spiral));
            var spiralBack = VRStairPath.Tiles(spiralTiles, new Point(6, 5), new Point(5, 5));
            Check("stair ride: and the same way back down, reversed", spiralBack.Count == 4 && spiralBack[1] == new Vector2(6.5f, 6.5f)
                && spiralBack[2] == new Vector2(5.5f, 6.5f), Show(spiralBack));
            var diagonal = VRStairPath.Tiles(spiralTiles, new Point(5, 5), new Point(6, 6));
            Check("stair ride: opposite corners of a block go the short way (two steps)", diagonal.Count == 3, Show(diagonal));

            var gap = VRStairPath.Tiles(T((0, 0), (0, 2)), new Point(0, 0), new Point(0, 2));
            Check("stair ride: no way through the tiles: straight from end to end", gap.Count == 2, Show(gap));
            var same = VRStairPath.Tiles(T((4, 4)), new Point(4, 4), new Point(4, 4));
            Check("stair ride: stairs on one tile (straight up): one point", same.Count == 1, Show(same));

            var start = VRStairPath.At(lRound, 0);
            var end = VRStairPath.At(lRound, lLength + 1);
            var monotonic = true;
            var last = 0f;
            for (int i = 1; i <= 20; i++)
            {
                var p = VRStairPath.At(lRound, lLength * i / 20f);
                var progress = Vector2.Distance(start, p);
                // (along an L, the distance from the start only grows)
                if (progress + 1e-3f < last) monotonic = false;
                last = progress;
            }
            Check("stair ride: points along the way start and end at its ends and move steadily away", start == lRound[0]
                && end == lRound[lRound.Count - 1] && monotonic, $"start {start}, end {end}");
        }

        // ---- a pixel on a panel to your room (VRMath.PlanePoint), the inverse of PointOnPlane: where a tablet tile is
        {
            var position = new Vector3(0.3f, 1.1f, -0.4f);
            var orientation = Quaternion.CreateFromYawPitchRoll(0.4f, -0.9f, 0.2f);
            float worst = 0;
            foreach (var uv in new[] { new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.25f, 0.8f), new Vector2(0.5f, 0.5f) })
            {
                var point = VRMath.PlanePoint(uv, position, orientation, 0.26f, 0.28f);
                var back = VRMath.PointOnPlane(point, position, orientation, 0.26f, 0.28f, out var off);
                worst = Math.Max(worst, Vector2.Distance(back, uv) + Math.Abs(off));
            }
            Check("panel: a pixel's point in the room lands back on that pixel, on the panel", worst < 1e-4f, $"worst {worst:0.000000}");
        }

        // ---- the pull of something just picked up in buy/build (VRMath.PullStep)
        {
            // from the tablet, 5 tiles off and 1 up: it rises on the way and lands on the arc's end
            var pullTarget = new Vector3(20, 30, 0.2f);
            var shown = pullTarget + new Vector3(-4, 3, 1);
            float time = 0, peak = 0, arrived = -1;
            var moved = false;
            for (int i = 0; i < 300 && arrived < 0; i++)
            {
                var rise = VRMath.PullStep(ref shown, pullTarget, ref time, ref moved, 1 / 90f, out var done);
                peak = Math.Max(peak, rise);
                if (done) arrived = time;
            }
            Check("pull: from the tablet it rises on the way and lands on the arc's end in under a second", arrived > 0.3f && arrived < 1f
                && peak > 1f && peak <= 1.5f && Vector3.Distance(shown, pullTarget) < 0.05f, $"arrived after {arrived:0.00} s, rose up to {peak:0.00} tiles");

            // grabbed off the lot: it hops, then waits there until you aim it somewhere else, and is pulled there
            pullTarget = new Vector3(10, 10, 0);
            shown = pullTarget;
            time = 0; peak = 0; moved = false;
            var doneEarly = false;
            for (int i = 0; i < 45; i++)
            {
                peak = Math.Max(peak, VRMath.PullStep(ref shown, pullTarget, ref time, ref moved, 1 / 90f, out var done));
                doneEarly |= done;
            }
            var aimed = pullTarget + new Vector3(3, 0, 0);
            arrived = -1;
            var start = time;
            for (int i = 0; i < 300 && arrived < 0; i++)
            {
                VRMath.PullStep(ref shown, aimed, ref time, ref moved, 1 / 90f, out var done);
                if (done) arrived = time - start;
            }
            Check("pull: grabbed off the lot it hops about 0.3 tiles, stays until aimed elsewhere, then is pulled there",
                peak > 0.25f && peak < 0.35f && !doneEarly && arrived > 0.2f && arrived < 1f, $"hop {peak:0.00} tiles, pulled over in {arrived:0.00} s");
        }

        // ---- stereo sounds mixed down to mono (VRSoundMix), so they can be placed in 3D
        {
            byte[] Wav(short channels, short bits, byte[] data)
            {
                var stream = new System.IO.MemoryStream();
                var w = new System.IO.BinaryWriter(stream);
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write(channels); w.Write(22050);
                w.Write(22050 * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write(bits);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(data.Length); w.Write(data);
                return stream.ToArray();
            }
            // 16 bits: (1000, -200) and (-30000, -30000)
            var s16 = new byte[8];
            BitConverter.GetBytes((short)1000).CopyTo(s16, 0); BitConverter.GetBytes((short)-200).CopyTo(s16, 2);
            BitConverter.GetBytes((short)-30000).CopyTo(s16, 4); BitConverter.GetBytes((short)-30000).CopyTo(s16, 6);
            var m16 = VRSoundMix.MonoFromStereoWav(Wav(2, 16, s16), out var rate16);
            Check("sound: 16-bit stereo becomes mono, each sample the average of its sides", m16 != null && m16.Length == 4 && rate16 == 22050
                && BitConverter.ToInt16(m16, 0) == 400 && BitConverter.ToInt16(m16, 2) == -30000,
                m16 == null ? "nothing" : $"{BitConverter.ToInt16(m16, 0)}, {BitConverter.ToInt16(m16, 2)} at {rate16} Hz");
            // 8 bits (unsigned, 128 silent): (200, 56) cancel out; (255, 255) is loud
            var m8 = VRSoundMix.MonoFromStereoWav(Wav(2, 8, new byte[] { 200, 56, 255, 255 }), out _);
            Check("sound: 8-bit stereo becomes 16-bit mono", m8 != null && m8.Length == 4 && BitConverter.ToInt16(m8, 0) == 0
                && BitConverter.ToInt16(m8, 2) == 127 * 256, m8 == null ? "nothing" : $"{BitConverter.ToInt16(m8, 0)}, {BitConverter.ToInt16(m8, 2)}");
            Check("sound: a mono file is left alone (it can be placed as it is), and a broken one too",
                VRSoundMix.MonoFromStereoWav(Wav(1, 16, s16), out _) == null && VRSoundMix.MonoFromStereoWav(new byte[] { 1, 2, 3 }, out _) == null
                && VRSoundMix.MonoFromStereoWav(null, out _) == null, "");
        }

        Console.WriteLine(Failures == 0 ? "all VR cursor maths checks passed" : $"{Failures} failure(s)");
        return Failures == 0 ? 0 : 1;
    }
}
