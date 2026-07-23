"""Fetching + mapping logic for Google Play reviews.

Uses the paginated ``reviews()`` function from ``google-play-scraper`` with a
continuation-token loop. Never leaks review text into logs or errors.
"""

from __future__ import annotations

import importlib
import logging
import random
import threading
import time
from datetime import datetime, timezone
from typing import Any
from urllib.request import urlopen as _stdlib_urlopen

from google_play_scraper import Sort, app as play_app, reviews
from google_play_scraper.utils import request as _google_request

from . import config
from .models import AppMetadataResponse, Review, ReviewsRequest, ReviewsResponse

logger = logging.getLogger("scraper")

# google-play-scraper 1.2.7 calls urllib without a timeout and silently converts
# transport exceptions into an empty page. Keep the dependency isolated behind
# this compatibility shim: apply a real socket timeout, let the import worker
# own retries, and remember hidden upstream errors so they become a sanitized
# 502 instead of a false successful import with zero reviews.
_reviews_module = importlib.import_module("google_play_scraper.features.reviews")
_original_fetch_review_items = _reviews_module._fetch_review_items
_upstream_call = threading.local()


def _urlopen_with_timeout(request: Any) -> Any:
    return _stdlib_urlopen(request, timeout=config.PAGE_TIMEOUT_SECONDS)


def _observed_fetch_review_items(*args: Any, **kwargs: Any) -> Any:
    try:
        return _original_fetch_review_items(*args, **kwargs)
    except Exception as exc:
        _upstream_call.error = exc
        raise


_google_request.urlopen = _urlopen_with_timeout
_google_request.MAX_RETRIES = 1
_reviews_module._fetch_review_items = _observed_fetch_review_items

_SORT_MAP: dict[str, Sort] = {
    "newest": Sort.NEWEST,
    "mostRelevant": Sort.MOST_RELEVANT,
}


class UpstreamScraperError(Exception):
    """Raised when the underlying scraper library fails.

    Carries no upstream detail so callers can surface a generic error.
    """


def fetch_app_metadata(package_id: str) -> AppMetadataResponse:
    """Fetch only the public title and icon needed by the game picker."""
    started = time.monotonic()
    try:
        raw = play_app(package_id, lang="en", country="us")
        title = raw.get("title")
        icon = raw.get("icon")
        result = AppMetadataResponse(
            packageId=package_id,
            title=title.strip() if isinstance(title, str) and title.strip() else None,
            iconUrl=icon.strip() if isinstance(icon, str) and icon.strip() else None,
        )
    except Exception:  # noqa: BLE001 - sanitize upstream failures
        logger.exception("app metadata upstream failure package=%s", package_id)
        raise UpstreamScraperError from None

    logger.info(
        "fetched app metadata package=%s has_icon=%s duration=%.3fs",
        package_id,
        result.iconUrl is not None,
        time.monotonic() - started,
    )
    return result


def _to_iso(value: Any) -> str | None:
    """Convert a library datetime to an ISO-8601 UTC string, or None."""
    if not isinstance(value, datetime):
        return None
    if value.tzinfo is None:
        value = value.replace(tzinfo=timezone.utc)
    else:
        value = value.astimezone(timezone.utc)
    return value.isoformat()


def map_review(raw: dict[str, Any]) -> Review | None:
    """Map a raw library review dict to a :class:`Review`.

    Returns ``None`` for reviews whose text is empty or whitespace-only so the
    caller can skip them.
    """
    text = raw.get("content")
    if not isinstance(text, str) or not text.strip():
        return None

    author = raw.get("userName")
    return Review(
        externalId=raw["reviewId"],
        text=text,
        author=author.strip() if isinstance(author, str) and author.strip() else None,
        rating=raw.get("score"),
        thumbsUpCount=raw.get("thumbsUpCount", 0) or 0,
        appVersion=raw.get("reviewCreatedVersion"),
        createdAt=_to_iso(raw.get("at")),
        developerReply=raw.get("replyContent"),
        developerRepliedAt=_to_iso(raw.get("repliedAt")),
    )


def _sleep_between_pages() -> None:
    """Sleep PAGE_DELAY_MS with a small random jitter."""
    base = config.PAGE_DELAY_MS / 1000.0
    jitter = random.uniform(0, base * 0.25)
    time.sleep(base + jitter)


def fetch_reviews(req: ReviewsRequest) -> ReviewsResponse:
    """Fetch up to ``req.count`` reviews for a package, paginating safely.

    Stops when the requested count is reached, a page comes back empty, or no
    continuation token is returned. Hard-capped at ``MAX_REVIEWS_PER_IMPORT``.
    """
    target = min(req.count, config.MAX_REVIEWS_PER_IMPORT)
    sort = _SORT_MAP[req.sort]

    mapped: list[Review] = []
    continuation_token: Any = None
    page_count = 0
    started = time.monotonic()

    try:
        while len(mapped) < target:
            remaining = target - len(mapped)
            page_size = min(remaining, config.MAX_PAGE_SIZE)

            _upstream_call.error = None
            result, continuation_token = reviews(
                req.packageId,
                lang=req.language,
                country=req.country,
                sort=sort,
                count=page_size,
                filter_score_with=req.score,
                continuation_token=continuation_token,
            )
            hidden_error = getattr(_upstream_call, "error", None)
            _upstream_call.error = None
            if hidden_error is not None:
                raise hidden_error
            page_count += 1

            if not result:
                break

            for raw in result:
                review = map_review(raw)
                if review is not None:
                    mapped.append(review)
                    if len(mapped) >= target:
                        break

            if continuation_token is None:
                break

            if len(mapped) < target:
                _sleep_between_pages()
    except Exception:  # noqa: BLE001 - sanitize any upstream failure
        duration = time.monotonic() - started
        logger.exception(
            "scraper upstream failure package=%s pages=%d duration=%.3fs",
            req.packageId,
            page_count,
            duration,
        )
        raise UpstreamScraperError from None

    duration = time.monotonic() - started
    logger.info(
        "fetched reviews package=%s pages=%d returned=%d requested=%d duration=%.3fs",
        req.packageId,
        page_count,
        len(mapped),
        req.count,
        duration,
    )

    return ReviewsResponse(
        packageId=req.packageId,
        requestedCount=req.count,
        returnedCount=len(mapped),
        reviews=mapped,
    )
