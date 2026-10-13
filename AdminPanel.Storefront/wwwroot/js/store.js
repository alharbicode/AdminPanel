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

// City selection is an enhancement; the complete form also works without JavaScript.
const destination = document.querySelector("[data-destination]");
if (destination) {
    const city = document.getElementById("City");
    destination.addEventListener("change", () => {
        const selectedCity = destination.selectedOptions[0]?.dataset.city;
        if (selectedCity && city) city.value = selectedCity;
    });
}

// The API key, rather than this UI guard, makes repeated confirmation safe.
document.querySelectorAll("[data-checkout-form], [data-checkout-confirm]").forEach(form => {
    form.addEventListener("submit", () => {
        const button = form.querySelector("[data-busy-text]");
        if (button) { button.disabled = true; button.textContent = button.dataset.busyText; }
        form.setAttribute("aria-busy", "true");
    });
});
window.addEventListener("pageshow", () => {
    document.querySelectorAll("form[aria-busy=true]").forEach(form => {
        form.removeAttribute("aria-busy");
        const button = form.querySelector("[data-busy-text]");
        if (button) { button.disabled = false; button.textContent = button.dataset.originalText; }
    });
});
document.querySelectorAll("[data-busy-text]").forEach(button => { button.dataset.originalText = button.textContent; });
const copyTracking = document.querySelector("[data-copy-tracking]");
if (copyTracking) {
    copyTracking.hidden = false;
    copyTracking.addEventListener("click", async () => {
        const field = document.getElementById("saved-tracking-code");
        field.select();
        try {
            await navigator.clipboard.writeText(field.value);
            document.querySelector("[data-copy-status]").textContent = copyTracking.dataset.copied;
        } catch { field.focus(); } // The selected code can always be copied manually.
    });
}
