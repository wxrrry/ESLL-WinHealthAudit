namespace WinHealthAudit
{
    public sealed class Finding
    {
        public Finding(Severity severity, string source, string message, string evidence)
        {
            Severity = severity;
            Source = source ?? string.Empty;
            Message = message ?? string.Empty;
            Evidence = evidence ?? string.Empty;
        }

        public Severity Severity { get; private set; }

        public string Source { get; private set; }

        public string Message { get; private set; }

        public string Evidence { get; private set; }
    }
}
