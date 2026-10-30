-- Keep the newest review query ordered and bounded while applying site filters before the limit.
CREATE INDEX idx_inspection_reviews_recent
    ON inspection_reviews(reviewed_at DESC, review_id DESC)
    INCLUDE (inspection_record_id, execution_id);
