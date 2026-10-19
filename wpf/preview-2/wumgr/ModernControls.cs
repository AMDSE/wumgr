using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace wumgr
{
    internal static class ModernControlPaint
    {
        // Native borders are outside OnPaint on some WinForms implementations.
        internal static CreateParams Borderless(CreateParams value)
        {
            value.Style &= ~0x00800000; // WS_BORDER
            value.ExStyle &= ~0x00000200; // WS_EX_CLIENTEDGE
            return value;
        }
        internal static Color Surface(Control control)
        {
            return control.Parent == null ? DriverTheme.Canvas : control.Parent.BackColor;
        }
        internal static void FocusRing(Graphics g, Rectangle bounds, int radius, Color color)
        {
            if (bounds.Width < 2 || bounds.Height < 2) return;
            using (var path = DriverTheme.Round(bounds, radius))
            using (var pen = new Pen(color, 1.5F)) g.DrawPath(pen, path);
        }
        internal static void Choice(Graphics g, Control c, bool selected, bool mixed, bool hover, bool radio)
        {
            float scale = g.DpiX / 96F;
            int size = Math.Min((int)Math.Round(18 * scale), Math.Min(c.Width - 6, c.Height - 6));
            if (size < 4) return;
            int inset = Math.Max(3, (int)Math.Round(3 * scale));
            bool hasText = !String.IsNullOrEmpty(c.Text);
            int x = hasText ? inset : (c.Width - size) / 2;
            if (hasText && c.RightToLeft == RightToLeft.Yes) x = c.Width - inset - size;
            var box = new Rectangle(x, (c.Height - size) / 2, size, size);
            Color accent = SystemInformation.HighContrast ? SystemColors.Highlight : DriverTheme.Accent;
            Color border = !c.Enabled ? Color.FromArgb(194, 204, 199) : hover ? accent : Color.FromArgb(149, 167, 159);
            Color fill = !c.Enabled ? Color.FromArgb(233, 238, 235) : selected || mixed ? accent : hover ? DriverTheme.Pale : Color.White;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = radio ? new GraphicsPath() : DriverTheme.Round(box, Math.Max(2, (int)(4 * scale))))
            {
                if (radio) path.AddEllipse(box);
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                using (var pen = new Pen(selected && c.Enabled ? accent : border, Math.Max(1F, 1.3F * scale))) g.DrawPath(pen, path);
            }
            Color mark = !c.Enabled ? Color.FromArgb(151, 165, 157) : SystemInformation.HighContrast ? SystemColors.HighlightText : Color.White;
            if (radio && selected)
            {
                var dot = box; dot.Inflate(-size / 3, -size / 3);
                using (var b = new SolidBrush(mark)) g.FillEllipse(b, dot);
            }
            else if (mixed)
            {
                using (var pen = new Pen(mark, 2F * scale)) g.DrawLine(pen, x + size * .27F, box.Top + size * .5F, x + size * .73F, box.Top + size * .5F);
            }
            else if (selected)
            {
                using (var pen = new Pen(mark, 2F * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(x + size * .24F, box.Top + size * .51F), new PointF(x + size * .43F, box.Top + size * .7F), new PointF(x + size * .78F, box.Top + size * .3F) });
            }
            if (c.Focused)
            {
                var focus = box; focus.Inflate(inset - 1, inset - 1);
                FocusRing(g, focus, Math.Max(3, (int)(6 * scale)), accent);
            }
            if (hasText)
            {
                var text = c.RightToLeft == RightToLeft.Yes ? new Rectangle(0, 0, x - inset, c.Height) : new Rectangle(box.Right + (int)(9 * scale), 0, Math.Max(0, c.Width - box.Right - (int)(9 * scale)), c.Height);
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
                if (c.RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.Right | TextFormatFlags.RightToLeft;
                DriverTheme.Text(g, c.Text, c.Font, c.Enabled ? c.ForeColor : SystemColors.GrayText, text, flags);
            }
        }
    }

    internal sealed class SoftButton : Button
    {
        public bool Primary, Active;
        bool hover, pressed;
        readonly Font ownedFont = DriverTheme.Font(9);
        public SoftButton(string text, bool primary = false)
        {
            Text = text; Primary = primary; Font = ownedFont; Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 38;
        }
        protected override CreateParams CreateParams { get { return ModernControlPaint.Borderless(base.CreateParams); } }
        protected override void Dispose(bool disposing) { if (disposing) ownedFont.Dispose(); base.Dispose(disposing); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(ModernControlPaint.Surface(this)); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Color bg = !Enabled ? Color.FromArgb(236, 240, 238) : Primary ? (pressed ? Color.FromArgb(9, 112, 86) : hover ? Color.FromArgb(12, 131, 100) : DriverTheme.Accent) : Active || hover || pressed ? DriverTheme.Pale : Color.White;
            if (Width < 16 || Height < 16) return;
            using (var path = DriverTheme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 7))
            {
                using (var brush = new SolidBrush(bg)) g.FillPath(brush, path);
                if (!Primary && !Active) using (var pen = new Pen(DriverTheme.Line)) g.DrawPath(pen, path);
            }
            DriverTheme.Text(g, Text, Font, !Enabled ? Color.FromArgb(153, 165, 160) : Primary ? Color.White : Active ? DriverTheme.Accent : DriverTheme.Ink, ClientRectangle, TextFormatFlags.HorizontalCenter);
            if (Focused && ShowFocusCues) ModernControlPaint.FocusRing(g, new Rectangle(4, 4, Width - 9, Height - 9), 5, Primary ? Color.White : DriverTheme.Accent);
        }
    }

    internal class SoftPanel : Panel
    {
        public SoftPanel()
        {
            BackColor = Color.White; BorderStyle = BorderStyle.None;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override CreateParams CreateParams { get { return ModernControlPaint.Borderless(base.CreateParams); } }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(ModernControlPaint.Surface(this));
            if (Width < 26 || Height < 26) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = DriverTheme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 12))
            using (var brush = new SolidBrush(BackColor)) g.FillPath(brush, path);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 26 || Height < 26) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = DriverTheme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 12))
            using (var pen = new Pen(DriverTheme.Line)) e.Graphics.DrawPath(pen, path);
        }
    }

    // Keep native CheckBox/RadioButton behavior (Space, Tab, UIA, AutoCheck and events).
    // Only replace their painting; never simulate selection or input handling.
    internal sealed class SoftCheckBox : CheckBox
    {
        bool hover;
        public SoftCheckBox()
        {
            AutoSize = false; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.CheckButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override CreateParams CreateParams { get { return ModernControlPaint.Borderless(base.CreateParams); } }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(BackColor); }
        protected override void OnPaint(PaintEventArgs e) { ModernControlPaint.Choice(e.Graphics, this, Checked, CheckState == CheckState.Indeterminate, hover, false); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    }

    internal sealed class SoftRadioButton : RadioButton
    {
        bool hover;
        public SoftRadioButton()
        {
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override CreateParams CreateParams { get { return ModernControlPaint.Borderless(base.CreateParams); } }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(BackColor); }
        protected override void OnPaint(PaintEventArgs e) { ModernControlPaint.Choice(e.Graphics, this, Checked, false, hover, true); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    }
}
