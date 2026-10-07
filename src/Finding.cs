namespace WinHealthAudit
{
    public sealed class Finding
    {
        public Finding(Severity severity, string source, string message, string evidence,
            string rule = null, string detail = null, string advice = null)
        {
            Severity = severity;
            Source = source ?? string.Empty;
            Message = message ?? string.Empty;
            Evidence = evidence ?? string.Empty;
            Rule = rule ?? string.Empty;
            Detail = detail ?? string.Empty;
            Advice = advice ?? string.Empty;
        }

        public Severity Severity { get; private set; }

        public string Source { get; private set; }

        public string Message { get; private set; }

        public string Evidence { get; private set; }

        public string Rule { get; private set; }

        public string Detail { get; private set; }

        public string Advice { get; private set; }
    }
}
