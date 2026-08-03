SELECT "AggregateId", "ErrorMessage", "AttemptCount", "DeadLetteredAt" FROM dead_letter_events ORDER BY "DeadLetteredAt" DESC LIMIT 5;
