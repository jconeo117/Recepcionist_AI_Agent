namespace ReceptionistAgent.Core.Models;

public class MetricsSummary
{
    public string TenantId { get; set; } = "";
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int TotalMessages { get; set; }
    public int SecurityBlocks { get; set; }
    public int UniqueSessions { get; set; }
    public List<DailyCount> MessagesPerDay { get; set; } = [];

    // Business KPIs
    public int TotalBookings { get; set; }
    public double ConversionRate { get; set; }     // (sessions with booking / total sessions) * 100
    public double AbandonmentRate { get; set; }    // 100 - ConversionRate
    public double AverageTimeToBookingSeconds { get; set; } // avg seconds from first message to booking
}

public class DailyCount
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}
