namespace WinHealthAudit
{
    public interface IHealthCheck
    {
        string Name { get; }

        CheckResult Run(AuditContext context);
    }
}
