using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WinHealthAudit.Ui
{
    public sealed class MainForm : Form
    {
        private const int HeaderHeight = 64;
        private const int ProgressHeight = 4;
        private const int SummaryHeight = 88;
        private const int TabStripHeight = 40;
        private const int FooterHeight = 46;
        private const int Gutter = 18;

        private Panel _header;
        private Panel _progressTrack;
        private Panel _summary;
        private Panel _tabStrip;
        private Panel _footer;

        private SummaryTile _criticalTile;
        private SummaryTile _warningTile;
        private SummaryTile _infoTile;
        private SummaryTile _timeTile;

        private NumericUpDown _days;
        private Label _daysLabel;
        private Label _elevation;
        private Button _runButton;
        private Button _openButton;
        private Button _saveButton;
        private Button _copyButton;
        private Label _status;
        private Button[] _pageButtons;
        private Panel[] _pages;

        private FlowLayoutPanel _findingList;
        private FlowLayoutPanel _checkList;
        private TextBox _log;
        private Panel _filterBar;
        private CheckBox _showCritical;
        private CheckBox _showWarning;
        private CheckBox _showInfo;

        private IList<CheckResult> _results;
        private string _reportPath;
        private int _listWidth;
        private double _progressFraction;
        private bool _ready;
        private volatile bool _running;

        public MainForm(string[] args)
        {
            Text = "WinHealthAudit";
            ClientSize = new Size(1160, 760);
            MinimumSize = new Size(980, 660);
            BackColor = Theme.Screen;
            Font = Theme.Body;

            _header = new Panel { BackColor = Theme.Surface };
            _progressTrack = new Panel { BackColor = Theme.Line, Visible = false };
            _summary = new Panel { BackColor = Theme.Screen };
            _tabStrip = new Panel { BackColor = Theme.Screen };
            _footer = new Panel { BackColor = Theme.Surface };

            _progressTrack.Paint += PaintProgress;

            BuildHeader();
            BuildSummary();
            BuildPages();
            BuildFooter();

            Controls.Add(_header);
            Controls.Add(_progressTrack);
            Controls.Add(_summary);
            Controls.Add(_tabStrip);
            Controls.Add(_pages[0]);
            Controls.Add(_pages[1]);
            Controls.Add(_pages[2]);
            Controls.Add(_footer);

            SelectPage(0);
            _ready = true;
            LayoutWindow();

            Shown += delegate
            {
                MakeTitleBarDark();
                LayoutWindow();
                SetDays(ReadDays(args));
                StartAudit();
            };
        }

        /// <summary>Asks the desktop window manager to paint the title bar in dark colours.</summary>
        private void MakeTitleBarDark()
        {
            try
            {
                var dark = 1;
                if (SetWindowAttribute(Handle, 20, dark) != 0) SetWindowAttribute(Handle, 19, dark);
            }
            catch (EntryPointNotFoundException)
            {
                // Windows older than 10 20H1 - the title bar simply stays light.
            }
            catch (DllNotFoundException)
            {
            }
        }

        private static int SetWindowAttribute(IntPtr window, int attribute, int value)
        {
            return DwmSetWindowAttribute(window, attribute, ref value, 4);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        // ----------------------------------------------------------------- layout

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_ready) LayoutWindow();
        }

        private void LayoutWindow()
        {
            var width = ClientSize.Width;
            var height = ClientSize.Height;

            _header.SetBounds(0, 0, width, HeaderHeight);
            _progressTrack.SetBounds(0, HeaderHeight, width, ProgressHeight);
            _summary.SetBounds(0, HeaderHeight + ProgressHeight, width, SummaryHeight);
            _tabStrip.SetBounds(0, HeaderHeight + ProgressHeight + SummaryHeight, width, TabStripHeight);

            var contentTop = HeaderHeight + ProgressHeight + SummaryHeight + TabStripHeight;
            var contentHeight = height - FooterHeight - contentTop;

            foreach (var page in _pages)
            {
                page.SetBounds(0, contentTop, width, contentHeight);
            }

            _footer.SetBounds(0, height - FooterHeight, width, FooterHeight);

            LayoutHeader(width);
            LayoutTabStrip(width);
            LayoutFooter(width);
            LayoutTiles(width);

            var listWidth = ListWidth();
            if (listWidth > 160 && listWidth != _listWidth)
            {
                _listWidth = listWidth;
                RebuildFindingList();
                RebuildCheckList();
            }
        }

        private void LayoutHeader(int width)
        {
            var right = width - Gutter;

            _runButton.SetBounds(right - 140, 16, 140, 32);
            right -= 140 + 12;

            _days.SetBounds(right - 56, 18, 56, 28);
            right -= 56 + 4;

            _daysLabel.SetBounds(right - 46, 21, 46, 22);
            right -= 46 + 16;

            _elevation.SetBounds(right - 108, 19, 108, 26);
        }

        private void LayoutTabStrip(int width)
        {
            var x = Gutter;
            foreach (var button in _pageButtons)
            {
                button.SetBounds(x, 4, 132, 32);
                x += 132;
            }
        }

        private void LayoutFooter(int width)
        {
            var right = width - Gutter;

            _copyButton.SetBounds(right - 110, 9, 110, 28);
            right -= 110 + 8;

            _saveButton.SetBounds(right - 110, 9, 110, 28);
            right -= 110 + 8;

            _openButton.SetBounds(right - 110, 9, 110, 28);

            _status.SetBounds(Gutter, 13, right - 110 - Gutter - 16, 24);
        }

        private void LayoutTiles(int width)
        {
            var tiles = new SummaryTile[] { _criticalTile, _warningTile, _infoTile, _timeTile };
            var gap = 12;
            var tileWidth = (width - Gutter * 2 - gap * (tiles.Length - 1)) / tiles.Length;
            var x = Gutter;
            var y = 10;

            foreach (var tile in tiles)
            {
                tile.SetBounds(x, y, tileWidth, SummaryHeight - 20);
                x += tileWidth + gap;
            }
        }

        // ----------------------------------------------------------------- building the window

        private void BuildHeader()
        {
            var logo = new Panel
            {
                BackColor = Theme.Accent,
                Location = new Point(Gutter, 16),
                Size = new Size(32, 32)
            };

            var mark = Theme.MakeLabel("W", Theme.Title, Color.White);
            mark.TextAlign = ContentAlignment.MiddleCenter;
            mark.SetBounds(0, 0, 32, 32);
            logo.Controls.Add(mark);
            logo.Resize += delegate { Theme.RoundCorners(logo, 9); };

            var title = Theme.MakeLabel("WinHealthAudit", Theme.Title, Theme.Text);
            title.SetBounds(62, 13, 320, 24);

            var subtitle = Theme.MakeLabel("Read-only health report for Windows", Theme.Small, Theme.Muted);
            subtitle.SetBounds(62, 37, 420, 18);

            _elevation = new Label
            {
                Text = AuditContext.IsAdministrator() ? "administrator" : "no admin rights",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                BackColor = Theme.SurfaceHover,
                TextAlign = ContentAlignment.MiddleCenter
            };

            _daysLabel = Theme.MakeLabel("days", Theme.Small, Theme.Muted);
            _daysLabel.TextAlign = ContentAlignment.MiddleRight;

            _days = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 365,
                Value = 14,
                Font = Theme.Body,
                ForeColor = Theme.Text,
                BackColor = Theme.SurfaceHover,
                TextAlign = HorizontalAlignment.Right
            };

            _runButton = Theme.MakeButton("Run audit", delegate { StartAudit(); }, Theme.Accent);

            _header.Controls.Add(logo);
            _header.Controls.Add(title);
            _header.Controls.Add(subtitle);
            _header.Controls.Add(_elevation);
            _header.Controls.Add(_daysLabel);
            _header.Controls.Add(_days);
            _header.Controls.Add(_runButton);
        }

        private void BuildSummary()
        {
            _criticalTile = new SummaryTile("critical", Theme.Critical);
            _warningTile = new SummaryTile("warning", Theme.Warning);
            _infoTile = new SummaryTile("info", Theme.Info);
            _timeTile = new SummaryTile("run time", Theme.Good);

            _summary.Controls.Add(_criticalTile);
            _summary.Controls.Add(_warningTile);
            _summary.Controls.Add(_infoTile);
            _summary.Controls.Add(_timeTile);
        }

        private void BuildPages()
        {
            _findingList = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Theme.Screen,
                Padding = new Padding(16, 14, 8, 16)
            };

            _filterBar = new Panel { BackColor = Theme.Screen, Size = new Size(400, 28), Margin = new Padding(0, 0, 0, 6) };
            _showCritical = FilterBox("Critical", Theme.Critical, 0, delegate { RebuildFindingList(); });
            _showWarning = FilterBox("Warning", Theme.Warning, 96, delegate { RebuildFindingList(); });
            _showInfo = FilterBox("Info", Theme.Info, 196, delegate { RebuildFindingList(); });
            _filterBar.Controls.Add(_showCritical);
            _filterBar.Controls.Add(_showWarning);
            _filterBar.Controls.Add(_showInfo);

            var findingsPage = new Panel { BackColor = Theme.Screen };
            findingsPage.Controls.Add(_findingList);

            _checkList = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Theme.Screen,
                Padding = new Padding(16, 14, 8, 16)
            };

            var checksPage = new Panel { BackColor = Theme.Screen };
            checksPage.Controls.Add(_checkList);

            _log = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Mono,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(0)
            };

            var logPage = new Panel { BackColor = Theme.Surface, Padding = new Padding(12) };
            logPage.Controls.Add(_log);

            _pages = new Panel[] { findingsPage, checksPage, logPage };

            _pageButtons = new Button[]
            {
                PageButton("Findings", 0),
                PageButton("Details", 1),
                PageButton("Log", 2)
            };

            _tabStrip.Controls.Add(_pageButtons[0]);
            _tabStrip.Controls.Add(_pageButtons[1]);
            _tabStrip.Controls.Add(_pageButtons[2]);
        }

        private void BuildFooter()
        {
            _status = Theme.MakeLabel("Ready.", Theme.Small, Theme.Muted);
            _status.TextAlign = ContentAlignment.MiddleLeft;

            _copyButton = Theme.MakeButton("Copy summary", delegate { CopySummary(); }, Theme.SurfaceHover);
            _saveButton = Theme.MakeButton("Save report", delegate { SaveReport(); }, Theme.SurfaceHover);
            _openButton = Theme.MakeButton("Open report", delegate { OpenReport(); }, Theme.SurfaceHover);

            _copyButton.Enabled = false;
            _saveButton.Enabled = false;
            _openButton.Enabled = false;

            _footer.Controls.Add(_copyButton);
            _footer.Controls.Add(_saveButton);
            _footer.Controls.Add(_openButton);
            _footer.Controls.Add(_status);
        }

        private Button PageButton(string text, int page)
        {
            return Theme.MakeButton(text, delegate { SelectPage(page); }, Theme.Surface);
        }

        private CheckBox FilterBox(string text, Color colour, int x, EventHandler onChanged)
        {
            var box = new CheckBox
            {
                Text = text,
                Font = Theme.SmallBold,
                ForeColor = colour,
                BackColor = Theme.Screen,
                AutoSize = true,
                Checked = true,
                Location = new Point(x, 5)
            };

            box.CheckedChanged += onChanged;
            return box;
        }

        private void SelectPage(int index)
        {
            for (var i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visible = i == index;
                _pageButtons[i].BackColor = i == index ? Theme.Accent : Theme.Surface;
                _pageButtons[i].ForeColor = i == index ? Color.White : Theme.Muted;
            }
        }

        // ----------------------------------------------------------------- running the audit

        private void StartAudit()
        {
            if (_running) return;

            _running = true;
            _runButton.Enabled = false;
            _openButton.Enabled = false;
            _saveButton.Enabled = false;
            _copyButton.Enabled = false;

            _log.Clear();
            ShowProgress(0, 1);
            SelectPage(2);
            _status.Text = "Starting...";

            var options = new AuditOptions();
            options.WindowDays = (int)_days.Value;
            options.OutputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reports");

            var writer = new LogWriter(this);
            var worker = new Thread(delegate() { RunAudit(options, writer); });
            worker.IsBackground = true;
            worker.Start();
        }

        private void RunAudit(AuditOptions options, TextWriter writer)
        {
            var startedAt = DateTime.Now;
            var context = new AuditContext(options, writer);
            var runner = new AuditRunner(HealthChecks.All(), ReportProgress);
            var results = runner.Run(context);

            string reportPath = null;
            try
            {
                reportPath = ReportWriter.Write(results, context);
            }
            catch (Exception ex)
            {
                WriteLog("Could not save the report: " + ex.Message);
            }

            var seconds = (DateTime.Now - startedAt).TotalSeconds;
            Post(delegate { FinishAudit(results, reportPath, seconds); });
        }

        private void ReportProgress(int done, int total, string name)
        {
            Post(delegate
            {
                ShowProgress(done, total);
                _status.Text = string.Format(CultureInfo.InvariantCulture,
                    "Running {0} of {1}: {2}", done, total, name);
            });
        }

        private void FinishAudit(IList<CheckResult> results, string reportPath, double seconds)
        {
            _results = results;
            _reportPath = reportPath;
            _running = false;

            _progressTrack.Visible = false;
            _runButton.Enabled = true;
            _copyButton.Enabled = true;
            _openButton.Enabled = reportPath != null;
            _saveButton.Enabled = reportPath != null;

            _criticalTile.Show(ReportWriter.Count(results, Severity.Critical).ToString(CultureInfo.InvariantCulture));
            _warningTile.Show(ReportWriter.Count(results, Severity.Warning).ToString(CultureInfo.InvariantCulture));
            _infoTile.Show(ReportWriter.Count(results, Severity.Info).ToString(CultureInfo.InvariantCulture));
            _timeTile.Show(seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s");

            _showCritical.Text = "Critical (" + _criticalTile.Value + ")";
            _showWarning.Text = "Warning (" + _warningTile.Value + ")";
            _showInfo.Text = "Info (" + _infoTile.Value + ")";

            RebuildFindingList();
            RebuildCheckList();
            SelectPage(0);

            _status.Text = string.Format(CultureInfo.InvariantCulture,
                reportPath != null
                    ? "Finished in {0:0.0} s - report saved to {1}"
                    : "Finished in {0:0.0} s - the report could not be saved",
                seconds, reportPath ?? string.Empty);
        }

        private void ShowProgress(int done, int total)
        {
            _progressTrack.Visible = true;
            _progressFraction = total <= 0 ? 0 : (double)done / total;
            _progressTrack.Invalidate();
        }

        private void PaintProgress(object sender, PaintEventArgs e)
        {
            var fill = (int)(_progressTrack.Width * _progressFraction);
            if (fill > 0) e.Graphics.FillRectangle(Theme.AccentBrush, 0, 0, fill, _progressTrack.Height);
        }

        // ----------------------------------------------------------------- drawing the results

        private void RebuildFindingList()
        {
            var width = ListWidth();
            if (width < 160) return;

            _findingList.SuspendLayout();
            _findingList.Controls.Clear();

            _filterBar.Size = new Size(width, 28);
            _findingList.Controls.Add(_filterBar);

            if (_results == null)
            {
                _findingList.Controls.Add(Hint("Press \"Run audit\" to check this machine.", width));
            }
            else
            {
                var findings = FilteredFindings();
                if (findings.Count == 0)
                {
                    _findingList.Controls.Add(Hint("Nothing matches the current filter.", width));
                }

                foreach (var finding in findings)
                {
                    var card = new FindingCard(finding, width, CopyToClipboard);
                    card.Margin = new Padding(0, 0, 0, 10);
                    _findingList.Controls.Add(card);
                }
            }

            _findingList.ResumeLayout();
        }

        private void RebuildCheckList()
        {
            var width = ListWidth();
            if (width < 160 || _results == null) return;

            _checkList.SuspendLayout();
            _checkList.Controls.Clear();

            foreach (var result in _results)
            {
                var panel = new CheckPanel(result, width);
                panel.Margin = new Padding(0, 0, 0, 12);
                _checkList.Controls.Add(panel);
            }

            _checkList.ResumeLayout();
        }

        private List<Finding> FilteredFindings()
        {
            var findings = new List<Finding>();

            foreach (var result in _results)
            {
                foreach (var finding in result.Findings)
                {
                    if (finding.Severity == Severity.Critical && !_showCritical.Checked) continue;
                    if (finding.Severity == Severity.Warning && !_showWarning.Checked) continue;
                    if (finding.Severity == Severity.Info && !_showInfo.Checked) continue;
                    findings.Add(finding);
                }
            }

            findings.Sort(delegate(Finding left, Finding right)
            {
                if (left.Severity != right.Severity)
                {
                    return right.Severity.CompareTo(left.Severity);
                }

                return string.Compare(left.Source, right.Source, StringComparison.OrdinalIgnoreCase);
            });

            return findings;
        }

        private Label Hint(string text, int width)
        {
            var hint = Theme.MakeLabel(string.Empty, Theme.Small, Theme.Muted);
            Theme.PlaceText(hint, text, Theme.Small, Theme.Muted, 0, 0, width - 8);
            hint.Height += 12;
            hint.TextAlign = ContentAlignment.TopLeft;
            return hint;
        }

        private int ListWidth()
        {
            return _findingList.ClientSize.Width
                   - _findingList.Padding.Horizontal
                   - SystemInformation.VerticalScrollBarWidth;
        }

        // ----------------------------------------------------------------- footer actions

        private void CopySummary()
        {
            if (_results == null) return;

            var text = new StringBuilder();
            text.AppendLine("WinHealthAudit - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Critical: {0}, Warning: {1}, Info: {2}",
                ReportWriter.Count(_results, Severity.Critical),
                ReportWriter.Count(_results, Severity.Warning),
                ReportWriter.Count(_results, Severity.Info)));
            text.AppendLine();

            foreach (var result in _results)
            {
                foreach (var finding in result.Findings)
                {
                    if (finding.Severity == Severity.Info) continue;
                    text.AppendLine("[" + Theme.SeverityWord(finding.Severity) + "] " +
                                    finding.Source + " - " + finding.Message);
                }
            }

            CopyToClipboard(text.ToString().TrimEnd());
        }

        private void SaveReport()
        {
            if (_reportPath == null || !File.Exists(_reportPath)) return;

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "Markdown|*.md|Text|*.txt|All files|*.*";
                dialog.FileName = Path.GetFileName(_reportPath);

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    File.Copy(_reportPath, dialog.FileName, true);
                    _status.Text = "Report saved to " + dialog.FileName;
                }
                catch (Exception ex)
                {
                    _status.Text = "Could not save the report: " + ex.Message;
                }
            }
        }

        private void OpenReport()
        {
            if (_reportPath == null) return;

            try
            {
                Process.Start(_reportPath);
                _status.Text = "Opened " + Path.GetFileName(_reportPath);
            }
            catch (Exception ex)
            {
                _status.Text = "Could not open the report: " + ex.Message;
            }
        }

        private void CopyToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
                _status.Text = "Copied to the clipboard.";
            }
            catch (Exception ex)
            {
                _status.Text = "Could not copy: " + ex.Message;
            }
        }

        // ----------------------------------------------------------------- plumbing

        private void WriteLog(string line)
        {
            Post(delegate { _log.AppendText(line + Environment.NewLine); });
        }

        private void Post(Action work)
        {
            if (IsDisposed || !IsHandleCreated) return;

            try
            {
                BeginInvoke(work);
            }
            catch (InvalidOperationException)
            {
                // The window went away while the check was still running.
            }
        }

        private static int ReadDays(string[] args)
        {
            for (var index = 0; index + 1 < args.Length; index++)
            {
                if (args[index] != "--days" && args[index] != "-d") continue;

                int days;
                if (int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out days) &&
                    days >= 1 && days <= 365)
                {
                    return days;
                }
            }

            return 14;
        }

        private void SetDays(int days)
        {
            _days.Value = days;
        }

        /// <summary>Feeds the live progress lines of the audit into the Log tab.</summary>
        private sealed class LogWriter : TextWriter
        {
            private MainForm _form;

            public LogWriter(MainForm form)
            {
                _form = form;
            }

            public override Encoding Encoding
            {
                get { return Encoding.UTF8; }
            }

            public override void WriteLine(string value)
            {
                _form.WriteLog(value);
            }
        }
    }
}
