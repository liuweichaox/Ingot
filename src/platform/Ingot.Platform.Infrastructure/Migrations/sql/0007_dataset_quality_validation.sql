DO $$
BEGIN
  IF to_regclass('public.scientific_validation_reports') IS NOT NULL
     AND to_regclass('public.dataset_quality_validation_reports') IS NULL THEN
    ALTER TABLE scientific_validation_reports
      RENAME TO dataset_quality_validation_reports;
  END IF;

  IF to_regclass('public.idx_scientific_validation_dataset') IS NOT NULL
     AND to_regclass('public.idx_dataset_quality_validation_dataset') IS NULL THEN
    ALTER INDEX idx_scientific_validation_dataset
      RENAME TO idx_dataset_quality_validation_dataset;
  END IF;
END
$$;
