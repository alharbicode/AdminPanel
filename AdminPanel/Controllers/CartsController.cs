using System.Security.Claims;
using AdminPanel.API.DTOs;
using AdminPanel.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace AdminPanel.API.Controllers;

[ApiController, AllowAnonymous, Route("api/guest/cart")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GuestCartController(CartService carts) : ControllerBase
{
    private string? Token => Request.Headers["X-Guest-Cart"].FirstOrDefault();
    [HttpPost, EnableRateLimiting("guest-cart")]
    public async Task<IActionResult> Create(CancellationToken ct) => Ok(await carts.CreateGuest(ct));
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await carts.Read(await carts.Guest(Token, false, ct), ct));
    [HttpPost("items/{id:int}")]
    public async Task<IActionResult> Add(int id, CartMutationDto dto, CancellationToken ct) => Ok(await carts.Mutate(await carts.Guest(Token, false, ct), id, dto, "add", ct));
    [HttpPut("items/{id:int}")]
    public async Task<IActionResult> Set(int id, CartMutationDto dto, CancellationToken ct) => Ok(await carts.Mutate(await carts.Guest(Token, false, ct), id, dto, "set", ct));
    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> Remove(int id, CartMutationDto dto, CancellationToken ct) => Ok(await carts.Mutate(await carts.Guest(Token, false, ct), id, dto, "remove", ct));
}
[ApiController, Route("api/customer/cart")]
[Authorize(AuthenticationSchemes = CustomerSecurity.Scheme, Roles = "Customer")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerCartController(CartService carts) : ControllerBase
{
    private int Id => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await carts.Read(await carts.Customer(Id, ct), ct));
    [HttpPost("items/{id:int}")]
    public async Task<IActionResult> Add(int id, CartMutationDto dto, CancellationToken ct) => Ok(await carts.Mutate(await carts.Customer(Id, ct), id, dto, "add", ct));
    [HttpPut("items/{id:int}")]
    public async Task<IActionResult> Set(int id, CartMutationDto dto, CancellationToken ct) => Ok(await carts.Mutate(await carts.Customer(Id, ct), id, dto, "set", ct));
    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> Remove(int id, CartMutationDto dto, CancellationToken ct) => Ok(await carts.Mutate(await carts.Customer(Id, ct), id, dto, "remove", ct));
    [HttpPost("merge")]
    public async Task<IActionResult> Merge(MergeCartDto dto, CancellationToken ct) => Ok(await carts.Merge(Id, dto.GuestToken, ct));
}

