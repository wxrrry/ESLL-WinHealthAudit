using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinHealthAudit.Ui
{
    /// <summary>One finding, drawn as a card with a coloured bar on the left.</summary>
    internal sealed class FindingCard : Panel
    {
        private readonly Finding _finding;

        public FindingCard(Finding finding, int width, Action<string> copy)
        {
            _finding = finding;
            BackColor = Theme.Surface;
            Width = width;

            var colour = Theme.SeverityColour(finding.Severity);
            var inner = width - 32;
            var bottom = 12;

            var source = Theme.MakeLabel(string.Empty, Theme.SmallBold, colour);
            bottom = Theme.PlaceText(source, finding.Source, Theme.SmallBold, colour, 16, 12, inner - 70) + 5;

            var message = Theme.MakeLabel(string.Empty, Theme.Body, Theme.Text);
            bottom = Theme.PlaceText(message, finding.Message, Theme.Body, Theme.Text, 16, bottom, inner) + 4;

            Controls.Add(source);
            Controls.Add(message);

            if (finding.Evidence.Length > 0)
            {
                var evidence = Theme.MakeLabel(string.Empty, Theme.Small, Theme.Muted);
                bottom = Theme.PlaceText(evidence, finding.Evidence, Theme.Small, Theme.Muted, 16, bottom, inner);
                Controls.Add(evidence);
            }

            Height = bottom + 12;

            var bar = new Panel
            {
                BackColor = colour,
                Location = new Point(0, 0),
                Size = new Size(4, Height)
            };
            Controls.Add(bar);
            Controls.SetChildIndex(bar, 0);

            var copyButton = Theme.MakeButton("copy", delegate { OnCopy(copy); }, Theme.Surface);
            copyButton.Font = Theme.Small;
            copyButton.SetBounds(width - 72, 9, 56, 20);
            Controls.Add(copyButton);

            MouseEnter += delegate { BackColor = Theme.SurfaceHover; };
            MouseLeave += delegate { BackColor = Theme.Surface; };
        }

        private void OnCopy(Action<string> copy)
        {
            if (copy == null) return;

            copy(_finding.Evidence.Length > 0
                ? _finding.Message + Environment.NewLine + _finding.Evidence
                : _finding.Message);
        }
    }
}
