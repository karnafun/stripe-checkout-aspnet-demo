# Stripe Checkout Integration Demo (C#, ASP.NET Core)

## Intro

A complete demonstration of a secure, asynchronous payment flow using **Stripe Checkout** with **C# ASP.NET Core**.  
This project shows how to safely handle Stripe sessions on the server, prevent client-side price tampering, and manage asynchronous race conditions between client callbacks and Stripe webhooks.  
*(This demo omits some production-level implementations, explained in detail at the end.)*

---

## Project / Setup Overview

**Architecture Components**

```
/Controllers
  CheckoutController.cs
  CallbackController.cs
  WebhookController.cs
/Services
  ProductService.cs
  OrderService.cs
/Repositories
  OrderRepository.cs
/Contracts
  Enums.cs
  CreateCheckoutSessionRequest.cs
/wwwroot
  checkout.html
```

**Endpoints**

- `POST /api/checkout/create-session` – Creates a Stripe Checkout session and registers the order as **Pending**
- `GET /success` – Client redirect handler, confirms payment and updates to **Confirmed**
- `POST /stripe-webhook` – Stripe server notification, finalizes fulfillment (**Fulfilled**)

**Order States**

- **Pending** – Initial order state after `/create-session`
- **Confirmed** – Set by `/success` endpoint when user returns from Stripe
- **Fulfilled** – Set by `/stripe-webhook`, may occur directly from Pending if webhook arrives first
- **Failed** – Only used if the mock database unexpectedly fails to fetch or update status (should never occur)

---

## How It Works

1. **Session Creation**  
   The frontend calls `/api/checkout/create-session`, sending a JSON body with `ItemId` and `Quantity`.  
   The backend maps `ItemId` to a secure Stripe `PriceId`, registers the order as *Pending*, and creates a Stripe session.

2. **Payment Process**  
   The customer completes payment on Stripe's hosted page. Stripe then both:
   - Redirects the browser to `/success`, and  
   - Sends a webhook (`/stripe-webhook`) to the server.

3. **Asynchronous Race Handling**  
   Both `/success` and `/stripe-webhook` may arrive in any order.  
   The `OrderRepository` ensures atomic updates via `ConcurrentDictionary.TryUpdate`:
   - `/success`: Pending → Confirmed  
   - `/stripe-webhook`: Pending/Confirmed → Fulfilled  
   Whichever completes first safely locks in the correct state transition.

---

## Demo / Usage

### 1. Configure Stripe Keys

You can configure your secrets either in `appsettings.json` **or** via the **.NET Secret Manager** — both are supported.

Example `appsettings.json`:

```json
"Stripe": {
  "SecretKey": "sk_test_...",
  "WebhookSecret": "whsec_..."
}
```

Retrieve your Webhook Signing Secret via Stripe CLI:

```bash
stripe listen --forward-to https://localhost:5001/stripe-webhook
```

