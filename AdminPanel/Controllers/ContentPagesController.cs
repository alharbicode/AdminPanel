using AdminPanel.API.Data;
using AdminPanel.API.DTOs;
using AdminPanel.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminPanel.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ContentPagesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ContentPagesController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ContentPageDto>>> GetPages()
    {
        var pages = await _context.ContentPages
            .Select(p => new ContentPageDto
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                Content = p.Content
            }).ToListAsync();

        return Ok(pages);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<ContentPageDto>> GetBySlug(string slug)
    {
        var page = await _context.ContentPages.FirstOrDefaultAsync(p => p.Slug == slug);
        if (page == null) return NotFound();

        return new ContentPageDto
        {
            Id = page.Id,
            Title = page.Title,
            Slug = page.Slug,
            Content = page.Content
        };
    }

    [HttpPost]
    public async Task<IActionResult> Create(ContentPageDto dto)
    {
        var page = new ContentPage
        {
            Title = dto.Title,
            Slug = dto.Slug.ToLower().Replace(" ", "-"),
            Content = dto.Content
        };

        _context.ContentPages.Add(page);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Page created successfully" });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, ContentPageDto dto)
    {
        var page = await _context.ContentPages.FindAsync(id);
        if (page == null) return NotFound();

        page.Title = dto.Title;
        page.Slug = dto.Slug.ToLower().Replace(" ", "-");
        page.Content = dto.Content;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Page updated successfully" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var page = await _context.ContentPages.FindAsync(id);
        if (page == null) return NotFound();

        _context.ContentPages.Remove(page);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Page deleted successfully" });
    }
}
