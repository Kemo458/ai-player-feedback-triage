"""Environment-driven configuration constants."""

from __future__ import annotations

import os

# Hard cap on how many reviews a single import may fetch.
MAX_REVIEWS_PER_IMPORT: int = int(os.getenv("MAX_REVIEWS_PER_IMPORT", "500"))

# Delay between paginated requests, in milliseconds (jitter applied on top).
PAGE_DELAY_MS: int = int(os.getenv("PAGE_DELAY_MS", "500"))

# Hard timeout for each upstream Google Play page request.
PAGE_TIMEOUT_SECONDS: float = max(
    1.0, float(os.getenv("PAGE_TIMEOUT_SECONDS", "30"))
)

# Maximum page size accepted by the google-play-scraper paginated endpoint.
MAX_PAGE_SIZE: int = 200

# Shared-secret header value required to call internal endpoints.
INTERNAL_SERVICE_KEY: str | None = os.getenv("INTERNAL_SERVICE_KEY")
