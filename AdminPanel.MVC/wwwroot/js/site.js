// Progressive enhancement: every management workflow also works without JavaScript.
(() => {
    const reset = () => {
        document.getElementById("request-progress").hidden = true;
        document.querySelectorAll("form[data-submitting]").forEach(form => {
            delete form.dataset.submitting;
            form.removeAttribute("aria-busy");
            form.querySelectorAll("[data-submit-disabled]").forEach(button => {
                button.disabled = false;
                button.removeAttribute("data-submit-disabled");
            });
        });
    };
    window.addEventListener("pageshow", reset);
    document.addEventListener("submit", event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || event.defaultPrevented) return;
        if (form.dataset.submitting) {
            event.preventDefault();
            return;
        }
        if (!form.checkValidity() || (window.jQuery?.validator && !window.jQuery(form).valid())) {
            event.preventDefault();
            form.reportValidity();
            return;
        }
        form.dataset.submitting = "true";
        form.setAttribute("aria-busy", "true");
        document.getElementById("request-progress-text").textContent =
            form.method.toLowerCase() === "get" ? "Loading results…" : "Saving changes…";
        document.getElementById("request-progress").hidden = false;
        // Defer disabling so the browser can include the clicked submitter in the request.
        window.setTimeout(() => {
            form.querySelectorAll("button[type=submit], input[type=submit]").forEach(button => {
                if (!button.disabled) {
                    button.setAttribute("data-submit-disabled", "");
                    button.disabled = true;
                }
            });
        }, 0);
    });
})();
