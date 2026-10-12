# Admin Panel

A .NET solution containing an ASP.NET Core API and an MVC web client for an administration panel.

## Projects

- `AdminPanel/` — API project with controllers, DTOs, data access, models, services, and migrations
- `AdminPanel.MVC/` — MVC web client
- `AdminPanel.sln` — Visual Studio solution file

## Tech stack

- ASP.NET Core
- Entity Framework Core
- MVC
- Swagger / OpenAPI

## Run locally

Open `AdminPanel.sln` in Visual Studio, restore packages, configure the connection strings in each project's `appsettings.json`, and run the API and MVC projects. You can also run either project using `dotnet run --project <project-file>`.
