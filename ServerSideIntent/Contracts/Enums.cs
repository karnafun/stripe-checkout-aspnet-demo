namespace ServerSideIntent.Contracts
{
    /// <summary>
    /// Defines the secure, sequential states for an order during payment processing.
    /// </summary>
    public enum OrderStatus
    {
        // The order is created in our backend (CheckoutController).
        Pending,

        // The payment is verified via the client-side success callback (CallbackController).
        // This state is NOT sufficient for fulfillment; it's a temporary confirmation.
        Confirmed,

        // The payment is guaranteed via the server-to-server webhook (WebhookController).
        // This state allows for irreversible fulfillment logic.
        Fulfilled,

        // Used to indicate if the order failed payment, was canceled, or otherwise invalid.
        Failed
    }
}
