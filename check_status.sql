-- DLQ en PostgreSQL
SELECT 'DLQ' as source, "AggregateId", "ErrorMessage", "AttemptCount", "DeadLetteredAt" FROM dead_letter_events ORDER BY "DeadLetteredAt" DESC LIMIT 10;
