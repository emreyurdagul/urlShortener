using Microsoft.AspNetCore.Mvc;

namespace Monolith;

/// <summary>
/// Presentation layer for link creation, listing, domains and QR codes. The
/// authenticated identity is read directly off HttpContext.Items (placed there by
/// <see cref="AuthMiddleware"/>) — no X-User-* header round-trip, because there is
/// no other process to hand it to.
/// </summary>
[ApiController]
public sealed class LinkController(LinkAppService links) : ControllerBase
{
    private UserIdentity? CurrentUser => HttpContext.Items[AuthMiddleware.ItemKey] as UserIdentity;

    // With no gateway in front, the request's own scheme/host is authoritative;
    // X-Forwarded-* is honored only when a real reverse proxy (Traefik/Cloudflare
    // in production) sits ahead of us.
    private string Scheme => Request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? Request.Scheme;

    [HttpGet("/api/domains")]
    public IActionResult Domains() =>
        Ok(new { domains = links.Domains.All, @default = links.Domains.Default });

    [HttpGet("/api/links")]
    public async Task<IActionResult> List()
    {
        if (CurrentUser is not { } user)
            return Unauthorized();

        var rows = await links.ListAsync(user.UserId);
        var result = rows.Select(r => new
        {
            r.Code,
            r.Domain,
            r.TargetUrl,
            r.CreatedAt,
            shortUrl = $"{Scheme}://{r.Domain}/{r.Code}",
            qrUrl = $"/api/links/{r.Code}/qr",
        });
        return Ok(new { links = result });
    }

    [HttpGet("/api/links/{code}/qr")]
    public async Task<IActionResult> Qr(string code)
    {
        var link = await links.FindForQrAsync(code);
        if (link.Domain is null)
            return NotFound();

        var shortUrl = $"{Scheme}://{link.Domain}/{link.Code}";
        return File(QrGenerator.Png(shortUrl), "image/png");
    }

    [HttpPost("/api/links")]
    public async Task<IActionResult> Create(CreateLinkRequest req)
    {
        var result = await links.CreateAsync(req, CurrentUser);
        if (!result.Ok)
            return StatusCode(result.StatusCode, new { error = result.Error });

        return Created($"/api/links/{result.Code}", new
        {
            code = result.Code,
            domain = result.Domain,
            shortUrl = $"{Scheme}://{result.Domain}/{result.Code}",
        });
    }
}
