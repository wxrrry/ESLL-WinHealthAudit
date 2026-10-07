using System;
using System.Collections.Generic;

namespace WinHealthAudit
{
    public sealed class AuditRunner
    {
        private readonly IList<IHealthCheck> _checks;
        private readonly Action<int, int, string> _progress;

        public AuditRunner(IEnumerable<IHealthCheck> checks)
            : this(checks, null)
        {
        }

        /// <param name="progress">Optional callback: (completed, total, check name).</param>
        public AuditRunner(IEnumerable<IHealthCheck> checks, Action<int, int, string> progress)
        {
            _checks = new List<IHealthCheck>(checks);
            _progress = progress;
        }

        public IList<CheckResult> Run(AuditContext context)
        {
            var results = new List<CheckResult>(_checks.Count);

            for (var index = 0; index < _checks.Count; index++)
            {
                var check = _checks[index];
                context.Trace(string.Format("[{0}/{1}] {2}", index + 1, _checks.Count, check.Name));

                if (_progress != null) _progress(index + 1, _checks.Count, check.Name);

                try
                {
                    results.Add(check.Run(context));
                }
                catch (Exception ex)
                {
                    var failed = new CheckResult(check.Name, "the check threw an exception before it could finish");
                    failed.Fail("Check aborted: " + ex.Message, ex.GetType().FullName);
                    results.Add(failed);
                }
            }

            return results;
        }
    }
}