Learn more: [Listening to webhooks with Stripe CLI](https://stripe.com/docs/stripe-cli/webhooks)

### 2. Define Product Mapping

In `ProductService.cs`, securely map internal IDs to Stripe Price IDs:

```csharp
private readonly Dictionary<string, string> SecurePriceMap = new()
{
    { "premium_product_demo", "price_1SRScgBMXnXVzQYDfGALFJ1b" }
};
```

**Important:** Stripe Price IDs are account-specific and cannot be reused across different Stripe accounts. To run this demo:

1. Create a product in your [Stripe Dashboard](https://dashboard.stripe.com/test/products)
2. Add a price to that product
3. Copy the Price ID (starts with `price_`)
4. Replace `price_1SRScgBMXnXVzQYDfGALFJ1b` with your own Price ID in `ProductService.cs`

The product ID `premium_product_demo` is what the frontend sends, but the actual Stripe Price ID must be from your own Stripe account.

### 3. Run the Project

Launch the ASP.NET Core project and open:

```bash
https://localhost:5001/checkout.html
```

Click **Proceed to Checkout** to start a test session using the predefined product configuration.

### 4. Complete Payment and Observe State

After payment, you'll be redirected to `/success` and see a JSON response showing the final order state.  
You can also observe state transitions in the backend console logs.

**Checkout Flow:**

![Checkout Button](/Docs/checkout-button.png)

**Webhook Processing:**

![Stripe Webhook](/Docs/stripe-webhook.png)

**Console Output:**

![Console Log](/Docs/console-log.png)

---

## Key Code Snippets

### CreateCheckoutSessionRequest.cs

```csharp
public class CreateCheckoutSessionRequest
{
    [Required(ErrorMessage = "Item ID must be provided.")]
    public required string ItemId { get; set; }

    [Range(1, 100, ErrorMessage = "Quantity must be between 1 and 100.")]
    public long Quantity { get; set; }
}
```

### OrderRepository.TryUpdateStatus

```csharp
public bool TryUpdateStatus(string orderId, OrderStatus currentStatus, OrderStatus newStatus)
{
    return _orderStatuses.TryUpdate(orderId, newStatus, currentStatus);
}
```

### OrderService.TryFulfillOrder

```csharp
public bool TryFulfillOrder(string orderId)
{
    // Try to transition from PENDING -> FULFILLED
    if (_orderRepository.TryUpdateStatus(orderId, OrderStatus.Pending, OrderStatus.Fulfilled))
    {
        return true;
    }
    
    // If that failed, try to transition from CONFIRMED -> FULFILLED
    if (_orderRepository.TryUpdateStatus(orderId, OrderStatus.Confirmed, OrderStatus.Fulfilled))
    {
        return true;
    }
    
    return false;
}
```

### WebhookController.cs (Core Section)

```csharp
[Route("stripe-webhook")]
public class WebhookController : ControllerBase
{
    private readonly string _webhookSecret;
    private readonly ILogger<WebhookController> _logger;
    private readonly OrderService _orderService;

    public WebhookController(
        ILogger<WebhookController> logger, 
        IConfiguration configuration,
        OrderService orderService)
    {
        _logger = logger;
        _webhookSecret = configuration["Stripe:WebhookSecret"];
        _orderService = orderService;
    }

    [HttpPost]
    public async Task<IActionResult> Index()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        var stripeEvent = EventUtility.ConstructEvent(
            json,
            Request.Headers["Stripe-Signature"],
            _webhookSecret
        );

        if (stripeEvent.Type == "checkout.session.completed")
        {
            var session = stripeEvent.Data.Object as Session;
            string internalOrderId = session.Metadata.GetValueOrDefault("internal_order_id", "Unknown");

            bool updateOrderSuccess = _orderService.TryFulfillOrder(internalOrderId);
            if (updateOrderSuccess)
            {
                _logger.LogInformation("Order {OrderId} fulfillment complete (END). Status: FULFILLED.", internalOrderId);
            }
            else
            {
                var currentStatus = _orderService.GetCurrentStatus(internalOrderId);
            
                if (currentStatus == OrderStatus.Fulfilled)
                {
                    _logger.LogInformation("Order {OrderId} already fulfilled. Idempotent webhook.", internalOrderId);
                }
                else
                {
                    _logger.LogError("CRITICAL: Order {OrderId} fulfillment failed. Current status: {Status}. This indicates a state machine or concurrency issue.",
                        internalOrderId, currentStatus);
                }
            }
        }

        return Ok();
    }
}
```

### Program.cs (Dependency Injection)

```csharp
// Register services and repositories
builder.Services.AddSingleton<OrderRepository>();
builder.Services.AddSingleton<ProductService>();
builder.Services.AddScoped<OrderService>();
```

---

## Next Steps / Extensions

- Replace `OrderRepository` in-memory storage with a transactional database (SQL or NoSQL).
- Implement retry logic and idempotency for webhook processing.
- Replace console logging with a production-ready logger (Serilog, Seq).
- Add customer notifications or fulfillment logic post-payment.
- Implement comprehensive error handling and monitoring.

---

## Production Notes / Limitations

**In-Memory Storage**: `OrderRepository` uses `ConcurrentDictionary` to simulate database persistence. In production, replace with Entity Framework Core, Dapper, or another data access layer backed by a real database.

**Singleton Lifetime**: `OrderRepository` is registered as Singleton because the in-memory dictionary must be shared across requests. With a real database, use Scoped lifetime to match DbContext lifecycle.

**Security**: Always verify webhook signatures and validate event sources.

**Idempotency**: Track processed Stripe Event IDs to prevent duplicate fulfillment in production systems.

**Error Handling**: Implement comprehensive logging, monitoring, and graceful failure handling for webhook processing.

For these reasons, this demo serves as an educational foundation rather than production-ready code.

---

## TL;DR

A complete, secure Stripe Checkout integration demo using ASP.NET Core.  
It showcases safe server-side price validation, atomic order state transitions, and asynchronous webhook handling — a proper foundation for real-world payment systems.
