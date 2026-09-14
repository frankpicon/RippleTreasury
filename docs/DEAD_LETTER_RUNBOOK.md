# Dead-letter investigation and replay runbook

MassTransit moves a consumer message to its receive endpoint's `_error` queue after the configured bounded
retries are exhausted. Treat every message in an error queue as a production incident requiring diagnosis
before replay.

## Detection

Monitor these signals in RabbitMQ or the production monitoring platform:

- Message count for queues ending in `_error`
- Age of the oldest error message
- Rate of messages entering error queues
- Consumer retry and failure rate
- Normal queue backlog
- Transactional-outbox age

Recommended initial alert: an `_error` queue remains nonempty for five minutes. Adjust the interval to the
service-level objective and expected operator response.

## Investigation

1. Open RabbitMQ Management and locate the affected `*_error` queue.
2. Inspect one message without acknowledging or deleting it.
3. Record the endpoint, message type, message ID, correlation ID, exception type, and failure timestamp.
4. Search Seq for `RetryExhausted = true` and the correlation ID.
5. Use the same correlation ID in Jaeger to inspect the originating HTTP and message flow.
6. Classify the cause:
   - transient infrastructure failure;
   - configuration or credential problem;
   - application defect;
   - invalid or incompatible message;
   - persistent data constraint.
7. Determine whether any business database transaction committed before the consumer failed.
8. Correct and deploy the underlying fix before replaying the message.

Never copy customer data or credentials into an incident ticket unless policy explicitly permits it.

## Controlled replay

Do not automatically loop messages from `_error` back to their source queue.

Before replay:

- Confirm the failure cause is fixed.
- Preserve the original message body, message type, message ID, correlation ID, and relevant headers.
- Confirm the destination endpoint and contract version.
- Check whether the consumer inbox already recorded the message.
- Record who authorized the replay and why.

Replay a small sample first. Verify its business effect in the owning service database or API, then replay the
remaining messages in controlled batches. Monitor the source queue, error queue, retry rate, logs, traces, and
projection state throughout the operation.

## Verification

A replay is complete only when:

- The source consumer processes the message successfully.
- The message does not return to `_error`.
- The expected projection or notification state is present.
- No duplicate business effect occurred.
- Queue depth and outbox age return to normal.
- The incident record contains the root cause and corrective action.

## Local demonstration

1. Start the Compose environment.
2. Open RabbitMQ Management at <http://localhost:15672>.
3. Sign in with `ticketing` / `ticketing_password`.
4. Select **Queues and Streams** and search for `_error`.
5. Search Seq at <http://localhost:5341> for `RetryExhausted = true`.
6. Use the logged correlation ID to follow the request and message path.

The integration test `Permanently_failing_consumer_moves_message_to_error_queue` proves the broker routing
behavior against a real RabbitMQ container.
