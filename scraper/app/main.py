"""FastAPI application exposing the internal scraper contract."""

from __future__ import annotations

import logging

from fastapi import Depends, FastAPI, Header, HTTPException, status
from fastapi.responses import JSONResponse

from . import config
from .models import AppMetadataResponse, ReviewsRequest, ReviewsResponse
from .scraper import UpstreamScraperError, fetch_app_metadata, fetch_reviews

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s %(name)s %(message)s",
)

app = FastAPI(title="Google Play Reviews Scraper", version="1.0.0")


def require_internal_key(
    x_internal_service_key: str | None = Header(default=None),
) -> None:
    """Authenticate requests via the shared internal service key header."""
    expected = config.INTERNAL_SERVICE_KEY
    if not expected or x_internal_service_key != expected:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="unauthorized",
        )


@app.get("/health")
def health() -> dict[str, str]:
    """Liveness probe (no auth)."""
    return {"status": "ok"}


@app.post(
    "/internal/v1/google-play/reviews",
    response_model=ReviewsResponse,
    dependencies=[Depends(require_internal_key)],
)
def get_reviews(req: ReviewsRequest) -> ReviewsResponse:
    """Fetch mapped Google Play reviews for a package."""
    try:
        return fetch_reviews(req)
    except UpstreamScraperError:
        return JSONResponse(  # type: ignore[return-value]
            status_code=status.HTTP_502_BAD_GATEWAY,
            content={"error": "upstream_scraper_error"},
            media_type="application/problem+json",
        )


@app.get(
    "/internal/v1/google-play/apps/{package_id}",
    response_model=AppMetadataResponse,
    dependencies=[Depends(require_internal_key)],
)
def get_app_metadata(package_id: str) -> AppMetadataResponse:
    """Fetch the public app title and icon for manager-facing artwork."""
    try:
        return fetch_app_metadata(package_id)
    except UpstreamScraperError:
        return JSONResponse(  # type: ignore[return-value]
            status_code=status.HTTP_502_BAD_GATEWAY,
            content={"error": "upstream_scraper_error"},
            media_type="application/problem+json",
        )
