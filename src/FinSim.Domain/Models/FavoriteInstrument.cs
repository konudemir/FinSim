namespace FinSim.Domain.Models
{
    public class FavoriteInstrument
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid InstrumentId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        public User User { get; set; } = null!;
        public Instrument Instrument { get; set; } = null!;
    }
}
