using System.ComponentModel.DataAnnotations;
using FinSim.Domain.Models.Enums;

namespace FinSim.Domain.Dtos
{
    public class CreateMarketOrderRequest
    {
        [Required]public Guid InstrumentId { get; set; }
        [Required]public OrderDirection Direction { get; set; }
        [Range(1, int.MaxValue, ErrorMessage = "InvalidQuantity")]
        public int Quantity { get; set; }//now can be negative if shorting
    }

    public class CreateLimitOrderRequest
    {
        [Required] public Guid InstrumentId { get; set; }
        [Required] public OrderDirection Direction { get; set; }
        [Range(1, int.MaxValue, ErrorMessage = "InvalidQuantity")]
        public int Quantity { get; set; }
        [Range(0.01, double.MaxValue, ErrorMessage = "InvalidPrice")]
        public decimal Price { get; set; }
        public decimal? StopPrice { get; set; }

        // Blank in the UI means 0; all three blank/zero means the order never expires.
        [Range(0, int.MaxValue, ErrorMessage = "InvalidExpiry")]
        public int? ExpiryDays { get; set; }
        [Range(0, int.MaxValue, ErrorMessage = "InvalidExpiry")]
        public int? ExpiryHours { get; set; }
        [Range(0, int.MaxValue, ErrorMessage = "InvalidExpiry")]
        public int? ExpiryMinutes { get; set; }
    }
    public class OrderResponse
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid InstrumentId { get; set; }
        public string OrderType { get; set; } = "";
        public string Direction { get; set; } = "";
        public int Quantity { get; set; }
        public decimal? Price { get; set; }
        public string Status { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
    }
}