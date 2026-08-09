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
    public async Task<IActionResult> List([FromQuery] int? page, [FromQuery] int? pageSize)
    {
        if (CurrentUser is not { } user)
            return Unauthorized();

        var p = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 20, 1, 100);
        var total = await links.CountAsync(user.UserId);
        var rows = await links.ListAsync(user.UserId, size, (p - 1) * size);
        var result = rows.Select(r => new
        {
            r.Code,
            r.Domain,
            r.TargetUrl,
            r.CreatedAt,
            shortUrl = $"{Scheme}://{r.Domain}/{r.Code}",
            qrUrl = $"/api/links/{r.Code}/qr",
        });
        return Ok(new { links = result, page = p, pageSize = size, total, hasMore = (long)p * size < total });
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
            return StatusCode(result.StatusCode, result.ErrorBody);

        return Created($"/api/links/{result.Code}", new
        {
            code = result.Code,
            domain = result.Domain,
            shortUrl = $"{Scheme}://{result.Domain}/{result.Code}",
        });
    }

    [HttpDelete("/api/links/{code}")]
    public async Task<IActionResult> Delete(string code, [FromQuery] string? domain)
    {
        if (CurrentUser is not { } user)
            return Unauthorized(new { code = "unauthenticated", error = "Sign in required." });

        var dom = (domain ?? Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.Value ?? "").ToLowerInvariant();
        return await links.DeleteAsync(dom, code, user.UserId)
            ? NoContent()
            : NotFound(new { code = "link_not_found", error = "Link not found." });
    }

    [HttpPut("/api/links/{code}")]
    public async Task<IActionResult> Update(string code, [FromQuery] string? domain, UpdateLinkRequest req)
    {
        if (CurrentUser is not { } user)
            return Unauthorized(new { code = "unauthenticated", error = "Sign in required." });
        if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https"))
            return BadRequest(new { code = "url_invalid", error = "URL must be an absolute http(s) URL." });
        if (!await LinkSafety.IsPublicAsync(target))
            return BadRequest(new { code = "url_unsafe", error = "That URL points to a private or unreachable host." });

        var dom = (domain ?? Request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? Request.Host.Value ?? "").ToLowerInvariant();
        return await links.UpdateAsync(dom, code, user.UserId, target.AbsoluteUri)
            ? Ok(new { code, domain = dom, targetUrl = target.AbsoluteUri })
            : NotFound(new { code = "link_not_found", error = "Link not found." });
    }
}
