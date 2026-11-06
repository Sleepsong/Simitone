using FSO.LotView;
using FSO.SimAntics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// The picture of a lot saved for the neighbourhood map sometimes comes out with its floors and roof solid black
    /// (pure 0,0,0 where those are drawn), in 3D, now and then and not always: the cause isn't known yet. A picture like
    /// that is noted (with what the renderer was set to, to find the cause), rendered again, and if it's still broken,
    /// the house keeps the picture it had.
    /// </summary>
    public static class LotPictureGuard
    {
        /// <summary>Notes for a log (the frontend sets it, e.g. with -logs).</summary>
        public static Action<string> Note;

        /// <summary>
        /// Whether a lot picture looks broken: a share of its visible pixels exactly black that a real roof or floor
        /// doesn't have (good pictures have none; broken ones had 29-73%).
        /// </summary>
        public static bool LooksBroken(Texture2D picture, out string stats)
        {
            stats = "";
            if (picture == null) return false;
            var data = new Color[picture.Width * picture.Height];
            picture.GetData(data);
            int visible = 0, black = 0;
            foreach (var c in data)
            {
                if (c.A <= 200) continue;
                visible++;
                if (c.R == 0 && c.G == 0 && c.B == 0) black++;
            }
            stats = black + " of " + visible + " visible pixels pure black";
            return black > 100 && black * 10 > visible;
        }

        /// <summary>What the renderer was set to when the picture was taken, for finding why it came out black.</summary>
        public static string Describe(World world, VM vm)
        {
            var parts = new List<string>();
            void Add(string name, Func<object> value)
            {
                try { parts.Add(name + " " + (value() ?? "null")); }
                catch (Exception e) { parts.Add(name + " ?(" + e.GetType().Name + ")"); }
            }
            Add("game time", () => vm.Context.Clock.Hours.ToString("00") + ":" + vm.Context.Clock.Minutes.ToString("00"));
            Add("camera", () => world.State.CameraMode);
            Add("zoom", () => world.State.Zoom + " (" + world.State.PreciseZoom + ")");
            Add("level", () => world.State.Level);
            Add("lighting mode", () => WorldConfig.Current.LightingMode + (WorldConfig.Current.Complex ? ", complex" : ""));
            Add("outside color", () => world.State.OutsideColor);
            Add("lighting adjust", () => world.State.LightingAdjust);
            var grass = WorldContent.GrassEffect;
            Add("floor/roof LightingAdjust", () => grass.Parameters["LightingAdjust"]?.GetValueVector3());
            Add("OutsideDark", () => grass.Parameters["OutsideDark"]?.GetValueVector4());
            Add("MinAvg", () => grass.Parameters["MinAvg"]?.GetValueVector2());
            Add("LightVec", () => grass.Parameters["LightVec"]?.GetValueVector3());
            Add("DiffuseColor", () => grass.Parameters["DiffuseColor"]?.GetValueVector4());
            Add("Bias", () => grass.Parameters["Bias"]?.GetValueSingle());
            var roof = vm.Context.Blueprint?.RoofComp;
            Add("roof style", () => roof?.RoofStyle);
            Add("roof texture", () => TextureState(roof?.Texture));
            Add("roof parallax", () => TextureState(roof?.ParallaxTexture));
            Add("roof normals", () => TextureState(roof?.NormalMap));
            return string.Join("; ", parts);
        }

        private static string TextureState(Texture2D tex)
        {
            if (tex == null) return "none";
            return tex.Width + "x" + tex.Height + " " + tex.Format + ", " + tex.LevelCount + " levels" + (tex.IsDisposed ? ", DISPOSED" : "");
        }
    }
}
