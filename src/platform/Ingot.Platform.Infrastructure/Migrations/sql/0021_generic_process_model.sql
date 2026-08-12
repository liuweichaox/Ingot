-- Move persisted data to the generic process-execution vocabulary.
-- Earlier migrations are immutable; all table, column and JSON contract changes live here.

CREATE FUNCTION pg_temp.ingot_rename_process_json(value JSONB)
RETURNS JSONB
LANGUAGE plpgsql
IMMUTABLE
AS $migration$
DECLARE
  result JSONB;
  text_value TEXT;
BEGIN
  CASE jsonb_typeof(value)
    WHEN 'object' THEN
      SELECT COALESCE(jsonb_object_agg(
        CASE key
          WHEN 'moldId' THEN 'toolingAssemblyId'
          WHEN 'machineId' THEN 'equipmentId'
          WHEN 'productSeries' THEN 'productFamilyCode'
          WHEN 'recipeId' THEN 'processSpecificationId'
          WHEN 'recipeVersion' THEN 'processSpecificationVersion'
          WHEN 'recipeTemplate' THEN 'processTemplate'
          WHEN 'recipeStep' THEN 'processStep'
          WHEN 'recipeStepName' THEN 'processStepName'
          WHEN 'workpieceId' THEN 'outputItemId'
          WHEN 'operationRunId' THEN 'executionId'
          WHEN 'correlationId' THEN 'executionId'
          WHEN 'correlationIds' THEN 'executionIds'
          WHEN 'sourceCycleNo' THEN 'sourceExecutionNo'
          WHEN 'cycleCount' THEN 'processExecutionCount'
          WHEN 'totalCycles' THEN 'totalProcessExecutions'
          WHEN 'processedCycles' THEN 'processedProcessExecutions'
          WHEN 'materializedCycles' THEN 'materializedProcessExecutions'
          WHEN 'failedCycles' THEN 'failedProcessExecutions'
          WHEN 'lastCorrelationId' THEN 'lastExecutionId'
          WHEN 'runKey' THEN 'executionKey'
          WHEN 'suggestionRunKey' THEN 'suggestionExecutionKey'
          WHEN 'actualRunKey' THEN 'actualExecutionKey'
          WHEN 'windowId' THEN 'operatingRegionId'
          WHEN 'sourceWindowId' THEN 'sourceOperatingRegionId'
          WHEN 'processWindow' THEN 'operatingRegion'
          WHEN 'recipeParameters' THEN 'controlParameters'
          WHEN 'sourceField' THEN 'displayName'
          WHEN 'recipe' THEN 'processSpecification'
          WHEN 'profileId' THEN 'taskId'
          WHEN 'acquisitionProfiles' THEN 'ingestionTasks'
          WHEN 'acquisitionProfileId' THEN 'ingestionTaskId'
          WHEN 'acquisitionProfileVersion' THEN 'ingestionTaskVersion'
          WHEN 'connection' THEN 'httpPolling'
          WHEN 'cleanSession' THEN 'resetSessionOnConnect'
          WHEN 'mold_id' THEN 'tooling_assembly_id'
          WHEN 'tooling_id' THEN 'tooling_assembly_id'
          WHEN 'machine_id' THEN 'equipment_id'
          WHEN 'product_series' THEN 'product_family_code'
          WHEN 'recipe_id' THEN 'process_specification_id'
          WHEN 'recipe_version' THEN 'process_specification_version'
          WHEN 'recipe_data_model_id' THEN 'process_specification_data_model_id'
          WHEN 'recipe_data_model_version' THEN 'process_specification_data_model_version'
          WHEN 'recipe_snapshot_status' THEN 'process_specification_snapshot_status'
          WHEN 'recipe_step' THEN 'process_step'
          WHEN 'workpiece_id' THEN 'output_item_id'
          WHEN 'operation_run_id' THEN 'execution_id'
          WHEN 'correlation_id' THEN 'execution_id'
          WHEN 'source_cycle_no' THEN 'source_execution_no'
          WHEN 'acquisition_profile_id' THEN 'ingestion_task_id'
          WHEN 'acquisition_profile_version' THEN 'ingestion_task_version'
          ELSE key
        END,
        pg_temp.ingot_rename_process_json(nested_value)), '{}'::jsonb)
      INTO result
      FROM jsonb_each(value) AS item(key, nested_value);
      RETURN result;
    WHEN 'array' THEN
      SELECT COALESCE(jsonb_agg(pg_temp.ingot_rename_process_json(nested_value)), '[]'::jsonb)
      INTO result
      FROM jsonb_array_elements(value) AS item(nested_value);
      RETURN result;
    WHEN 'string' THEN
      text_value := value #>> '{}';
      text_value := CASE text_value
        WHEN 'production-cycle' THEN 'production-execution'
        WHEN 'discrete-cycle' THEN 'discrete'
        WHEN 'cycle.started' THEN 'process.execution.started'
        WHEN 'cycle.completed' THEN 'process.execution.completed'
        WHEN 'recipe.applied' THEN 'process.specification.applied'
        WHEN 'cycle' THEN 'execution'
        WHEN 'product_series' THEN 'product_family_code'
        WHEN 'machine_id' THEN 'equipment_id'
        WHEN 'mold_id' THEN 'tooling_assembly_id'
        WHEN 'tooling_id' THEN 'tooling_assembly_id'
        WHEN 'recipe_id' THEN 'process_specification_id'
        WHEN 'recipe_version' THEN 'process_specification_version'
        WHEN 'recipe_step' THEN 'process_step'
        WHEN 'workpiece_id' THEN 'output_item_id'
        WHEN 'operation_run_id' THEN 'execution_id'
        WHEN 'correlation_id' THEN 'execution_id'
        WHEN 'source_cycle_no' THEN 'source_execution_no'
        WHEN 'process-window' THEN 'operating-region'
        ELSE text_value
      END;
      IF text_value LIKE 'recipe:%' THEN
        text_value := 'control-parameter:' || substr(text_value, length('recipe:') + 1);
      END IF;
      RETURN to_jsonb(text_value);
    ELSE
      RETURN value;
  END CASE;
