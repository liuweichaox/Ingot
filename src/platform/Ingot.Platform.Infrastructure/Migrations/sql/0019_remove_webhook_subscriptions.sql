-- Webhook delivery was removed from the product. Drop its durable cursor and
-- subscription configuration so upgraded databases match the active schema.
DROP TABLE IF EXISTS webhook_subscriptions;
