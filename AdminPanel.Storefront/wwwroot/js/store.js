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
