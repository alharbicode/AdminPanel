namespace AdminPanel.API.Services;

public class ProductImageStore(IWebHostEnvironment environment)
{
    public const long MaxBytes = 5 * 1024 * 1024;
    private string Folder => Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "images", "products");

    public async Task<string> SaveAsync(IFormFile file)
    {
        if (file.Length is <= 0 or > MaxBytes) throw new InvalidDataException("Image must be between 1 byte and 5 MiB.");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        await using var input = file.OpenReadStream();
        var header = new byte[12];
        var read = await input.ReadAtLeastAsync(header, 12, throwOnEndOfStream: false);
        var valid = extension switch
        {
            ".jpg" or ".jpeg" => read >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff,
            ".png" => read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".webp" => read == 12 && System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" &&
                       System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WEBP",
            _ => false
        };
        if (!valid) throw new InvalidDataException("Upload a JPEG, PNG or WebP image with a matching file signature.");
        Directory.CreateDirectory(Folder);
        var name = Guid.NewGuid().ToString("N") + extension;
        var url = "/images/products/" + name;
        try
        {
            await using var output = new FileStream(Path.Combine(Folder, name), FileMode.CreateNew);
            await output.WriteAsync(header.AsMemory(0, read));
            await input.CopyToAsync(output);
        }
        catch { Delete(url); throw; }
        return url;
    }

    public void Delete(string? url)
    {
        const string prefix = "/images/products/";
        if (url is null || !url.StartsWith(prefix, StringComparison.Ordinal)) return;
        var name = url[prefix.Length..];
        if (name != Path.GetFileName(name) || name.Contains('\\') || name.Contains('/')) return;
        File.Delete(Path.Combine(Folder, name));
    }
}
