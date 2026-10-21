using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Contracts.Categories;
using SupportDesk.Application.Features.Categories.Queries.GetCategories;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Authorize]
[Route("api/categories")]
[Produces("application/json")]
public sealed class CategoriesController(GetCategoriesQueryHandler getCategories) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CategoryListItemDto>> GetAll(CancellationToken ct) =>
        getCategories.HandleAsync(ct);
}
