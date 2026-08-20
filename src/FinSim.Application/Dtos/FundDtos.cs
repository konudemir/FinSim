using System.ComponentModel.DataAnnotations;

namespace FinSim.Application.Dtos
{
    public record FundHoldingDto(
        Guid ConstituentId,
        string Symbol,
        string Name,
        int Quantity,
        decimal CurrentPrice,
        decimal Value,
        decimal WeightPercent);

    public record FundDto(
        Guid Id,
        string Symbol,
        string Name,
        decimal BasePrice,
        decimal CurrentPrice,
        decimal Divisor,
        decimal Nav,
        bool IsActive,
        List<FundHoldingDto> Holdings);

    public class FundHoldingInput
    {
        [Required]
        public Guid ConstituentId { get; set; }

        [Range(1, 1_000_000)]
        public int Quantity { get; set; }
    }

    public class CreateFundRequest
    {
        [Required, StringLength(10, MinimumLength = 1)]
        public string Symbol { get; set; } = default!;

        [Required, StringLength(120, MinimumLength = 1)]
        public string Name { get; set; } = default!;

        [Range(0.01, 1_000_000)]
        public decimal BasePrice { get; set; } = 100m;

        [Required, MinLength(1)]
        public List<FundHoldingInput> Holdings { get; set; } = [];
    }

    public class RebalanceFundRequest
    {
        [Required, MinLength(1)]
        public List<FundHoldingInput> Holdings { get; set; } = [];

        [StringLength(500)]
        public string? Reason { get; set; }
    }

    public enum FundResult
    {
        Success,
        NotFound,
        InvalidSymbol,
        InvalidName,
        InvalidPrice,
        DuplicateSymbol,
        NoHoldings,
        DuplicateConstituent,
        ConstituentNotFound,
        ConstituentInactive,
        ConstituentNotStock,
        InvalidQuantity,
        InvalidNav,
        ConcurrencyConflict
    }
}