END
$migration$;

UPDATE tooling_assemblies
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE tooling_assembly_revisions
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE tooling_installations
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE production_contexts
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE process_data_models
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE recipe_versions
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE process_analysis_plans
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE inspection_plans
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE phase_mappings
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE feature_definitions
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE cycle_analysis_materializations
SET result = pg_temp.ingot_rename_process_json(result);
UPDATE cycle_analysis_backfill_jobs
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE operation_context_snapshots
SET context = pg_temp.ingot_rename_process_json(context);
UPDATE production_events
SET context = pg_temp.ingot_rename_process_json(context),
    data = pg_temp.ingot_rename_process_json(data),
    event_type = CASE event_type
      WHEN 'cycle.started' THEN 'process.execution.started'
      WHEN 'cycle.completed' THEN 'process.execution.completed'
      WHEN 'recipe.applied' THEN 'process.specification.applied'
      ELSE event_type
    END;
UPDATE operation_context_snapshots
SET started_event_type = CASE started_event_type
  WHEN 'cycle.started' THEN 'process.execution.started'
  ELSE started_event_type
END;
UPDATE data_object_summaries
SET latest_event_type = CASE latest_event_type
  WHEN 'cycle.started' THEN 'process.execution.started'
  WHEN 'cycle.completed' THEN 'process.execution.completed'
  WHEN 'recipe.applied' THEN 'process.specification.applied'
  ELSE latest_event_type
END;
UPDATE cycle_features
SET phase_source = 'execution'
WHERE phase_source = 'cycle';
UPDATE acquisition_profiles
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE scenario_packages
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE process_research_projects
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE research_hypotheses
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE research_experiments
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE research_experiment_runs
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE research_experiment_results
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE research_process_windows
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE research_knowledge_claims
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE research_shadow_recommendations
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE research_historical_replay_reports
SET payload = pg_temp.ingot_rename_process_json(payload);
UPDATE research_transfer_assessments
SET payload = pg_temp.ingot_rename_process_json(payload);

ALTER TABLE research_experiment_runs
  RENAME COLUMN run_key TO execution_key;

ALTER TABLE research_process_windows
  RENAME TO research_operating_regions;
ALTER TABLE research_operating_regions
  RENAME COLUMN window_id TO operating_region_id;
ALTER TABLE research_operating_regions
  RENAME CONSTRAINT research_process_windows_pkey
  TO research_operating_regions_pkey;
