namespace WinHealthAudit
{
    public sealed class AuditOptions
    {
        public AuditOptions()
        {
            WindowDays = 14;
            OutputDirectory = "reports";
        }

        /// <summary>Look-back window for event log based checks, in days.</summary>
        public int WindowDays { get; set; }

        public string OutputDirectory { get; set; }

        public bool Quiet { get; set; }

        /// <summary>Print the result as one JSON document on stdout instead of the text summary.</summary>
        public bool Json { get; set; }

        public bool ShowHelp { get; set; }
    }
}
