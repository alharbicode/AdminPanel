using System.Net;
using System.Text.Json;
using AdminPanel.MVC.Models;
using AdminPanel.MVC.Services;
using Microsoft.AspNetCore.Mvc;

namespace AdminPanel.MVC.Controllers;

public abstract class ManagementController(ApiClient api) : Controller
{
    protected ApiClient Api { get; } = api;

    protected async Task<PagedList<T>> List<T>(string endpoint, AdminListQuery query)
    {
        var result = ModelState.IsValid
            ? await Api.GetAsync<PagedList<T>>(query.Url(endpoint + "/admin/list")) ?? new()
            : new PagedList<T>();
        result.Query = query;
        return result;
    }

    protected async Task AddApiErrors(HttpResponseMessage response, string fallback, string conflict)
    {
        // Display only validated field errors. Never expose arbitrary upstream/database diagnostics.
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            try
            {
                var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
                if (problem?.Errors.Count > 0)
                {
                    foreach (var field in problem.Errors)
                        foreach (var error in field.Value) ModelState.AddModelError(field.Key, error);
                    return;
                }
            }
            catch (JsonException) { }
        }
        ModelState.AddModelError("", response.StatusCode == HttpStatusCode.Conflict ? conflict : fallback);
    }

    protected IActionResult Saved(string message)
    {
        TempData["Success"] = message;
        return RedirectToAction("Index");
    }
}
