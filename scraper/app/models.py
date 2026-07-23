"""Pydantic v2 request/response models for the scraper internal contract."""

from __future__ import annotations

from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field

from .config import MAX_REVIEWS_PER_IMPORT

Sort = Literal["newest", "mostRelevant"]


class ReviewsRequest(BaseModel):
    """Body of ``POST /internal/v1/google-play/reviews``."""

    model_config = ConfigDict(extra="forbid")

    packageId: str = Field(min_length=1)
    count: int = Field(ge=1, le=MAX_REVIEWS_PER_IMPORT)
    language: str = Field(min_length=2, max_length=2)
    country: str = Field(min_length=2, max_length=2)
    sort: Sort
    score: Annotated[int, Field(ge=1, le=5)] | None = None


class Review(BaseModel):
    """A single mapped review in the response."""

    externalId: str
    text: str
    author: str | None = None
    rating: Annotated[int, Field(ge=1, le=5)] | None = None
    thumbsUpCount: int = 0
    appVersion: str | None = None
    createdAt: str | None = None
    developerReply: str | None = None
    developerRepliedAt: str | None = None


class ReviewsResponse(BaseModel):
    """Response body of ``POST /internal/v1/google-play/reviews``."""

    packageId: str
    requestedCount: int
    returnedCount: int
    reviews: list[Review]


class AppMetadataResponse(BaseModel):
    """Small artwork contract used by the manager game picker."""

    packageId: str
    title: str | None = None
    iconUrl: str | None = None
