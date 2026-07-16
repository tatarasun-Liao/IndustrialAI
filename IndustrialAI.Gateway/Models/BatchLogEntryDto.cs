namespace IndustrialAI.Gateway.Models
{
    public class BatchLogEntryDto
    {
        public string Message { get; set; } = "";
        public string Level { get; set; } = "Info"; // Info, Warning, Error
        public string? Source { get; set; }
    }
}
