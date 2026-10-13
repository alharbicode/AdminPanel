// Images are progressively enhanced; browsing and language switching work without JS.
document.querySelectorAll("[data-product-image]").forEach(image => {
    const fallback = () => {
        if (!image.dataset.fallback) {
            image.dataset.fallback = "true";
            image.src = "/images/product-placeholder.svg";
        }
    };
    image.addEventListener("error", fallback);
    if (image.complete && image.naturalWidth === 0) fallback();
});

// Reset secrets travel only in the email URL fragment, never in HTTP URLs/referrers.
const resetForm = document.getElementById("password-reset-form");
if (resetForm && location.hash) {
    const fields = new URLSearchParams(location.hash.slice(1));
    const code = fields.get("code") || "";
    const email = fields.get("email") || "";
    if (/^[A-Za-z0-9_-]{43}$/.test(code)) resetForm.querySelector("[name=Token]").value = code;
    if (email.length <= 254) resetForm.querySelector("[name=Email]").value = email;
    history.replaceState(null, "", location.pathname + location.search);
}
