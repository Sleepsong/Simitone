using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.VR;

namespace Simitone.Client.VR
{
    /// <summary>
    /// The game's tooltip, floating over what you're pointing at (the game draws it beside the mouse, which you can't
    /// see in VR): why something can't be placed there, what a wall costs, who a Sim is. On Simitone's dark stripe, in
    /// orange for a placement error as the money panel shows a loss. Faces you, and keeps the same apparent size
    /// however far away it is.
    /// </summary>
    public class VRTipPanel : VRUIPanel
    {
        private const int PanelW = 640, PanelH = 116, TextLeft = 26, TextWidth = PanelW - 32 - TextLeft * 2;
        private readonly UILabel Text;
        private string ShownText = "";
        private bool ShownError;

        public VRTipPanel() : base(PanelW, PanelH, 0.5f)
        {
            Add(new UIDiagonalStripe(new Point(PanelW - 32, PanelH), UIDiagonalStripeSide.RIGHT, UIStyle.Current.Bg));
            Text = VRUI.Paragraph("", 22, UIStyle.Current.Text, TextWidth, PanelH - 20);
            Text.Alignment = TextAlignment.Left | TextAlignment.Middle;
            Text.Position = new Vector2(TextLeft, 10);
            Add(Text);
        }

        /// <summary>Shows <paramref name="text"/> at <paramref name="anchor"/> (tracking space), facing the head.</summary>
        public void ShowAt(string text, bool error, Vector3 anchor, VRPose head)
        {
            if (!head.Valid || string.IsNullOrEmpty(text))
            {
                Hide();
                return;
            }
            if (text != ShownText || error != ShownError)
            {
                ShownText = text;
                ShownError = error;
                Text.Caption = text;
                Text.CaptionStyle.Color = error ? UIStyle.Current.NegMoney : UIStyle.Current.Text;
            }
            var toAnchor = anchor - head.Position;
            var distance = toAnchor.Length();
            var direction = distance > 0.05f ? toAnchor / distance : Vector3.Transform(-Vector3.UnitZ, head.Orientation);
            var d = MathHelper.Clamp(distance, 0.6f, 5f);
            PanelPosition = head.Position + direction * d;
            var forward = Vector3.Transform(-Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.Up, VRMath.Yaw(head.Orientation)));
            PanelOrientation = Quaternion.CreateFromRotationMatrix(Microsoft.Xna.Framework.Matrix.CreateWorld(Vector3.Zero, direction,
                System.Math.Abs(direction.Y) > 0.95f ? forward : Vector3.Up));
            WidthMetres = d * 0.3f;
            if (!Shown) Show();
        }
    }
}
