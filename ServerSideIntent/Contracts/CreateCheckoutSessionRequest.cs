using System.ComponentModel.DataAnnotations;

namespace ServerSideIntent.Contracts
{
    public class CreateCheckoutSessionRequest
    {
        [Required(ErrorMessage = "Item ID must be provided.")]
        public required string ItemId { get; set; }

        [Range(1, 100, ErrorMessage = "Quantity must be between 1 and 100.")]
        public long Quantity { get; set; }
    } 
}
