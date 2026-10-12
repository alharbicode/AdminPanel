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

// Small native product search: bounded results, explicit selection, no third-party widget.
document.querySelectorAll(".product-picker").forEach(picker => {
    const enhancement = picker.querySelector(".product-search");
    const term = picker.querySelector("#ProductSearch");
    const matches = picker.querySelector("#ProductMatches");
    const id = picker.querySelector("#ProductId");
    const selected = picker.querySelector(".product-selection");
    const status = picker.querySelector(".product-search-status");
    const search = picker.querySelector(".product-search-button");
    const previous = picker.querySelector(".product-previous");
    const next = picker.querySelector(".product-next");
    let page = 1, totalPages = 1, request;
    enhancement.hidden = false;

    const find = async requestedPage => {
        request?.abort();
        const current = new AbortController();
        request = current;
        enhancement.setAttribute("aria-busy", "true");
        search.disabled = previous.disabled = next.disabled = true;
        matches.disabled = true;
        status.textContent = "Searching products…";
        try {
            const url = new URL(picker.dataset.lookupUrl, window.location.origin);
            url.searchParams.set("Search", term.value);
            url.searchParams.set("Page", requestedPage);
            const response = await fetch(url, { signal: current.signal, headers: { Accept: "application/json" } });
            if (!response.ok || !response.headers.get("content-type")?.includes("application/json")) throw new Error("Search unavailable");
            const data = await response.json();
            if (current !== request) return;
            page = data.page;
            totalPages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));
            matches.replaceChildren(new Option("Choose a result", ""));
            for (const product of data.items) matches.add(new Option(product.name + " (#" + product.id + ")", product.id));
            status.textContent = data.totalCount ? data.totalCount + " matches. Page " + page + " of " + totalPages + "." : "No products found. Try another search.";
        } catch (error) {
            if (error.name !== "AbortError" && current === request) {
                matches.replaceChildren(new Option("Search unavailable", ""));
                status.textContent = "Could not search products. Retry or sign in again. You can also enter a known product ID.";
                totalPages = 1;
                page = 1;
            }
        } finally {
            if (current === request) {
                enhancement.removeAttribute("aria-busy");
                search.disabled = matches.disabled = false;
                previous.disabled = page <= 1;
                next.disabled = page >= totalPages;
            }
        }
    };
    search.addEventListener("click", () => find(1));
    term.addEventListener("keydown", event => {
        if (event.key === "Enter") { event.preventDefault(); find(1); }
    });
    previous.addEventListener("click", () => find(page - 1));
    next.addEventListener("click", () => find(page + 1));
    matches.addEventListener("change", () => {
        if (!matches.value) return;
        id.value = matches.value;
        selected.textContent = "Selected: " + matches.selectedOptions[0].textContent;
        id.dispatchEvent(new Event("change", { bubbles: true }));
    });
    id.addEventListener("input", () => { selected.textContent = ""; });
});

// Clear errors while correcting a previously invalid field. Waiting until focusout
// can move the submit button between pointerdown and pointerup and swallow the click.
document.addEventListener("input", event => {
    const field = event.target;
    if (!field.form || !window.jQuery?.validator) return;
    const validator = window.jQuery(field.form).data("validator");
    if (validator && (validator.invalid[field.name] || validator.submitted[field.name]))
        validator.element(field);
});
