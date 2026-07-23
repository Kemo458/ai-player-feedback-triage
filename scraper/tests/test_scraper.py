"""Unit tests for mapping + pagination logic.

The ``google_play_scraper.reviews`` function is fully mocked; no test touches
the real network.
"""

from __future__ import annotations

from datetime import datetime, timezone
from typing import Any

import pytest
from google_play_scraper import Sort

from app import config, scraper
from app.models import ReviewsRequest


def make_raw(
    review_id: str,
    content: str = "great game",
    score: int | None = 5,
    **overrides: Any,
) -> dict[str, Any]:
    """Build a raw library-shaped review dict."""
    raw: dict[str, Any] = {
        "reviewId": review_id,
        "content": content,
        "score": score,
        "thumbsUpCount": 3,
        "reviewCreatedVersion": "1.2.3",
        "at": datetime(2026, 1, 2, 3, 4, 5),
        "replyContent": None,
        "repliedAt": None,
    }
    raw.update(overrides)
    return raw


class FakeReviews:
    """Callable mock for ``google_play_scraper.reviews``.

    Serves reviews from a list of pages. Records the ``count`` and ``sort``
    passed on each call so tests can assert page-size behavior.
    """

    def __init__(self, pages: list[list[dict[str, Any]]]) -> None:
        self._pages = pages
        self.calls: list[dict[str, Any]] = []

    def __call__(
        self,
        package_id: str,
        *,
        lang: str,
        country: str,
        sort: Sort,
        count: int,
        filter_score_with: int | None,
        continuation_token: Any,
    ) -> tuple[list[dict[str, Any]], Any]:
        index = 0 if continuation_token is None else continuation_token
        self.calls.append(
            {
                "package_id": package_id,
                "count": count,
                "sort": sort,
                "filter_score_with": filter_score_with,
                "index": index,
            }
        )
        if index >= len(self._pages):
            return [], None
        page = self._pages[index]
        next_token = index + 1 if index + 1 < len(self._pages) else None
        return page, next_token


@pytest.fixture(autouse=True)
def _no_sleep(monkeypatch: pytest.MonkeyPatch) -> None:
    """Disable inter-page delay for fast tests."""
    monkeypatch.setattr(config, "PAGE_DELAY_MS", 0)


def make_request(**overrides: Any) -> ReviewsRequest:
    defaults: dict[str, Any] = {
        "packageId": "com.example.game",
        "count": 10,
        "language": "en",
        "country": "us",
        "sort": "newest",
        "score": None,
    }
    defaults.update(overrides)
    return ReviewsRequest(**defaults)


# --- mapping -----------------------------------------------------------------


def test_map_review_full() -> None:
    raw = make_raw(
        "rid-1",
        content="Loved it",
        score=4,
        thumbsUpCount=7,
        reviewCreatedVersion="9.9.9",
        at=datetime(2026, 5, 6, 7, 8, 9, tzinfo=timezone.utc),
        replyContent="thanks!",
        repliedAt=datetime(2026, 5, 7, 0, 0, 0, tzinfo=timezone.utc),
    )
    review = scraper.map_review(raw)
    assert review is not None
    assert review.externalId == "rid-1"
    assert review.text == "Loved it"
    assert review.rating == 4
    assert review.thumbsUpCount == 7
    assert review.appVersion == "9.9.9"
    assert review.createdAt == "2026-05-06T07:08:09+00:00"
    assert review.developerReply == "thanks!"
    assert review.developerRepliedAt == "2026-05-07T00:00:00+00:00"


def test_map_review_defaults_and_none() -> None:
    raw = {
        "reviewId": "rid-2",
        "content": "ok",
        "score": None,
        # thumbsUpCount, reviewCreatedVersion, at, replyContent, repliedAt absent
    }
    review = scraper.map_review(raw)
    assert review is not None
    assert review.rating is None
    assert review.thumbsUpCount == 0
    assert review.appVersion is None
    assert review.createdAt is None
    assert review.developerReply is None
    assert review.developerRepliedAt is None


def test_naive_datetime_treated_as_utc() -> None:
    raw = make_raw("rid-3", at=datetime(2026, 1, 2, 3, 4, 5))
    review = scraper.map_review(raw)
    assert review is not None
    assert review.createdAt == "2026-01-02T03:04:05+00:00"


@pytest.mark.parametrize("bad", ["", "   ", "\n\t", None])
def test_empty_text_reviews_skipped_in_mapping(bad: Any) -> None:
    raw = make_raw("rid-x", content=bad)
    assert scraper.map_review(raw) is None


# --- sort mapping ------------------------------------------------------------


@pytest.mark.parametrize(
    ("sort_value", "expected"),
    [("newest", Sort.NEWEST), ("mostRelevant", Sort.MOST_RELEVANT)],
)
def test_sort_mapping(
    monkeypatch: pytest.MonkeyPatch, sort_value: str, expected: Sort
) -> None:
    fake = FakeReviews([[make_raw("r1")]])
    monkeypatch.setattr(scraper, "reviews", fake)
    scraper.fetch_reviews(make_request(count=1, sort=sort_value))
    assert fake.calls[0]["sort"] is expected


