using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinHealthAudit.Ui
{
    /// <summary>One big number with a caption: how many critical / warning / info findings there are.</summary>
    internal sealed class SummaryTile : Panel
    {
        private readonly Label _number;
        private readonly Label _caption;

        public SummaryTile(string caption, Color numberColour)
        {
            BackColor = Theme.Surface;

            _number = Theme.MakeLabel("0", Theme.TileNumber, numberColour);
            _caption = Theme.MakeLabel(caption.ToUpperInvariant(), Theme.TileCaption, Theme.Muted);

            Controls.Add(_number);
            Controls.Add(_caption);

            Size = new Size(160, 70);
        }

        /// <summary>The number currently on the tile.</summary>
        public string Value
        {
            get { return _number.Text; }
        }

        public void Show(string value)
        {
            _number.Text = value;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _number.SetBounds(14, 8, Width - 28, 34);
            _caption.SetBounds(14, 46, Width - 28, 16);
        }
    }
}
