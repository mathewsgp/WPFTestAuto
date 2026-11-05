"""
Retry Policy — Exponential Backoff with Jitter
===============================================

Provides a configurable retry policy for transient automation failures
(UIA/COM timeouts, pipe disconnects, etc.).

Usage:
    from TestAutoLayer.api.retry_policy import RetryPolicy, retry_with_backoff

    policy = RetryPolicy(max_attempts=3, base_delay=0.5, max_delay=10.0)
    result = retry_with_backoff(lambda: driver.find_element(locator), policy)
"""

from dataclasses import dataclass
from typing import Callable, TypeVar, Optional
import time
import random
import logging

logger = logging.getLogger(__name__)

T = TypeVar("T")


@dataclass
class RetryPolicy:
    """Configuration for exponential backoff with jitter retry.

    Attributes:
        max_attempts: Maximum number of attempts (including the first).
        base_delay: Initial delay between retries in seconds.
        max_delay: Maximum delay cap in seconds.
        exponential_base: Base for exponential backoff calculation.
        jitter: Random jitter factor (0.0 = no jitter, 1.0 = full jitter).
    """
    max_attempts: int = 3
    base_delay: float = 0.5
    max_delay: float = 30.0
    exponential_base: float = 2.0
    jitter: float = 0.1

    def delay_for_attempt(self, attempt: int) -> float:
        """Compute the delay before the given attempt number (1-indexed).

        Formula: min(base_delay * base^(attempt-1), max_delay) * jitter_factor
        where jitter_factor is in [1 - jitter, 1 + jitter].
        """
        delay = self.base_delay * (self.exponential_base ** (attempt - 1))
        delay = min(delay, self.max_delay)
        if self.jitter > 0:
            jitter_factor = 1.0 + random.uniform(-self.jitter, self.jitter)
            delay *= jitter_factor
        return max(delay, 0.0)


def retry_with_backoff(
    func: Callable[[], T],
    policy: RetryPolicy,
    retryable_exceptions: tuple = (Exception,),
) -> T:
    """Execute `func` with exponential backoff retry.

    Args:
        func: No-argument callable to execute.
        policy: RetryPolicy configuration.
        retryable_exceptions: Tuple of exception types that trigger a retry.

    Returns:
        The result of `func` on success.

    Raises:
        The last exception raised by `func` if all attempts are exhausted.
    """
    last_exception: Optional[Exception] = None

    for attempt in range(1, policy.max_attempts + 1):
        try:
            return func()
        except retryable_exceptions as e:
            last_exception = e
            if attempt < policy.max_attempts:
                delay = policy.delay_for_attempt(attempt)
                logger.debug(
                    f"Attempt {attempt} failed, retrying in {delay:.2f}s: {e}"
                )
                time.sleep(delay)
            else:
                logger.debug(
                    f"All {policy.max_attempts} attempts exhausted"
                )

    # All attempts exhausted — raise the last exception
    assert last_exception is not None
    raise last_exception


def default_retry_policy() -> RetryPolicy:
    """Return a sensible default retry policy for driver operations."""
    return RetryPolicy(
        max_attempts=3,
        base_delay=0.5,
        max_delay=10.0,
        exponential_base=2.0,
        jitter=0.1,
    )
