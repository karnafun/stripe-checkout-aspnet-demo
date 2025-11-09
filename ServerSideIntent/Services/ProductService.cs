using System.Collections.Generic;

/// <summary>
/// Mocks a dedicated service layer responsible for securely retrieving product data 
/// and the associated immutable Stripe Price IDs from an authoritative source (like a database).
/// </summary>
public class ProductService
{
    private readonly Dictionary<string, string> SecurePriceMap = new()
    {
        // Internal SKU  -> Stripe Price ID
        {"premium_product_demo", "price_1SRScgBMXnXVzQYDfGALFJ1b" }, 
        {"PREMIUM_COURSE", "price_2X..." },
    };

    /// <summary>
    /// Attempts to retrieve the official Stripe Price ID for a given internal product ItemId.
    /// </summary>
    /// <param name="itemId">The internal SKU of the product.</param>
    /// <param name="securePriceId">The retrieved Stripe Price ID.</param>
    /// <returns>True if the ItemId was found and is valid; otherwise, false.</returns>
    public bool TryGetStripePriceId(string itemId, out string securePriceId)
    {
        return SecurePriceMap.TryGetValue(itemId, out securePriceId);
    }
}