using System.ComponentModel.DataAnnotations;

namespace PagoTicAPI.API.AutomaticDebits.Scheduling;

public sealed class AutomaticDebitSchedulerOptions
{
    public const string SectionName = "AutomaticDebitScheduler";

    public bool Enabled { get; set; }

    [Range(10, 86400)]
    public int IntervalSeconds { get; set; } = 300;

    [Range(10, 86400)]
    public int ExecutionTimeoutSeconds { get; set; } = 120;

    [Range(10, 86400)]
    public int LeaseSeconds { get; set; } = 240;

    [Required, MaxLength(100)]
    public string LeaseName { get; set; } = "GENERACION_DEBITOS_AUTOMATICOS";
}
