namespace AdminPanel.API.Models;

public class ContentPage
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public string Slug { get; set; } = ""; // مثل about-us, privacy-policy (مفيد للعرض)

    public string Content { get; set; } = ""; // HTML أو نص عادي
}
