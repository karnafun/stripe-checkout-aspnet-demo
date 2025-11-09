using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using Microsoft.Extensions.Logging;
using ServerSideIntent.Contracts;
using ServerSideIntent.Repositories;
using ServerSideIntent.Services;
using System.Diagnostics.Eventing.Reader; // <-- NEW: Import for ILogger

// We use ControllerBase because webhooks are pure API endpoints and don't need MVC View support.
[Route("stripe-webhook")]
public class WebhookController : ControllerBase
{
    // WARNING: Replace this with your actual Webhook Signing Secret from the Stripe CLI or Dashboard.
    private readonly string _webhookSecret;

    // --- NEW: Inject ILogger for reliable logging ---
    private readonly ILogger<WebhookController> _logger;
    private readonly OrderService _orderService;

    public WebhookController(ILogger<WebhookController> logger, IConfiguration configuration, OrderService orderService)
    {
        _logger = logger;
        _orderService = orderService;
        _webhookSecret = configuration["Stripe:WebhookSecret"];

        // Mandatory check for production systems
        if (string.IsNullOrEmpty(_webhookSecret))
        {
            // Note: In a real system, this would be a Critical Error and stop startup.
            _logger.LogWarning("Stripe Webhook Secret not configured. Webhook security is disabled.");
        }
    }

    /// <summary>
    /// POST /stripe-webhook
    /// Responsibility: Guaranteed fulfillment via server-to-server communication.
    /// This is the ONLY place where the order status is set to FULFILLED.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Index()
    {
        // 1. Read the request body
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        // 2. Check for missing secret
        if (string.IsNullOrEmpty(_webhookSecret))
        {
            _logger.LogError("Webhook Secret is not set. Returning 500 to prevent processing unverified events.");
            return StatusCode(500);
        }

        try
        {
            // 3. CRITICAL SECURITY STEP: Verify the event signature
            var stripeEvent = EventUtility.ConstructEvent(
                json,
                Request.Headers["Stripe-Signature"],
                _webhookSecret
            );

            // 4. Handle the event
            if (stripeEvent.Type == "checkout.session.completed")
            {
                var session = stripeEvent.Data.Object as Session;
                string internalOrderId = session.Metadata.GetValueOrDefault("internal_order_id", "Unknown");

                _logger.LogInformation("--- WEBHOOK FULFILLMENT TRIGGERED (START) ---");
                _logger.LogInformation("Received Checkout Session Completed for Order: {OrderId} (Session ID: {SessionId})", internalOrderId, session.Id);

                // 5. ATOMIC STATE TRANSITION
                // Attempt to update the status to FULFILLED ONLY IF it is PENDING or CONFIRMED.
                // We must handle both initial states as the /success callback is asynchronous.

                                
                bool updateOrderSuccess = _orderService.TryFulfillOrder(internalOrderId);
                if (updateOrderSuccess)
                {
                    // FULFILLMENT LOGIC GOES HERE (Email, provisioning access, shipping, etc.)
                    _logger.LogInformation("Order {OrderId} fulfillment complete (END). Status: FULFILLED.", internalOrderId);
                }
                else
                {
                    var currentStatus = _orderService.GetCurrentStatus(internalOrderId);

                    if (currentStatus == OrderStatus.Fulfilled)
                    {
                        // Expected: Idempotent webhook call
                        _logger.LogInformation("Order {OrderId} already fulfilled. Idempotent webhook.", internalOrderId);
                    }
                    else
                    {
                        // UNEXPECTED: Order exists but couldn't transition to Fulfilled
                        _logger.LogError("CRITICAL: Order {OrderId} fulfillment failed. Current status: {Status}. This indicates a state machine or concurrency issue.",
                            internalOrderId, currentStatus);
                    }
                }
            }

            // 6. Acknowledge the event
            return Ok();
        }
        catch (StripeException ex)
        {
            // Bad signature, unexpected data, etc.
            _logger.LogError(ex, "Stripe Webhook Error.");
            return BadRequest();
        }
    }
}