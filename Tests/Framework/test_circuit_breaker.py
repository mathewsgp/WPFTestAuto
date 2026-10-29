"""
Circuit Breaker Tests
=====================

Verifies circuit breaker behavior:
- Opens after N consecutive failures
- Half-open state after timeout
- Closes after successful probe in half-open
- Records successes and failures correctly

Run: pytest Tests/Framework/test_circuit_breaker.py -v
"""

import sys
import os
import time

# Add api directory to path
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "TestAutoLayer", "api"))

from circuit_breaker import CircuitBreaker, CircuitBreakerManager, CircuitState


class TestCircuitBreaker:
    """Unit tests for CircuitBreaker."""

    def test_breaker_starts_closed(self):
        """New circuit breaker should be in CLOSED state."""
        breaker = CircuitBreaker("test", threshold=3, timeout=30.0)
        assert breaker.state == CircuitState.CLOSED
        assert breaker.allow_request() is True

    def test_breaker_opens_after_threshold_failures(self):
        """Breaker should open after threshold consecutive failures."""
        breaker = CircuitBreaker("test", threshold=3, timeout=30.0)
        
        # Record 3 failures
        for i in range(3):
            breaker.record_failure()
        
        assert breaker.state == CircuitState.OPEN
        assert breaker.allow_request() is False

    def test_breaker_success_resets_failure_count(self):
        """Success should reset the failure count in CLOSED state."""
        breaker = CircuitBreaker("test", threshold=3, timeout=30.0)
        
        breaker.record_failure()
        breaker.record_failure()
        assert breaker.state == CircuitState.CLOSED  # Only 2 failures, below threshold
        
        breaker.record_success()
        assert breaker.failure_count == 0  # Reset on success

    def test_breaker_half_open_after_timeout(self):
        """Breaker should transition to HALF_OPEN after timeout in OPEN state."""
        breaker = CircuitBreaker("test", threshold=1, timeout=0.1)
        
        breaker.record_failure()
        assert breaker.state == CircuitState.OPEN
        assert breaker.allow_request() is False
        
        # Wait for timeout
        time.sleep(0.15)
        
        # Should now be HALF_OPEN and allow one request
        assert breaker.state == CircuitState.HALF_OPEN
        assert breaker.allow_request() is True

    def test_breaker_closes_after_success_in_half_open(self):
        """Breaker should close after successful probe in HALF_OPEN state."""
        breaker = CircuitBreaker("test", threshold=1, timeout=0.1)
        
        breaker.record_failure()
        time.sleep(0.15)
        assert breaker.state == CircuitState.HALF_OPEN
        
        # Successful probe closes the breaker
        breaker.record_success()
        assert breaker.state == CircuitState.CLOSED
        assert breaker.failure_count == 0

    def test_breaker_reopens_after_failure_in_half_open(self):
        """Breaker should reopen immediately on failure in HALF_OPEN state."""
        breaker = CircuitBreaker("test", threshold=1, timeout=0.1)
        
        breaker.record_failure()
        time.sleep(0.15)
        assert breaker.state == CircuitState.HALF_OPEN
        
        # Failed probe reopens the breaker
        breaker.record_failure()
        assert breaker.state == CircuitState.OPEN

    def test_manager_get_breaker_creates_new(self):
        """CircuitBreakerManager should create new breakers on demand."""
        manager = CircuitBreakerManager(threshold=3, timeout=30.0)
        
        b1 = manager.get_breaker("driver_a")
        b2 = manager.get_breaker("driver_b")
        b1_again = manager.get_breaker("driver_a")
        
        assert b1 is b1_again  # Same breaker for same name
        assert b1 is not b2    # Different breakers for different names

    def test_manager_reset_all(self):
        """Manager.reset_all() should reset all breakers to CLOSED."""
        manager = CircuitBreakerManager(threshold=3, timeout=30.0)
        
        b = manager.get_breaker("test", threshold=1, timeout=30.0)
        b.record_failure()
        assert b.state == CircuitState.OPEN
        
        manager.reset_all()
        assert b.state == CircuitState.CLOSED


if __name__ == "__main__":
    import pytest
    pytest.main([__file__, "-v"])