# --- pagination --------------------------------------------------------------


def test_page_size_never_exceeds_200(monkeypatch: pytest.MonkeyPatch) -> None:
    pages = [[make_raw(f"p{p}-{i}") for i in range(200)] for p in range(3)]
    fake = FakeReviews(pages)
    monkeypatch.setattr(scraper, "reviews", fake)

    resp = scraper.fetch_reviews(make_request(count=450))

    assert all(call["count"] <= 200 for call in fake.calls)
    # 200 + 200 + 50 requested across three pages
    assert [call["count"] for call in fake.calls] == [200, 200, 50]
    assert resp.returnedCount == 450


def test_count_cap_respected(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(config, "MAX_REVIEWS_PER_IMPORT", 3)
    pages = [[make_raw(f"r{i}") for i in range(50)]]
    fake = FakeReviews(pages)
    monkeypatch.setattr(scraper, "reviews", fake)

    resp = scraper.fetch_reviews(make_request(count=10))

    assert resp.returnedCount == 3
    assert resp.requestedCount == 10
    assert fake.calls[0]["count"] == 3


def test_stop_on_empty_page(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = FakeReviews([[]])  # first page empty
    monkeypatch.setattr(scraper, "reviews", fake)
    resp = scraper.fetch_reviews(make_request(count=100))
    assert resp.returnedCount == 0
    assert len(fake.calls) == 1


def test_stop_on_no_continuation_token(monkeypatch: pytest.MonkeyPatch) -> None:
    # single page (10 reviews) then continuation is None -> stop even though
    # count (100) not reached.
    fake = FakeReviews([[make_raw(f"r{i}") for i in range(10)]])
    monkeypatch.setattr(scraper, "reviews", fake)
    resp = scraper.fetch_reviews(make_request(count=100))
    assert resp.returnedCount == 10
    assert len(fake.calls) == 1


def test_empty_text_reviews_skipped_across_pages(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    page = [
        make_raw("keep-1", content="nice"),
        make_raw("skip-1", content="   "),
        make_raw("keep-2", content="cool"),
        make_raw("skip-2", content=""),
    ]
    fake = FakeReviews([page])
    monkeypatch.setattr(scraper, "reviews", fake)

    resp = scraper.fetch_reviews(make_request(count=100))

    assert resp.returnedCount == 2
    assert [r.externalId for r in resp.reviews] == ["keep-1", "keep-2"]


def test_upstream_error_sanitized(monkeypatch: pytest.MonkeyPatch) -> None:
    def boom(*args: Any, **kwargs: Any) -> Any:
        raise RuntimeError("internal library detail should not leak")

    monkeypatch.setattr(scraper, "reviews", boom)

    with pytest.raises(scraper.UpstreamScraperError) as exc_info:
        scraper.fetch_reviews(make_request(count=5))

    assert "library detail" not in str(exc_info.value)


def test_app_metadata_maps_title_and_icon(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(
        scraper,
        "play_app",
        lambda package_id, *, lang, country: {
            "title": " Example Game ",
            "icon": " https://cdn.example/icon.png ",
        },
    )

    metadata = scraper.fetch_app_metadata("com.example.game")

    assert metadata.packageId == "com.example.game"
    assert metadata.title == "Example Game"
    assert metadata.iconUrl == "https://cdn.example/icon.png"


def test_app_metadata_failure_is_sanitized(monkeypatch: pytest.MonkeyPatch) -> None:
    def boom(*args: Any, **kwargs: Any) -> Any:
        raise RuntimeError("private upstream detail")

    monkeypatch.setattr(scraper, "play_app", boom)

    with pytest.raises(scraper.UpstreamScraperError) as exc_info:
        scraper.fetch_app_metadata("com.example.game")

    assert "private upstream detail" not in str(exc_info.value)


def test_upstream_page_uses_configured_timeout(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    seen: dict[str, Any] = {}
    response = object()

    def fake_urlopen(request: Any, *, timeout: float) -> Any:
        seen["request"] = request
        seen["timeout"] = timeout
        return response

    monkeypatch.setattr(config, "PAGE_TIMEOUT_SECONDS", 7.5)
    monkeypatch.setattr(scraper, "_stdlib_urlopen", fake_urlopen)

    request = object()
    assert scraper._urlopen_with_timeout(request) is response
    assert seen == {"request": request, "timeout": 7.5}


def test_hidden_library_error_is_not_treated_as_empty_success(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def swallowed_error(*args: Any, **kwargs: Any) -> tuple[list[Any], None]:
        scraper._upstream_call.error = TimeoutError("private upstream detail")
        return [], None

    monkeypatch.setattr(scraper, "reviews", swallowed_error)

    with pytest.raises(scraper.UpstreamScraperError) as exc_info:
        scraper.fetch_reviews(make_request(count=5))

    assert "private upstream detail" not in str(exc_info.value)


def test_score_filter_forwarded(monkeypatch: pytest.MonkeyPatch) -> None:
    fake = FakeReviews([[make_raw("r1")]])
    monkeypatch.setattr(scraper, "reviews", fake)
    scraper.fetch_reviews(make_request(count=1, score=5))
    assert fake.calls[0]["filter_score_with"] == 5
