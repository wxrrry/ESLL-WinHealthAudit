using System.Collections.Generic;

namespace WinHealthAudit
{
    public sealed class CheckResult
    {
        public CheckResult(string name)
        {
            Name = name;
            Notes = new List<string>();
            Findings = new List<Finding>();
        }

        public string Name { get; private set; }

        public List<string> Notes { get; private set; }

        public List<Finding> Findings { get; private set; }

        public void Note(string text)
        {
            Notes.Add(text ?? string.Empty);
        }

        public void Add(Severity severity, string message, string evidence)
        {
            Findings.Add(new Finding(severity, Name, message, evidence));
        }

        public void Warn(string message, string evidence)
        {
            Add(Severity.Warning, message, evidence);
        }

        public void Fail(string message, string evidence)
        {
            Add(Severity.Critical, message, evidence);
        }
    }
}
