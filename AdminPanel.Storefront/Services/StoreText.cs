using System.Globalization;
using Microsoft.Extensions.Localization;
namespace AdminPanel.Storefront.Services;

public sealed class StoreOptions { public string Name { get; set; } = "Alharbi Store"; }
public sealed class StoreText(IStringLocalizer<StoreText> text)
{
    public bool Arabic => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
    public string Language => Arabic ? "ar" : "en";
    public string Direction => Arabic ? "rtl" : "ltr";
    public string this[string key] => text[key];
    public string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);
    // Retain existing fractional prices; do not silently round or convert catalog data.
    public string Price(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);
}
