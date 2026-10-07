using System.Collections.Generic;

namespace WinHealthAudit
{
    public sealed class CheckResult
    {
        public CheckResult(string name, string detail = null)
        {
            Name = name;
            Detail = detail ?? string.Empty;
            Notes = new List<string>();
            Findings = new List<Finding>();
        }

        public string Name { get; private set; }

        public string Detail { get; private set; }

        public List<string> Notes { get; private set; }

        public List<Finding> Findings { get; private set; }

        public void Note(string text)
        {
            Notes.Add(text ?? string.Empty);
        }

        public void Add(Severity severity, string message, string evidence,
            string rule = null, string detail = null, string advice = null)
        {
            Findings.Add(new Finding(severity, Name, message, evidence, rule, detail, advice));
        }

        public void Warn(string message, string evidence,
            string rule = null, string detail = null, string advice = null)
        {
            Add(Severity.Warning, message, evidence, rule, detail, advice);
        }

        public void Fail(string message, string evidence,
            string rule = null, string detail = null, string advice = null)
        {
            Add(Severity.Critical, message, evidence, rule, detail, advice);
        }
    }
}
