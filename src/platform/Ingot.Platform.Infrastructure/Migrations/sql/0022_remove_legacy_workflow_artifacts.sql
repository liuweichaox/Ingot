-- Remove obsolete workflow storage and values so the current schema contains only the real-run recommendation loop.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM recommendation_knowledge_usage AS legacy
        JOIN recipe_recommendation_knowledge_usage AS current
          ON current.recommendation_id = legacy.recommendation_id
         AND current.claim_id = legacy.claim_id
         AND current.claim_version = legacy.claim_version
         AND current.usage_type = legacy.usage_type
        WHERE current.content_hash IS DISTINCT FROM legacy.content_hash
    ) THEN
        RAISE EXCEPTION
            'Cannot remove legacy knowledge usage because a canonical row has a different content hash.';
    END IF;
END;
$$;

INSERT INTO recipe_recommendation_knowledge_usage(
    recommendation_id,
    claim_id,
    claim_version,
    usage_type,
    content_hash)
SELECT
    recommendation_id,
    claim_id,
    claim_version,
    usage_type,
    content_hash
FROM recommendation_knowledge_usage
ON CONFLICT DO NOTHING;

DROP TABLE recommendation_knowledge_usage;
DROP TABLE research_retired_workflow_records;
DROP TABLE research_historical_replay_reports;

DELETE FROM research_evidence
WHERE kind = 'experiment-result'
   OR resource_type IN ('experiment', 'experiment-result');

DELETE FROM research_hypothesis_evidence
WHERE kind = 'experiment-result';

DELETE FROM mechanism_claim_evidence
WHERE evidence_kind = 'experiment-result';

DELETE FROM mechanism_claim_lifecycle_decisions
WHERE evidence_kind = 'experiment-result';

DELETE FROM process_research_audit
WHERE resource_type IN ('experiment', 'experiment-result')
   OR payload->>'resourceType' IN ('experiment', 'experiment-result');

CREATE FUNCTION pg_temp.without_retired_evidence(document jsonb, property_name text)
RETURNS jsonb
LANGUAGE sql
IMMUTABLE
STRICT
AS $function$
    SELECT CASE
        WHEN jsonb_typeof(document->property_name) = 'array' THEN
            jsonb_set(
                document,
                ARRAY[property_name],
                COALESCE(
                    (
                        SELECT jsonb_agg(evidence_item.value)
                        FROM jsonb_array_elements(document->property_name)
                            AS evidence_item(value)
                        WHERE COALESCE(
                                  evidence_item.value->>'kind',
                                  evidence_item.value->>'Kind')
                              IS DISTINCT FROM 'experiment-result'
                    ),
                    '[]'::jsonb),
                false)
        ELSE document
    END
$function$;

UPDATE research_recipe_recommendations
SET payload = payload - 'experimentId' - 'experiment_id'
WHERE payload ?| ARRAY['experimentId', 'experiment_id'];

UPDATE research_operating_regions
SET payload = pg_temp.without_retired_evidence(
    payload
        - 'supportingExperimentIds'
        - 'supporting_experiment_ids'
        - 'supportingResultIds'
        - 'supporting_result_ids',
    'evidence');

UPDATE research_knowledge_claims
SET payload = pg_temp.without_retired_evidence(payload, 'evidence');

DROP FUNCTION IF EXISTS reject_shadow_recommendation_mutation();

ALTER TABLE research_evidence
    DROP CONSTRAINT research_evidence_kind_check;

ALTER TABLE research_evidence
    ADD CONSTRAINT research_evidence_kind_check
    CHECK (kind = ANY (ARRAY[
        'dataset-snapshot',
        'analysis-run',
        'execution-comparison',
        'mechanism-model',
        'knowledge-source',
        'operating-region',
        'transfer-assessment'
    ]));
