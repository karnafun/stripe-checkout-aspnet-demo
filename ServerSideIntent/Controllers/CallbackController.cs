using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using Microsoft.Extensions.Logging;
using ServerSideIntent.Contracts;
using ServerSideIntent.Repositories;
using ServerSideIntent.Services;

[ApiController]
[Route("[controller]")]     
public class CallbackController : ControllerBase
{
    private readonly ILogger<CallbackController> _logger;
    private readonly string _stripeSecretKey;
    private readonly OrderService _orderService;

    public CallbackController(ILogger<CallbackController> logger, IConfiguration configuration, OrderService orderService)
    {
        _logger = logger;
        _orderService = orderService;
        _stripeSecretKey = configuration["Stripe:SecretKey"];

        if (!string.IsNullOrEmpty(_stripeSecretKey))
        {
            StripeConfiguration.ApiKey = _stripeSecretKey;
        }
        else
        {
            _logger.LogWarning("Stripe Secret Key not configured. Server-side payment verification will fail.");
        }

    }

    /// <summary>
    /// GET /success
    /// This is the client-facing endpoint. It MUST verify payment and set the order to 'Confirmed'.
    /// Responsibility: Client-side verification and atomic state update to CONFIRMED.
    /// </summary>
    [HttpGet("/success")]
    public async Task<IActionResult> Success([FromQuery] string session_id)
    {
        if (string.IsNullOrEmpty(session_id))
        {
            _logger.LogWarning("Success redirect missing Stripe session ID.");
            return BadRequest(new { message = "Missing Stripe session ID." });
        }

        if (string.IsNullOrEmpty(_stripeSecretKey))
        {
            _logger.LogError("Stripe Secret Key missing. Cannot verify payment status.");
            return StatusCode(500, new { message = "Server configuration error. Cannot verify payment." });
        }

        try
        {
            // 1. Verify Session Status
            var service = new SessionService();
            var session = await service.GetAsync(session_id, new SessionGetOptions
            {
                Expand = new List<string> { "payment_intent" }
            });

            if (session.PaymentStatus != "paid")
            {
                _logger.LogWarning("Success redirect received for non-paid status: {Status}", session.PaymentStatus);
                return BadRequest(new { message = $"Payment status is not 'paid': {session.PaymentStatus}" });
            }

            string internalOrderId = session.Metadata.GetValueOrDefault("internal_order_id", "Unknown");

            // 2. ATOMIC STATE TRANSITION
            // We only update the status if it's still 'Pending' (meaning the webhook hasn't won the race yet).
            bool updated = _orderService.TryConfirmOrder(internalOrderId);

            if (!updated)
            {
                // Check current state to see why the update failed (Webhook fulfilled it)
                OrderStatus currentStatus = _orderService.GetCurrentStatus(internalOrderId);

                if (currentStatus == OrderStatus.Fulfilled)
                {
                    _logger.LogInformation("Order {OrderId} is already FULFILLED by the webhook. Skipping 'Confirmed' update.", internalOrderId);
                }
                else
                {
                    // This indicates an unexpected state or multi-threading issue in the mock database.
                    _logger.LogError("Failed to update Order {OrderId} status from Pending to Confirmed. Current Status: {Status}", internalOrderId, currentStatus);
                }
            }
            else
            {
                _logger.LogInformation("--- CALLBACK SUCCESS VERIFIED ---");
                _logger.LogInformation("Order {OrderId} status successfully updated from Pending to 'Confirmed'.", internalOrderId);
            }

            // 3. Return confirmation to the user
            return Ok(
            new
            {
                message = "Payment Confirmed and Verified! Your order is now processing.",
                localOrderId = internalOrderId,
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe API Error during /success verification.");
            return StatusCode(500, new { message = "Internal error confirming payment." });
        }
    }
}