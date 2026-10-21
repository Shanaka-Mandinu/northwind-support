using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Contracts.Customers;
using SupportDesk.Application.Features.Customers.Queries.GetCustomer;
using SupportDesk.Application.Features.Customers.Queries.GetCustomers;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Authorize]
[Route("api/customers")]
[Produces("application/json")]
public sealed class CustomersController : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CustomerListItemDto>> GetAll(
        [FromServices] GetCustomersQueryHandler handler,
        CancellationToken ct) =>
        handler.HandleAsync(ct);

    [HttpGet("{id:int}")]
    public Task<CustomerDetailDto> Get(int id, [FromServices] GetCustomerQueryHandler handler, CancellationToken ct) =>
        handler.HandleAsync(id, ct);
}
