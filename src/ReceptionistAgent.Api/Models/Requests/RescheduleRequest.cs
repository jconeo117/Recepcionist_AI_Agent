using System;

namespace ReceptionistAgent.Api.Models.Requests;

public class RescheduleRequest
{
    public DateTime Date { get; set; }
    public string Time { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
}