ALTER TABLE research_operating_regions
  RENAME CONSTRAINT research_process_windows_project_id_fkey
  TO research_operating_regions_project_id_fkey;
ALTER TABLE research_operating_regions
  RENAME CONSTRAINT research_process_windows_status_check
  TO research_operating_regions_status_check;
ALTER INDEX idx_research_process_windows_project
  RENAME TO idx_research_operating_regions_project;

ALTER TABLE research_window_results
  RENAME TO research_operating_region_results;
ALTER TABLE research_operating_region_results
  RENAME COLUMN window_id TO operating_region_id;
ALTER TABLE research_operating_region_results
  RENAME CONSTRAINT research_window_results_pkey
  TO research_operating_region_results_pkey;
ALTER TABLE research_operating_region_results
  RENAME CONSTRAINT research_window_results_window_id_fkey
  TO research_operating_region_results_region_id_fkey;
ALTER TABLE research_operating_region_results
  RENAME CONSTRAINT research_window_results_result_id_fkey
  TO research_operating_region_results_result_id_fkey;
ALTER INDEX idx_research_window_results_result
  RENAME TO idx_research_operating_region_results_result;

ALTER TABLE research_evidence
  DROP CONSTRAINT research_evidence_kind_check;
UPDATE research_evidence
SET kind = 'operating-region'
WHERE kind = 'process-window';
ALTER TABLE research_evidence
  ADD CONSTRAINT research_evidence_kind_check
  CHECK (kind IN (
    'dataset-snapshot',
    'experiment-result',
    'analysis-run',
    'mechanism-model',
    'knowledge-source',
    'operating-region'));

ALTER TABLE research_shadow_recommendations
  RENAME COLUMN suggestion_run_key TO suggestion_execution_key;
ALTER TABLE research_shadow_recommendations
  RENAME COLUMN actual_run_key TO actual_execution_key;
ALTER TABLE research_shadow_recommendations
  RENAME CONSTRAINT research_shadow_recommendatio_experiment_id_suggestion_run__key
  TO research_shadow_recommendations_experiment_suggestion_execution_key_key;
ALTER TABLE research_shadow_recommendations
  RENAME CONSTRAINT research_shadow_recommendations_project_id_actual_run_key_key
  TO research_shadow_recommendations_project_actual_execution_key_key;

ALTER TABLE research_transfer_assessments
  RENAME COLUMN source_window_id TO source_operating_region_id;
ALTER TABLE research_transfer_assessments
  RENAME CONSTRAINT research_transfer_assessments_project_id_source_window_id_r_key
  TO research_transfer_assessments_project_source_region_record_key;
ALTER TABLE research_transfer_assessments
  RENAME CONSTRAINT research_transfer_assessments_source_window_id_fkey
  TO research_transfer_assessments_source_operating_region_id_fkey;

ALTER TABLE tooling_components
  DROP COLUMN tooling_type_code,
  DROP COLUMN role_code;

ALTER TABLE tooling_assemblies
  RENAME COLUMN mold_id TO tooling_assembly_id;
ALTER TABLE tooling_assembly_revisions
  RENAME COLUMN mold_id TO tooling_assembly_id;
ALTER TABLE tooling_assembly_revisions
  RENAME CONSTRAINT tooling_assembly_revisions_mold_id_revision_key
  TO tooling_assembly_revisions_tooling_assembly_id_revision_key;
ALTER TABLE tooling_assembly_revisions
  RENAME CONSTRAINT tooling_assembly_revisions_mold_id_fkey
  TO tooling_assembly_revisions_tooling_assembly_id_fkey;

ALTER TABLE tooling_installations
  RENAME COLUMN machine_id TO equipment_id;
ALTER INDEX idx_tooling_installations_active_machine
  RENAME TO idx_tooling_installations_active_equipment;
ALTER INDEX idx_tooling_installations_machine_time
  RENAME TO idx_tooling_installations_equipment_time;

ALTER TABLE production_contexts
  RENAME COLUMN machine_id TO equipment_id;
ALTER INDEX idx_production_contexts_active_machine
  RENAME TO idx_production_contexts_active_equipment;
ALTER INDEX idx_production_contexts_machine_time
  RENAME TO idx_production_contexts_equipment_time;

