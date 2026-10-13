using System.Security.Claims;
using AdminPanel.API.DTOs;
using AdminPanel.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AdminPanel.API.Controllers;

[ApiController, AllowAnonymous, Route("api/checkout")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CheckoutController(CheckoutService checkout) : ControllerBase
{
    [HttpGet("destinations")]
    public async Task<IActionResult> Destinations(CancellationToken ct) => Ok(await checkout.Destinations(ct));
}

[ApiController, AllowAnonymous, Route("api/guest/checkout"), EnableRateLimiting("checkout")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GuestCheckoutController(CheckoutService checkout) : ControllerBase
{
    private string? Token => Request.Headers["X-Guest-Cart"].FirstOrDefault();
    [HttpPost("quote")]
    public async Task<IActionResult> Quote(CheckoutQuoteDto dto, CancellationToken ct) => Ok(await checkout.Quote(null, Token, dto, ct));
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(CheckoutConfirmDto dto, CancellationToken ct) => Ok(await checkout.Confirm(null, Token, dto, ct));
}

[ApiController, Route("api/customer/checkout"), EnableRateLimiting("checkout")]
[Authorize(AuthenticationSchemes = CustomerSecurity.Scheme, Roles = "Customer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerCheckoutController(CheckoutService checkout) : ControllerBase
{
    private int Id => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpPost("quote")]
    public async Task<IActionResult> Quote(CheckoutQuoteDto dto, CancellationToken ct) => Ok(await checkout.Quote(Id, null, dto, ct));
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(CheckoutConfirmDto dto, CancellationToken ct) => Ok(await checkout.Confirm(Id, null, dto, ct));
}

[ApiController, Route("api/customer/orders")]
[Authorize(AuthenticationSchemes = CustomerSecurity.Scheme, Roles = "Customer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerOrdersController(CheckoutService checkout, OrderLifecycle lifecycle) : ControllerBase
{
    private int Id => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> History([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await checkout.History(Id, pageNumber, pageSize, ct));
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct) => Ok(CheckoutService.View(await checkout.CustomerOrder(Id, id, ct)));
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, OrderActionDto dto, CancellationToken ct)
    {
        var order = await checkout.CustomerOrder(Id, id, ct);
        await lifecycle.Cancel(order, dto.Version, $"customer:{Id}", dto.Reason, false, ct);
        return Ok(CheckoutService.View(order));
    }
    [HttpPost("{id:int}/cancellation-request")]
    public async Task<IActionResult> RequestCancellation(int id, OrderActionDto dto, CancellationToken ct)
    {
        var order = await checkout.CustomerOrder(Id, id, ct);
        await lifecycle.RequestCancellation(order, dto.Version, dto.Reason, ct);
        return Ok(CheckoutService.View(order));
    }
}

[ApiController, AllowAnonymous, Route("api/guest/orders"), EnableRateLimiting("guest-orders")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GuestOrdersController(CheckoutService checkout, OrderLifecycle lifecycle) : ControllerBase
{
    private string? Token => Request.Headers["X-Guest-Order"].FirstOrDefault();
    [HttpPost("lookup")]
    public async Task<IActionResult> Lookup(CancellationToken ct) => Ok(CheckoutService.View(await checkout.GuestOrder(Token, ct)));
    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(OrderActionDto dto, CancellationToken ct)
    {
        var order = await checkout.GuestOrder(Token, ct);
        await lifecycle.Cancel(order, dto.Version, "guest", dto.Reason, false, ct);
        return Ok(CheckoutService.View(order));
    }
    [HttpPost("cancellation-request")]
    public async Task<IActionResult> RequestCancellation(OrderActionDto dto, CancellationToken ct)
    {
        var order = await checkout.GuestOrder(Token, ct);
        await lifecycle.RequestCancellation(order, dto.Version, dto.Reason, ct);
        return Ok(CheckoutService.View(order));
    }
}
