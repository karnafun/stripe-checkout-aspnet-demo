using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using Microsoft.Extensions.Logging;
using ServerSideIntent.Contracts;
using ServerSideIntent.Repositories;
using ServerSideIntent.Services;

[ApiController]
[Route("api/[controller]")]
public class CheckoutController : ControllerBase
{
    private readonly ILogger<CheckoutController> _logger;
    private readonly string _domain = "https://localhost:5001";
    private readonly ProductService _productService;
    private readonly OrderService _orderService;

    // NOTE: We assume StripeConfiguration.ApiKey is set globally in Program.cs.
    public CheckoutController(ILogger<CheckoutController> logger, ProductService productService, OrderService orderService)
    {
        _logger = logger;
        _productService = productService;
        _orderService = orderService;
    }

    [HttpPost("create-session")]
    public async Task<IActionResult> CreateSession([FromBody] CreateCheckoutSessionRequest request)
    {
        // 1. CRITICAL SECURITY STEP: Look up the TRUSTED PriceId using the ItemId via the ProductService.
        if (!_productService.TryGetStripePriceId(request.ItemId, out string securePriceId))
        {
            _logger.LogWarning("CreateSession requested unknown ItemId: {ItemId}", request.ItemId);
            return BadRequest(new { message = $"Invalid ItemId: {request.ItemId}." });
        }

        // 2. Register internal order ID and save a 'pending' record to the DB.
        
        string internalOrderId = _orderService.CreatePendingOrder();
        _logger.LogInformation("Creating new pending order: {OrderId} for ItemId: {ItemId}", internalOrderId, request.ItemId);

        var options = new SessionCreateOptions
        {
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    Price = securePriceId,
                    Quantity = request.Quantity,
                },
            },
            Mode = "payment",
            // FIX: Uses the root path /success as per best practice.
            SuccessUrl = _domain + "/success?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = _domain + "/cancel",

            // CRITICAL: Pass your internal order ID to Stripe via Metadata
            Metadata = new Dictionary<string, string>
            {
                { "internal_order_id", internalOrderId },
                { "item_id", request.ItemId }
            }
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options);

        _logger.LogInformation("Stripe Session {SessionId} created for Order {OrderId}", session.Id, internalOrderId);

        return Ok(new { sessionId = session.Id });
    }

}