ALTER TABLE cycle_analysis_materializations
  RENAME TO execution_analysis_materializations;
ALTER TABLE execution_analysis_materializations
  RENAME COLUMN correlation_id TO execution_id;
ALTER TABLE execution_analysis_materializations
  RENAME CONSTRAINT cycle_analysis_materializations_pkey
  TO execution_analysis_materializations_pkey;
ALTER INDEX idx_cycle_analysis_materializations_status
  RENAME TO idx_execution_analysis_materializations_status;

ALTER TABLE cycle_phases
  RENAME TO execution_phases;
ALTER TABLE execution_phases
  RENAME COLUMN correlation_id TO execution_id;
ALTER TABLE execution_phases
  RENAME CONSTRAINT cycle_phases_pkey TO execution_phases_pkey;
ALTER INDEX idx_cycle_phases_code_time
  RENAME TO idx_execution_phases_code_time;

ALTER TABLE cycle_features
  RENAME TO execution_features;
ALTER TABLE execution_features
  RENAME COLUMN correlation_id TO execution_id;
ALTER TABLE execution_features
  RENAME CONSTRAINT cycle_features_pkey TO execution_features_pkey;
ALTER INDEX idx_cycle_features_lookup
  RENAME TO idx_execution_features_lookup;

ALTER TABLE cycle_analysis_backfill_jobs
  RENAME TO execution_analysis_backfill_jobs;
ALTER TABLE execution_analysis_backfill_jobs
  RENAME CONSTRAINT cycle_analysis_backfill_jobs_pkey
  TO execution_analysis_backfill_jobs_pkey;
ALTER TABLE execution_analysis_backfill_jobs
  RENAME CONSTRAINT cycle_analysis_backfill_jobs_status_check
  TO execution_analysis_backfill_jobs_status_check;
ALTER INDEX idx_cycle_analysis_backfill_jobs_status
  RENAME TO idx_execution_analysis_backfill_jobs_status;

ALTER TABLE time_series_samples
  RENAME COLUMN correlation_id TO execution_id;
ALTER INDEX ix_time_series_samples_correlation
  RENAME TO ix_time_series_samples_execution;

ALTER TABLE production_events
  RENAME COLUMN correlation_id TO execution_id;
ALTER INDEX idx_production_events_correlation
  RENAME TO idx_production_events_execution;

ALTER TABLE operation_context_snapshots
  RENAME COLUMN correlation_id TO execution_id;

ALTER TABLE data_object_operation_keys
  RENAME COLUMN correlation_id TO execution_id;

ALTER TABLE phase_mappings
  RENAME COLUMN recipe_id TO process_specification_id;
ALTER TABLE phase_mappings
  RENAME COLUMN recipe_version TO process_specification_version;
ALTER TABLE phase_mappings
  RENAME COLUMN recipe_template TO process_template;
ALTER TABLE phase_mappings
  RENAME COLUMN recipe_step TO process_step;

ALTER TABLE inspection_records
  RENAME COLUMN workpiece_id TO output_item_id;
ALTER TABLE inspection_records
  RENAME COLUMN operation_run_id TO execution_id;
ALTER INDEX idx_inspection_records_workpiece_time
  RENAME TO idx_inspection_records_output_item_time;
ALTER INDEX idx_inspection_records_operation_time
  RENAME TO idx_inspection_records_execution_time;

ALTER TABLE inspection_reviews
  RENAME COLUMN operation_run_id TO execution_id;
ALTER INDEX idx_inspection_reviews_operation_time
  RENAME TO idx_inspection_reviews_execution_time;

ALTER TABLE recipe_versions
  RENAME TO process_specification_versions;
ALTER TABLE process_specification_versions
  RENAME COLUMN recipe_id TO process_specification_id;
ALTER TABLE process_specification_versions
  RENAME CONSTRAINT recipe_versions_pkey TO process_specification_versions_pkey;
ALTER TABLE process_specification_versions
  RENAME CONSTRAINT recipe_versions_version_check
  TO process_specification_versions_version_check;
ALTER TABLE process_specification_versions
  RENAME CONSTRAINT recipe_versions_data_model_version_check
  TO process_specification_versions_data_model_version_check;
ALTER INDEX idx_recipe_versions_model
  RENAME TO idx_process_specification_versions_model;
