using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WinHealthAudit.Ui
{
    /// <summary>One check in the Details tab: its notes and its findings.</summary>
    internal sealed class CheckPanel : Panel
    {
        public CheckPanel(CheckResult result, int width)
        {
            BackColor = Theme.Surface;
            Width = width;

            var inner = width - 32;
            var messageWidth = width - 94 - 16;
            var y = 14;

            var header = Theme.MakeLabel(string.Empty, Theme.Heading, Theme.Text);
            Theme.PlaceText(header, result.Name, Theme.Heading, Theme.Text, 16, y, inner - 170);
            Controls.Add(header);

            var counter = Theme.MakeLabel(Summary(result), Theme.Small, WorstColour(result));
            counter.TextAlign = ContentAlignment.MiddleRight;
            counter.SetBounds(width - 16 - 170, y + 2, 170, 16);
            Controls.Add(counter);

            y = Math.Max(header.Bottom, counter.Bottom) + 10;

            foreach (var note in result.Notes)
            {
                var label = Theme.MakeLabel(string.Empty, Theme.Small, Theme.Muted);
                y = Theme.PlaceText(label, note, Theme.Small, Theme.Muted, 16, y, inner) + 5;
                Controls.Add(label);
            }

            foreach (var finding in result.Findings)
            {
                var colour = Theme.SeverityColour(finding.Severity);

                var word = Theme.MakeLabel(Theme.SeverityWord(finding.Severity), Theme.SmallBold, colour);
                word.SetBounds(16, y + 1, 74, 16);
                Controls.Add(word);

                var message = Theme.MakeLabel(string.Empty, Theme.Body, Theme.Text);
                y = Theme.PlaceText(message, finding.Message, Theme.Body, Theme.Text, 94, y, messageWidth);
                Controls.Add(message);

                if (finding.Evidence.Length > 0)
                {
                    var evidence = Theme.MakeLabel(string.Empty, Theme.Small, Theme.Muted);
                    y = Theme.PlaceText(evidence, finding.Evidence, Theme.Small, Theme.Muted, 94, y, messageWidth) + 6;
                    Controls.Add(evidence);
                }
                else
                {
                    y = message.Bottom + 6;
                }
            }

            if (result.Notes.Count == 0 && result.Findings.Count == 0)
            {
                var empty = Theme.MakeLabel(string.Empty, Theme.Small, Theme.Muted);
                y = Theme.PlaceText(empty, "Nothing to report.", Theme.Small, Theme.Muted, 16, y, inner);
                Controls.Add(empty);
            }

            Height = y + 8;
        }

        private static string Summary(CheckResult result)
        {
            var parts = new List<string>();

            if (result.Findings.Count > 0)
            {
                parts.Add(result.Findings.Count + (result.Findings.Count == 1 ? " finding" : " findings"));
            }

            if (result.Notes.Count > 0)
            {
                parts.Add(result.Notes.Count + (result.Notes.Count == 1 ? " note" : " notes"));
            }

            return parts.Count > 0 ? string.Join(" · ", parts.ToArray()) : "clear";
        }

        private static Color WorstColour(CheckResult result)
        {
            foreach (var finding in result.Findings)
            {
                if (finding.Severity == Severity.Critical) return Theme.Critical;
            }

            return result.Findings.Count > 0 ? Theme.Warning : Theme.Good;
        }
    }
}
