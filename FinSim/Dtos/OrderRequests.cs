using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using FinSim.Models.Enums;

namespace FinSim.Dtos
{
    public class CreateMarketOrderRequest
    {
        [Required]public Guid UserId { get; set; }
        [Required]public Guid InstrumentId { get; set; }
        [Required]public OrderDirection Direction { get; set; }
        [Range(1, int.MaxValue, ErrorMessage = "At least 1 in quantity.")]
        public int Quantity { get; set; }
    }

    public class CreateLimitOrderRequest
    {
        [Required]public Guid UserId { get; set; }
        [Required]public Guid InstrumentId { get; set; }
        [Required]public OrderDirection Direction { get; set; }
        [Range(1, int.MaxValue, ErrorMessage = "At least 1 in quantity.")]
        public int Quantity { get; set; }
        [Required]public decimal Price { get; set; }
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