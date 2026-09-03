namespace Cloudstrap.Demo.Api.Controllers
{
    using Asp.Versioning;
    using Cloudstrap.Demo.Api.Data;
    using Cloudstrap.Demo.Contracts;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;
    using Wolverine.EntityFrameworkCore;

    /// <summary>
    /// The producer side of the messaging demo (deliverable #14): the HTTP-path outbox pattern, live. No
    /// <c>[Authorize]</c> attribute — the host-wide fallback policy demands the token.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/orders")]
    public sealed class OrdersController(IDbContextOutbox<DemoDbContext> outbox, DemoDbContext db) : ControllerBase
    {
        /// <summary>
        /// Places an order: stages the row, sends <see cref="PlaceOrderCommand"/> to the Worker through the
        /// transactional outbox, and commits both in one transaction — dispatch happens only after the
        /// commit, and a committed-but-undispatched command is recovered by the node (AC-MSG8).
        /// </summary>
        /// <param name="dto">The order to place.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns><c>202 Accepted</c> with the order id to poll.</returns>
        [HttpPost]
        public async Task<ActionResult<OrderAcceptedDto>> Place([FromBody] PlaceOrderDto dto, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(dto);

            Order order = new() { Id = Guid.NewGuid(), Description = dto.Description };

            // The three-line pattern: stage the entity, stage the message, save + flush as one unit.
            outbox.DbContext.Orders.Add(order);
            await outbox.SendAsync(new PlaceOrderCommand(order.Id));
            await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

            return Accepted($"/api/v1/orders/{order.Id}", new OrderAcceptedDto(order.Id));
        }

        /// <summary>
        /// The demo query endpoint: the order's status and, once the Worker processed it, the correlation id
        /// its handler observed — the cross-process proof of the flow.
        /// </summary>
        /// <param name="id">The order id.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The order, or <c>404</c>.</returns>
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            Order? order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
            return order is null
                ? NotFound()
                : Ok(new OrderDto(order.Id, order.Status, order.ProcessedCorrelationId));
        }
    }
}
