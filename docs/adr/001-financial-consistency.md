# ADR 001: Serialize financial mutations and preserve terminal outcomes

Status: Accepted for the reference slice.

A retried HTTP call or overlapping scheduler must not create duplicate invoices or debit attempts. Use deterministic SHA-256-derived IDs, unique customer/period and invoice/payment constraints, an advisory transaction lock and a single database transaction including audit writes. Export order is explicit. Replayed reconciliation is a no-op; conflicting terminal outcomes abort.

This provides database-local consistency, not exactly-once transmission to a bank. A future transport must add submission acknowledgments and an immutable attempt model. The coarse lock limits throughput; partitioning locks by tenant/period requires lock-order analysis. PostgreSQL is authoritative, not an in-memory demo substitute.
