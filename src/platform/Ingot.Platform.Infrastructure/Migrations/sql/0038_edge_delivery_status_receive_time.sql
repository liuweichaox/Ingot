-- Optional Delivery reports need their own receive time so later heartbeats cannot refresh old queue evidence.
ALTER TABLE public.platform_edges
    ADD COLUMN delivery_status_received_at TIMESTAMPTZ NULL;
