using System.ComponentModel.DataAnnotations;
using FinSim.Domain.Models.Enums;

namespace FinSim.Domain.Dtos
{
    public class InstrumentDto
    {
        public Guid Id { get; set; }
        public string Symbol { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal BasePrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public bool IsActive { get; set; }
    }
    public class CreateInstrumentRequest
    {
        [Required, StringLength(10, MinimumLength = 1)]
        public string Symbol { get; set; } = default!;   // THYAO, ASELS

        [Required, StringLength(120, MinimumLength = 1)]
        public string Name { get; set; } = default!;

        [Range(0.0001, 1_000_000)]
        public decimal BasePrice { get; set; }
        public bool isActive = true;
    }
    public class SetInstrumentActiveRequest
    {
        public bool IsActive { get; set; }
    }
    public enum CreateInstrumentResult
    {
        Success,
        InvalidSymbol,
        InvalidName,
        InvalidPrice,
        DuplicateSymbol
    }
}