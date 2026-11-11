using FSO.Content;
using FSO.Content.Model;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.LotView.RC;
using FSO.SimAntics.Model;
using FSO.SimAntics.Utils;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Simitone.Client.Packs;
using Simitone.Client.UI.Panels.LiveSubpanels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace HalfWalls
{
    /// <summary>
    /// The Sims 2's half wall, added to the engine entirely from a pack: nothing in Simitone or FreeSO knows about it.
    /// A half wall is a new wall style. It is wallpapered and drawn like a wall, only low, and otherwise acts like a
    /// fence: it blocks Sims but doesn't divide a room.
    ///
    /// The same feature exists as an engine change (FreeSO branch simitone/half-walls, about 80 lines in 6 files).
    /// Each part of that change is redone here as a Harmony patch, in the order below. Where the engine edit was
    /// in the middle of a long method, the patch has to get the same effect from outside, and says how.
    /// </summary>
    public class HalfWallsPack : ISimitonePack
    {
        /// <summary>
        /// The style of a half wall: well above the 255 global styles and the ones objects add from 256.
        /// Walls keep their style as 16 bits in saves, so a saved half wall needs this pack to be drawn as one.
        /// </summary>
        public const ushort Style = 0x7F00;

        /// <summary>A half wall's height, as a share of a wall's: a little above a counter.</summary>
        public const float Height = 0.3f;

        public static bool IsThickStyle(ushort style) => style == 1 || style == 255 || style == Style;
        public static bool TopLeftHalf(in WallTile wall) => (wall.Segments & WallSegments.TopLeft) > 0 && wall.TopLeftStyle == Style;
        public static bool TopRightHalf(in WallTile wall) => (wall.Segments & (WallSegments.TopRight | WallSegments.AnyDiag)) > 0 && wall.TopRightStyle == Style;

        [ThreadStatic] private static int InRoomMap; //above 0 while rooms are being worked out on this thread
        [ThreadStatic] private static bool Low;      //true while the 3D geometry of a half wall is being built

        public void OnLoad(Pack pack)
        {
            var harmony = new Harmony("pack." + pack.Id);
            try
            {
                Apply(harmony);
            }
            catch
            {
                harmony.UnpatchAll(harmony.Id); //all or nothing: half of these patches would be worse than none
                throw;
            }

            //build mode's list of wall styles: the half wall goes second, after the wall, wallpapered like one
            if (!UIBuyBrowsePanel.WallStyleIDs.Contains((short)Style))
            {
                UIBuyBrowsePanel.WallStyleIDs = InsertAt(UIBuyBrowsePanel.WallStyleIDs, 1, (short)Style);
                UIBuyBrowsePanel.WallStylePatterns = InsertAt(UIBuyBrowsePanel.WallStylePatterns, 1, (short)0);
            }
        }

        private static short[] InsertAt(short[] array, int index, short value)
        {
            var list = array.ToList();
            list.Insert(index, value);
            return list.ToArray();
        }

        private static HarmonyMethod Mine(string name) => new HarmonyMethod(typeof(HalfWallsPack), name);

        private static MethodInfo Find(Type type, string name)
        {
            return AccessTools.Method(type, name) ?? throw new Exception(type.Name + "." + name + " was not found: this pack was made for a different version of Simitone");
        }

        private static void Apply(Harmony harmony)
        {
            // 1. The style itself, and its catalog icon.
            harmony.Patch(Find(typeof(WorldWallProvider), "InitGlobals"), postfix: Mine(nameof(AddStyle)));
            harmony.Patch(Find(typeof(WorldWallProvider), "GetWallStyleIcon"), prefix: Mine(nameof(UseWallIcon)));

            // 2. "Thick" means drawn and wallpapered as a wall. A half wall is thick...
            harmony.Patch(AccessTools.PropertyGetter(typeof(WallTile), "TopLeftThick"), postfix: Mine(nameof(TopLeftThickToo)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(WallTile), "TopRightThick"), postfix: Mine(nameof(TopRightThickToo)));

            // 3. ...except when rooms are worked out, where thick means "divides a room". The engine edit added
            //    "and not a half wall" to four lines inside GenerateMap. From outside: thick answers no for the whole call.
            harmony.Patch(Find(typeof(VMRoomMap), "GenerateMap"), prefix: Mine(nameof(EnterRoomMap)), finalizer: Mine(nameof(LeaveRoomMap)));

            // 4. Diagonal walls don't use "thick": eight places compare the style with 1 directly. The engine edit changed
            //    each comparison to IsThickStyle. From outside that takes rewriting the compiled code of those methods.
            //    GenerateWallData has a third such comparison, earlier on, that the engine edit left alone: skip it.
            foreach (var method in new[] {
                Find(typeof(VMArchitectureTools), "WallPatternDot"), Find(typeof(VMArchitectureTools), "SpreadOnto"),
                Find(typeof(WallComponent), "GenerateWallData"), Find(typeof(WallComponentRC), "Generate") })
            {
                harmony.Patch(method, transpiler: Mine(nameof(StyleIsOneToIsThick)));
            }

            // 5. In 2D a half wall is always drawn low, with no cutaway slopes. The engine edit set that in four places
            //    in the middle of Draw. From outside: fix the cutaway data Draw reads, right after it is worked out.
            harmony.Patch(Find(typeof(WallComponent), "GenerateWallData"), postfix: Mine(nameof(NoSlopesOnHalfWalls)));

            // 6. In 3D a half wall is a wall of a lower height. The engine edit added a height cap variable to Generate,
            //    read by its two local functions. From outside: patch those two local functions, which the compiler
            //    turned into hidden methods that can only be found by their shape.
            var locals = typeof(WallComponentRC).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .SelectMany(x => x.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)).ToList();
            var lineGeom = locals.Where(x => x.ReturnType == typeof(void) && x.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] {
                typeof(int), typeof(int), typeof(Vector2), typeof(Vector2), typeof(ushort), typeof(ushort), typeof(int), typeof(float), typeof(float), typeof(float) })).ToList();
            var wallHeight = locals.Where(x => x.ReturnType == typeof(float) && x.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(Vector3) })).ToList();
            if (lineGeom.Count != 1 || wallHeight.Count != 1)
                throw new Exception("The 3D wall code is not as expected (" + lineGeom.Count + ", " + wallHeight.Count + "): this pack was made for a different version of Simitone");
            harmony.Patch(lineGeom[0], prefix: Mine(nameof(BeforeLine)), finalizer: Mine(nameof(AfterLine)));
            harmony.Patch(wallHeight[0], postfix: Mine(nameof(CapHeight)));
            harmony.Patch(Find(typeof(WallComponentRC), "Generate"), prefix: Mine(nameof(EnterGenerate)));
        }

        // ---- 1 ----

        public static void AddStyle(WorldWallProvider __instance)
        {
            var wall = __instance.GetWallStyle(1);
            if (wall == null || __instance.GetWallStyle(Style) != null) return;
            //its 2D look is the wall's walls-down one, always
            Find(typeof(WorldWallProvider), "AddWallStyle").Invoke(__instance, new object[] { new WallStyle
            {
                ID = Style,
                WallsUpFar = wall.WallsDownFar, WallsUpMedium = wall.WallsDownMedium, WallsUpNear = wall.WallsDownNear,
                WallsDownFar = wall.WallsDownFar, WallsDownMedium = wall.WallsDownMedium, WallsDownNear = wall.WallsDownNear,
                Price = wall.Price,
                Name = "Half Wall",
                Description = "A low wall, as high as a counter. Sims can't walk through it, but it doesn't divide a room."
            } });
        }

        public static void UseWallIcon(ref ushort id)
        {
            if (id == Style) id = 1;
        }

        // ---- 2 and 3 ----

        public static void TopLeftThickToo(ref WallTile __instance, ref bool __result)
        {
            if (!__result && InRoomMap == 0) __result = TopLeftHalf(__instance);
        }

        public static void TopRightThickToo(ref WallTile __instance, ref bool __result)
        {
            //the original only looks at straight walls, so this must too
            if (!__result && InRoomMap == 0) __result = (__instance.Segments & WallSegments.TopRight) > 0 && __instance.TopRightStyle == Style;
        }

        public static void EnterRoomMap() { InRoomMap++; }
        public static Exception LeaveRoomMap(Exception __exception) { InRoomMap--; return __exception; }

        // ---- 4 ----

        /// <summary>How many comparisons were rewritten in each method, for checking against what this pack expects.</summary>
        public static Dictionary<string, int> Rewritten = new Dictionary<string, int>();

        /// <summary>
        /// Turns each "TopRightStyle == 1" in a method into "IsThickStyle(TopRightStyle)". In compiled code that is the
        /// field load, the constant 1, then a compare or a branch.
        /// </summary>
        public static IEnumerable<CodeInstruction> StyleIsOneToIsThick(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            //Harmony runs this again whenever the method gets another patch, so it must not depend on anything but the method
            var skip = original.Name == "GenerateWallData" ? 1 : 0; //how many of the comparisons to leave alone, from the start
            var code = instructions.ToList();
            var isThick = AccessTools.Method(typeof(HalfWallsPack), nameof(IsThickStyle));
            int count = 0;
            for (int i = 0; i + 2 < code.Count; i++)
            {
                if (!(code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo field && field.Name == "TopRightStyle" && field.DeclaringType == typeof(WallTile))) continue;
                if (code[i + 1].opcode != OpCodes.Ldc_I4_1) continue;
                var compare = code[i + 2].opcode;
                OpCode? replacement = null;
                if (compare == OpCodes.Ceq) replacement = OpCodes.Nop;                                   //leaves true or false
                else if (compare == OpCodes.Beq || compare == OpCodes.Beq_S) replacement = OpCodes.Brtrue;      //jumps if equal
                else if (compare == OpCodes.Bne_Un || compare == OpCodes.Bne_Un_S) replacement = OpCodes.Brfalse; //jumps if not
                if (replacement == null) continue;
                if (count++ < skip) continue;
                code[i + 1].opcode = OpCodes.Call; code[i + 1].operand = isThick;
                code[i + 2].opcode = replacement.Value;
            }
            //the count is this pack's only way to notice that the method is no longer the one it was written against
            if (count != skip + 2) throw new Exception(original.Name + " compares a wall's style with 1 in " + count + " places, not " + (skip + 2) + ": this pack was made for a different version of Simitone");
            Rewritten[original.DeclaringType.Name + "." + original.Name] = count - skip;
            return code;
        }

        // ---- 5 ----

        public static void NoSlopesOnHalfWalls(WallTile[] walls, WallCuts[] ___Cuts)
        {
            if (___Cuts == null) return;
            var length = Math.Min(walls.Length, ___Cuts.Length);
            for (int i = 0; i < length; i++)
            {
                if (walls[i].Segments == 0) continue;
                if (TopLeftHalf(walls[i]) && (___Cuts[i].TLCut == WallCut.DownLeftUpRight || ___Cuts[i].TLCut == WallCut.DownRightUpLeft)) ___Cuts[i].TLCut = WallCut.Down;
                if (TopRightHalf(walls[i]) && (___Cuts[i].TRCut == WallCut.DownLeftUpRight || ___Cuts[i].TRCut == WallCut.DownRightUpLeft)) ___Cuts[i].TRCut = WallCut.Down;
            }
        }

        // ---- 6 ----

        [ThreadStatic] private static WallComponentRC Generating;

        public static void EnterGenerate(WallComponentRC __instance) { Generating = __instance; }

        /// <summary>
        /// Before a strip of wall is built. Faces are passed their wall's style, which says if it is a half wall.
        /// The end caps are not: the engine edit could see which wall it was capping, but from here that has to be
        /// worked out from where the cap is. Either way the strip is then built as an ordinary wall, capped in height.
        /// </summary>
        public static void BeforeLine(object __instance, int __0, int __1, Vector2 __2, Vector2 __3, ref ushort __5, int __6)
        {
            Low = false;
            if (__5 == Style)
            {
                Low = true;
                __5 = 1; //masked as a wall
            }
            else if (__6 == 5 && __5 == 1 && (__3 - __2).LengthSquared() < 0.25f && Generating != null)
            {
                //a cap: a short strip across the end of a straight wall. one across X caps the wall along Y (top left), and the reverse
                var level = CapturedLevel(__instance);
                var blueprint = Generating.blueprint;
                if (level < 1 || __0 < 0 || __1 < 0 || __0 >= blueprint.Width || __1 >= blueprint.Height) return;
                var wall = blueprint.GetWall((short)__0, (short)__1, level);
                Low = (__2.Y == __3.Y) ? TopLeftHalf(wall) : ((wall.Segments & WallSegments.TopRight) > 0 && wall.TopRightStyle == Style);
            }
        }

        public static Exception AfterLine(Exception __exception) { Low = false; return __exception; }

        public static void CapHeight(ref float __result)
        {
            if (Low && __result > Height) __result = Height;
        }

        private static FieldInfo LevelField; private static FieldInfo LevelOuterField; private static Type LevelClosure;

        /// <summary>The floor level Generate is working on: one of its local variables, kept in the hidden object its local functions share.</summary>
        private static sbyte CapturedLevel(object closure)
        {
            if (LevelClosure != closure.GetType())
            {
                LevelClosure = closure.GetType(); LevelOuterField = null;
                var fields = LevelClosure.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                LevelField = fields.FirstOrDefault(x => x.Name == "level" && x.FieldType == typeof(sbyte));
                if (LevelField == null)
                {
                    //or one hidden object further out
                    foreach (var outer in fields)
                    {
                        var inner = outer.FieldType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(x => x.Name == "level" && x.FieldType == typeof(sbyte));
                        if (inner != null) { LevelOuterField = outer; LevelField = inner; break; }
                    }
                }
            }
            if (LevelField == null) return -1;
            var holder = LevelOuterField == null ? closure : LevelOuterField.GetValue(closure);
            return holder == null ? (sbyte)-1 : (sbyte)LevelField.GetValue(holder);
        }
    }
}
