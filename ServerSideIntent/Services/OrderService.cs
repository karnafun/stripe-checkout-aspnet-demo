using ServerSideIntent.Contracts;
using ServerSideIntent.Repositories;

namespace ServerSideIntent.Services
{
    /// <summary>
    /// The dedicated service layer responsible for handling order business logic and state transitions.
    /// It consumes the MockDBRepository to perform atomic data operations.
    /// </summary>
    public class OrderService
    {
        private readonly OrderRepository _orderRepository;
        public OrderService(OrderRepository orderRepository)
        {
            _orderRepository = orderRepository;
        }
        /// <summary>
        /// Creates a new order entry in the repository with a PENDING status.
        /// </summary>
        /// <returns>The generated internal Order ID.</returns>
        public string CreatePendingOrder()
        {
            string internalOrderId = $"ORDER-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
            _orderRepository.CreateOrder(internalOrderId);
            return internalOrderId;
        }

        /// <summary>
        /// Attempts to update the order status from PENDING to CONFIRMED (client-side verification).
        /// </summary>
        public bool TryConfirmOrder(string orderId)
        {
            return _orderRepository.TryUpdateStatus(orderId, OrderStatus.Pending, OrderStatus.Confirmed);
        }

        /// <summary>
        /// Attempts to update the order status to FULFILLED, allowing transition from 
        /// either PENDING or CONFIRMED state (webhook guarantee).
        /// </summary>
        public bool TryFulfillOrder(string orderId)
        {
            // 1. Try to transition from PENDING -> FULFILLED
            if (_orderRepository.TryUpdateStatus(orderId, OrderStatus.Pending, OrderStatus.Fulfilled))
            {
                return true;
            }

            // 2. If 1 failed, try to transition from CONFIRMED -> FULFILLED
            if (_orderRepository.TryUpdateStatus(orderId, OrderStatus.Confirmed, OrderStatus.Fulfilled))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Retrieves the current status of an order from the repository.
        /// </summary>
        public OrderStatus GetCurrentStatus(string orderId)
        {
            return _orderRepository.GetStatus(orderId);
        }
    }
}
