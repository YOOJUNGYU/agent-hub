// ReSharper disable InconsistentNaming
namespace AgentHub.Common.Models
{
    public class Error
    {
        public int code { get; set; } = 500;
        public string message { get; set; } = "Internal Server Error";
    }
}
