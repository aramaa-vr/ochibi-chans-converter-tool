(() => {
  // Add native lazy-loading + async decoding to docs images.
  // Keep the very first image eager to avoid delaying above-the-fold visuals.
  const imgs = document.querySelectorAll(".main-content img");
  imgs.forEach((img, i) => {
    // NOTE: img.loading defaults to "auto" even when no attribute is set.
    // So we check the attribute instead of the property.
    if (!img.hasAttribute("loading")) {
      img.setAttribute("loading", i === 0 ? "eager" : "lazy");
    }

    if (!img.hasAttribute("decoding")) {
      img.setAttribute("decoding", "async");
    }

    // Privacy-first default for future hotlinked images.
    // (Doesn't affect local assets.)
    if (!img.hasAttribute("referrerpolicy")) {
      img.setAttribute("referrerpolicy", "no-referrer");
    }
  });

  // Make long tables easier to handle on mobile (scroll hint).
  const tables = document.querySelectorAll(".main-content table");
  tables.forEach((t) => {
    t.setAttribute("role", "table");
  });
})();
