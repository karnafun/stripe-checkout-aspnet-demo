using ServerSideIntent.Contracts;
using System.Collections.Concurrent;

namespace ServerSideIntent.Repositories
{
    public class OrderRepository
    {
        // Maps Order ID to its current status. ConcurrentDictionary is used to simulate thread safety.
        private readonly ConcurrentDictionary<string, OrderStatus> OrderStatuses = new();

        // Mocks initial order creation (called by CheckoutController)
        public void CreateOrder(string orderId)
        {
            OrderStatuses.TryAdd(orderId, OrderStatus.Pending);
        }

        /// <summary>
        /// Attempts an atomic state transition: Pending/Confirmed -> NewStatus.
        /// This prevents race conditions (e.g., trying to CONFIRM an already FULFILLED order).
        /// </summary>
        /// <param name="orderId">The ID of the order.</param>
        /// <param name="currentStatus">The expected status to change from (Pending or Confirmed).</param>
        /// <param name="newStatus">The desired new status (Confirmed or Fulfilled).</param>
        /// <returns>True if the transition succeeded; False if the order was already beyond the 'currentStatus'.</returns>
        public bool TryUpdateStatus(string orderId, OrderStatus currentStatus, OrderStatus newStatus)
        {
            // Try to update the status ONLY IF it is currently at the expected 'currentStatus'.
            return OrderStatuses.TryUpdate(orderId, newStatus, currentStatus);
        }

        /// <summary>
        /// Checks the current state of an order.
        /// </summary>
        public OrderStatus GetStatus(string orderId)
        {
            return OrderStatuses.GetValueOrDefault(orderId, OrderStatus.Failed);
        }
    }
}
